using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Unity.Multiplayer.Core;

namespace Unity.Multiplayer.Rendezvous;

/// <summary>The HTTP API, routes per <see cref="RendezvousProtocol"/>.</summary>
internal static class RendezvousEndpoints
{
    public const string RegisterPolicy = "register";
    public const string HeartbeatPolicy = "heartbeat";
    public const string ResolvePolicy = "resolve";

    public static void Map(IEndpointRouteBuilder app)
    {
        app.MapPost(RendezvousProtocol.RegisterPath, Register).RequireRateLimiting(RegisterPolicy);
        app.MapPost(RendezvousProtocol.HeartbeatPath, Heartbeat).RequireRateLimiting(HeartbeatPolicy);
        app.MapGet(RendezvousProtocol.ResolvePathPrefix + "{code}", Resolve).RequireRateLimiting(ResolvePolicy);
        app.MapDelete(RendezvousProtocol.RegistrationPathPrefix + "{code}", Delete).RequireRateLimiting(HeartbeatPolicy);
    }

    private static IResult Register(RegisterRequest? request, HttpContext context, RegistrationStore store, IOptions<RendezvousOptions> options, ILogger<RegistrationStore> logger)
    {
        RendezvousOptions settings = options.Value;
        if (request == null)
        {
            return Error(StatusCodes.Status400BadRequest, "A JSON body is required.");
        }

        if (!settings.AllowOpenRegistration && !SecretMatches(settings.RegistrationSecret, request.Secret))
        {
            return Error(StatusCodes.Status401Unauthorized, "Wrong or missing registration secret.");
        }

        string? address = string.IsNullOrWhiteSpace(request.Address) ? ClientAddress.Of(context)?.ToString() : request.Address;
        if (address == null)
        {
            return Error(StatusCodes.Status400BadRequest, "No address given and the caller's address is unknown.");
        }

        if (request.Port < 1 || request.Port > ushort.MaxValue)
        {
            return Error(StatusCodes.Status400BadRequest, "Port must be between 1 and 65535.");
        }

        if (!RendezvousProtocol.TryParseTransport(request.Transport, out TransportKind transport))
        {
            return Error(StatusCodes.Status400BadRequest,
                "Transport must be '" + RendezvousProtocol.TransportUdp + "' or '" + RendezvousProtocol.TransportWebSocket + "'.");
        }

        JoinTarget target;
        try
        {
            target = JoinTarget.ForEndpoint(address, (ushort)request.Port, transport);
        }
        catch (ArgumentException ex)
        {
            return Error(StatusCodes.Status400BadRequest, WithoutParameterName(ex.Message));
        }

        Registration? registration = store.Register(target);
        if (registration == null)
        {
            logger.LogWarning("Registration refused: the store is full ({Max}).", settings.MaxRegistrations);
            return Error(StatusCodes.Status503ServiceUnavailable, "The rendezvous service is full; try again later.");
        }

        logger.LogInformation("Registered {Code} -> {Target}.", registration.Code.Value, target);
        return Results.Created(
            RendezvousProtocol.ResolvePath(registration.Code),
            new RegisterResponse(registration.Code.Value, registration.Token, settings.TtlSeconds, registration.ExpiresAt));
    }

    private static IResult Heartbeat(HeartbeatRequest? request, RegistrationStore store, IOptions<RendezvousOptions> options)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.Code) || string.IsNullOrEmpty(request.Token))
        {
            return Error(StatusCodes.Status400BadRequest, "A heartbeat needs a code and its token.");
        }

        if (!JoinCode.TryParse(request.Code, out JoinCodeParseResult parsed))
        {
            return Error(StatusCodes.Status400BadRequest, parsed.Message);
        }

        Registration? renewed = store.Heartbeat(parsed.Code, request.Token);
        if (renewed == null)
        {
            return Error(StatusCodes.Status404NotFound, "No live registration with that code and token; register again.");
        }

        return Results.Ok(new HeartbeatResponse(renewed.Code.Value, options.Value.TtlSeconds, renewed.ExpiresAt));
    }

    private static IResult Resolve(string code, HttpContext context, RegistrationStore store)
    {
        context.Response.Headers.CacheControl = "no-store";
        if (!JoinCode.TryParse(code, out JoinCodeParseResult parsed))
        {
            return Error(StatusCodes.Status400BadRequest, parsed.Message);
        }

        Registration? registration = store.Resolve(parsed.Code);
        if (registration == null)
        {
            return Error(StatusCodes.Status404NotFound, "No server is using that code. It may have closed or expired.");
        }

        JoinTarget target = registration.Target;
        return Results.Ok(new ResolveResponse(
            registration.Code.Value, target.Address, target.Port, RendezvousProtocol.ToWire(target.Transport)));
    }

    private static IResult Delete(string code, HttpContext context, RegistrationStore store, ILogger<RegistrationStore> logger)
    {
        if (!JoinCode.TryParse(code, out JoinCodeParseResult parsed))
        {
            return Error(StatusCodes.Status400BadRequest, parsed.Message);
        }

        string? token = context.Request.Headers[RendezvousProtocol.TokenHeader];
        if (!store.Remove(parsed.Code, token))
        {
            return Error(StatusCodes.Status404NotFound, "No live registration with that code and token.");
        }

        logger.LogInformation("Unregistered {Code}.", parsed.Code.Value);
        return Results.NoContent();
    }

    internal static IResult Error(int status, string message) =>
        Results.Json(new ErrorResponse(message), RendezvousJsonContext.Default.ErrorResponse, statusCode: status);

    private static bool SecretMatches(string? expected, string? actual)
    {
        if (string.IsNullOrEmpty(expected) || string.IsNullOrEmpty(actual))
        {
            return false;
        }

        // Hash both sides so the comparison takes the same time whatever the lengths.
        return CryptographicOperations.FixedTimeEquals(
            SHA256.HashData(Encoding.UTF8.GetBytes(expected)),
            SHA256.HashData(Encoding.UTF8.GetBytes(actual)));
    }

    private static string WithoutParameterName(string message)
    {
        // ArgumentException appends " (Parameter 'x')", which means nothing to an operator.
        int cut = message.IndexOf(" (Parameter", StringComparison.Ordinal);
        return cut > 0 ? message[..cut] : message;
    }
}
