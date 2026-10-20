using System.Net;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using MindAttic.Unity.Multiplayer.Core;
using MindAttic.Unity.Multiplayer.Rendezvous;

if (args.Contains(HealthProbe.Argument))
{
    // The container's HEALTHCHECK: the aspnet image has no curl, so the service probes itself.
    return await HealthProbe.RunAsync();
}

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.Services.AddOptions<RendezvousOptions>()
    .Bind(builder.Configuration.GetSection(RendezvousOptions.SectionName))
    .ValidateDataAnnotations()
    .Validate(
        o => o.AllowOpenRegistration || !string.IsNullOrWhiteSpace(o.RegistrationSecret),
        "Rendezvous:RegistrationSecret is required (set Rendezvous__RegistrationSecret), "
        + "or set Rendezvous:AllowOpenRegistration=true for local development.")
    .ValidateOnStart();

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<RegistrationStore>();
builder.Services.AddHostedService<ExpirySweeper>();

builder.Services.ConfigureHttpJsonOptions(json =>
    json.SerializerOptions.TypeInfoResolverChain.Insert(0, RendezvousJsonContext.Default));

builder.Services.AddHealthChecks()
    .AddCheck<StoreHealthCheck>("store");

builder.Services.AddRateLimiter(limiter =>
{
    limiter.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    limiter.AddPolicy(RendezvousEndpoints.RegisterPolicy, context => PerIp(context, r => r.RegisterPerWindow));
    limiter.AddPolicy(RendezvousEndpoints.HeartbeatPolicy, context => PerIp(context, r => r.HeartbeatPerWindow));
    limiter.AddPolicy(RendezvousEndpoints.ResolvePolicy, context => PerIp(context, r => r.ResolvePerWindow));
    limiter.OnRejected = async (rejected, cancellationToken) =>
    {
        HttpResponse response = rejected.HttpContext.Response;
        if (rejected.Lease.TryGetMetadata(MetadataName.RetryAfter, out TimeSpan retryAfter))
        {
            response.Headers.RetryAfter = ((int)Math.Ceiling(retryAfter.TotalSeconds)).ToString();
        }

        await response.WriteAsJsonAsync(
            new ErrorResponse("Too many requests from this address; slow down."),
            RendezvousJsonContext.Default.ErrorResponse,
            cancellationToken: cancellationToken);
    };
});

RendezvousOptions startup = builder.Configuration.GetSection(RendezvousOptions.SectionName).Get<RendezvousOptions>() ?? new RendezvousOptions();
if (startup.UseForwardedHeaders)
{
    builder.Services.Configure<ForwardedHeadersOptions>(forwarded =>
    {
        forwarded.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
        foreach (string proxy in startup.KnownProxies)
        {
            forwarded.KnownProxies.Add(IPAddress.Parse(proxy));
        }
    });
}

WebApplication app = builder.Build();

if (startup.UseForwardedHeaders)
{
    app.UseForwardedHeaders();
}

if (startup.RequireHttps)
{
    app.UseHsts();
    app.UseHttpsRedirection();
}

app.UseRateLimiter();

app.MapHealthChecks(RendezvousProtocol.HealthPath, new HealthCheckOptions()).DisableRateLimiting();
RendezvousEndpoints.Map(app);

await app.RunAsync();
return 0;

static RateLimitPartition<string> PerIp(HttpContext context, Func<RateLimitOptions, int> permits)
{
    RateLimitOptions limits = context.RequestServices.GetRequiredService<IOptions<RendezvousOptions>>().Value.RateLimits;
    return RateLimitPartition.GetFixedWindowLimiter(
        ClientAddress.PartitionKey(context),
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = permits(limits),
            Window = TimeSpan.FromSeconds(limits.WindowSeconds),
            QueueLimit = 0,
            AutoReplenishment = true,
        });
}

/// <summary>The program, public so the tests' <c>WebApplicationFactory</c> can host it.</summary>
public partial class Program
{
}

/// <summary>Healthy while the store answers; reports how many registrations are live.</summary>
internal sealed class StoreHealthCheck(RegistrationStore store, IOptions<RendezvousOptions> options) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        int count = store.Count;
        int max = options.Value.MaxRegistrations;
        var data = new Dictionary<string, object> { ["registrations"] = count, ["max"] = max };
        return Task.FromResult(count >= max
            ? HealthCheckResult.Degraded("The store is full.", data: data)
            : HealthCheckResult.Healthy(count + " live registration(s).", data));
    }
}
