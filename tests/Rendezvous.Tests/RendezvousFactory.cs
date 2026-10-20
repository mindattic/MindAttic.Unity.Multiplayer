using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MindAttic.Unity.Multiplayer.Core;

namespace MindAttic.Unity.Multiplayer.Rendezvous.Tests;

/// <summary>
/// Hosts the service in-process with a test secret, a hand-driven clock, and a fake caller IP taken
/// from the <see cref="ClientIpHeader"/> header (TestServer has no real remote address).
/// </summary>
internal sealed class RendezvousFactory : WebApplicationFactory<Program>
{
    public const string Secret = "test-secret-0123456789";
    public const string ClientIpHeader = "X-Test-Client-Ip";

    private readonly Dictionary<string, string?> settings = new()
    {
        ["Rendezvous:RegistrationSecret"] = Secret,
        ["Rendezvous:TtlSeconds"] = "60",
    };

    public ManualTimeProvider Time { get; } = new(new DateTimeOffset(2026, 10, 20, 12, 0, 0, TimeSpan.Zero));

    /// <summary>Overrides one configuration value (null removes it). Call before the first client.</summary>
    public RendezvousFactory With(string key, string? value)
    {
        settings[key] = value;
        return this;
    }

    public RegistrationStore Store => Services.GetRequiredService<RegistrationStore>();

    /// <summary>A client whose requests appear to come from <paramref name="ip"/>.</summary>
    public HttpClient ClientFrom(string ip)
    {
        HttpClient client = CreateClient();
        client.DefaultRequestHeaders.Add(ClientIpHeader, ip);
        return client;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Production");
        foreach (KeyValuePair<string, string?> pair in settings)
        {
            builder.UseSetting(pair.Key, pair.Value);
        }

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(Time);
            services.AddSingleton<IStartupFilter, FakeRemoteIpStartupFilter>();
        });
    }

    private sealed class FakeRemoteIpStartupFilter : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use(async (context, nextMiddleware) =>
            {
                if (context.Request.Headers.TryGetValue(ClientIpHeader, out var ip) && IPAddress.TryParse(ip, out IPAddress? address))
                {
                    context.Connection.RemoteIpAddress = address;
                }

                await nextMiddleware(context);
            });
            next(app);
        };
    }
}

/// <summary>A clock the test moves by hand.</summary>
internal sealed class ManualTimeProvider(DateTimeOffset start) : TimeProvider
{
    private DateTimeOffset now = start;

    public override DateTimeOffset GetUtcNow() => now;

    public void Advance(TimeSpan by) => now += by;
}

/// <summary>Wire helpers that speak the protocol as raw JSON, the way a non-.NET client would.</summary>
internal static class Wire
{
    public static Task<HttpResponseMessage> Register(this HttpClient client, object body) =>
        client.PostAsJsonAsync(RendezvousProtocol.RegisterPath, body);

    public static Task<HttpResponseMessage> RegisterServer(this HttpClient client, string address = "203.0.113.7", int port = 7777, string? transport = null) =>
        client.Register(new { secret = RendezvousFactory.Secret, address, port, transport });

    public static async Task<(string Code, string Token)> RegisterOk(this HttpClient client, string address = "203.0.113.7", int port = 7777, string? transport = null)
    {
        HttpResponseMessage response = await client.RegisterServer(address, port, transport);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Created), await response.Content.ReadAsStringAsync());
        JsonElement json = await response.Json();
        return (json.GetProperty("code").GetString()!, json.GetProperty("token").GetString()!);
    }

    public static Task<HttpResponseMessage> Resolve(this HttpClient client, string code) =>
        client.GetAsync(RendezvousProtocol.ResolvePathPrefix + Uri.EscapeDataString(code));

    public static Task<HttpResponseMessage> Heartbeat(this HttpClient client, string code, string? token) =>
        client.PostAsJsonAsync(RendezvousProtocol.HeartbeatPath, new { code, token });

    public static Task<HttpResponseMessage> Unregister(this HttpClient client, string code, string? token)
    {
        var request = new HttpRequestMessage(HttpMethod.Delete, RendezvousProtocol.RegistrationPathPrefix + Uri.EscapeDataString(code));
        if (token != null)
        {
            request.Headers.Add(RendezvousProtocol.TokenHeader, token);
        }

        return client.SendAsync(request);
    }

    public static async Task<JsonElement> Json(this HttpResponseMessage response)
    {
        string text = await response.Content.ReadAsStringAsync();
        return JsonDocument.Parse(text).RootElement.Clone();
    }

    public static async Task<string> ErrorText(this HttpResponseMessage response) =>
        (await response.Json()).GetProperty("error").GetString()!;
}
