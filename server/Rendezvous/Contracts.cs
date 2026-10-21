using System.Text.Json.Serialization;
using F = Unity.Multiplayer.Core.RendezvousProtocol.Fields;

namespace Unity.Multiplayer.Rendezvous;

/// <summary><c>POST /v1/register</c> body.</summary>
public sealed record RegisterRequest(
    [property: JsonPropertyName(F.Secret)] string? Secret,
    [property: JsonPropertyName(F.Address)] string? Address,
    [property: JsonPropertyName(F.Port)] int Port,
    [property: JsonPropertyName(F.Transport)] string? Transport);

/// <summary><c>POST /v1/register</c> 201 body.</summary>
public sealed record RegisterResponse(
    [property: JsonPropertyName(F.Code)] string Code,
    [property: JsonPropertyName(F.Token)] string Token,
    [property: JsonPropertyName(F.TtlSeconds)] int TtlSeconds,
    [property: JsonPropertyName(F.ExpiresAt)] DateTimeOffset ExpiresAt);

/// <summary><c>POST /v1/heartbeat</c> body.</summary>
public sealed record HeartbeatRequest(
    [property: JsonPropertyName(F.Code)] string? Code,
    [property: JsonPropertyName(F.Token)] string? Token);

/// <summary><c>POST /v1/heartbeat</c> 200 body.</summary>
public sealed record HeartbeatResponse(
    [property: JsonPropertyName(F.Code)] string Code,
    [property: JsonPropertyName(F.TtlSeconds)] int TtlSeconds,
    [property: JsonPropertyName(F.ExpiresAt)] DateTimeOffset ExpiresAt);

/// <summary><c>GET /v1/resolve/{code}</c> 200 body.</summary>
public sealed record ResolveResponse(
    [property: JsonPropertyName(F.Code)] string Code,
    [property: JsonPropertyName(F.Address)] string Address,
    [property: JsonPropertyName(F.Port)] int Port,
    [property: JsonPropertyName(F.Transport)] string Transport);

/// <summary>Every error body.</summary>
public sealed record ErrorResponse([property: JsonPropertyName(F.Error)] string Error);

/// <summary>Source-generated JSON metadata, so the service also runs trimmed or AOT-published.</summary>
[JsonSerializable(typeof(RegisterRequest))]
[JsonSerializable(typeof(RegisterResponse))]
[JsonSerializable(typeof(HeartbeatRequest))]
[JsonSerializable(typeof(HeartbeatResponse))]
[JsonSerializable(typeof(ResolveResponse))]
[JsonSerializable(typeof(ErrorResponse))]
internal sealed partial class RendezvousJsonContext : JsonSerializerContext
{
}
