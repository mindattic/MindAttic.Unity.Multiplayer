using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Unity.Multiplayer.Hosting.Cli.Provisioning.Aws;

/// <summary>
/// AWS Signature Version 4, the request-signing scheme every AWS service (EC2's Query API,
/// Lightsail's JSON RPC) requires in place of a bearer token. Shared by
/// <see cref="Ec2.HttpEc2ApiClient"/> and <see cref="Lightsail.HttpLightsailApiClient"/> so the
/// canonical-request/string-to-sign/signing-key algorithm (AWS's published spec) is implemented and
/// tested exactly once. Pure and deterministic given <paramref name="utcNow"/>, so NUnit can assert
/// on an exact signature instead of just "a header was added".
/// </summary>
public static class AwsSigV4Signer
{
    /// <summary>The headers a signed request must carry, beyond whatever the caller already set.</summary>
    public sealed record SignedHeaders(string AmzDate, string Authorization);

    /// <summary>
    /// Signs one request. <paramref name="extraSignedHeaders"/> are the other headers (lower-cased
    /// names, e.g. <c>content-type</c>, <c>x-amz-target</c>) that must be part of the signature
    /// beyond <c>host</c> and <c>x-amz-date</c>, which this method always includes itself.
    /// </summary>
    public static SignedHeaders Sign(
        string method,
        string host,
        string canonicalUri,
        string canonicalQueryString,
        IReadOnlyDictionary<string, string> extraSignedHeaders,
        string body,
        string accessKeyId,
        string secretAccessKey,
        string region,
        string service,
        DateTime utcNow) =>
        SignWithPayloadHash(method, host, canonicalUri, canonicalQueryString, extraSignedHeaders, Sha256Hex(body),
            accessKeyId, secretAccessKey, region, service, utcNow);

    /// <summary>
    /// Signs one request whose payload hash is already known -- a binary body (an S3 <c>PutObject</c>
    /// of a zipped build, for GameLift's build upload) cannot be hashed as a <see cref="string"/> the
    /// way <see cref="Sign"/> does without corrupting it, so the caller hashes the raw bytes itself
    /// (<see cref="SHA256"/> over the exact bytes sent) and passes the lower-case hex digest here
    /// instead of a body.
    /// </summary>
    public static SignedHeaders SignWithPayloadHash(
        string method,
        string host,
        string canonicalUri,
        string canonicalQueryString,
        IReadOnlyDictionary<string, string> extraSignedHeaders,
        string payloadHash,
        string accessKeyId,
        string secretAccessKey,
        string region,
        string service,
        DateTime utcNow)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accessKeyId);
        ArgumentException.ThrowIfNullOrWhiteSpace(secretAccessKey);
        string amzDate = utcNow.ToString("yyyyMMddTHHmmssZ", CultureInfo.InvariantCulture);
        string dateStamp = utcNow.ToString("yyyyMMdd", CultureInfo.InvariantCulture);

        var headers = new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["host"] = host,
            ["x-amz-date"] = amzDate,
        };
        foreach (var (name, value) in extraSignedHeaders)
        {
            headers[name.ToLowerInvariant()] = value;
        }

        string canonicalHeaders = string.Concat(headers.Select(kv => $"{kv.Key}:{kv.Value}\n"));
        string signedHeaderNames = string.Join(';', headers.Keys);

        string canonicalRequest = string.Join('\n', method, canonicalUri, canonicalQueryString, canonicalHeaders, signedHeaderNames, payloadHash);
        string credentialScope = $"{dateStamp}/{region}/{service}/aws4_request";
        string stringToSign = string.Join('\n', "AWS4-HMAC-SHA256", amzDate, credentialScope, Sha256Hex(canonicalRequest));

        byte[] signingKey = DeriveSigningKey(secretAccessKey, dateStamp, region, service);
        string signature = HmacHex(signingKey, stringToSign);

        string authorization = "AWS4-HMAC-SHA256 " +
            $"Credential={accessKeyId}/{credentialScope}, SignedHeaders={signedHeaderNames}, Signature={signature}";
        return new SignedHeaders(amzDate, authorization);
    }

    /// <summary>The lower-case hex SHA-256 digest of raw bytes, for <see cref="SignWithPayloadHash"/>'s <c>x-amz-content-sha256</c>/payload hash on a binary body.</summary>
    public static string Sha256Hex(byte[] data) => Convert.ToHexStringLower(SHA256.HashData(data));

    private static byte[] DeriveSigningKey(string secretAccessKey, string dateStamp, string region, string service)
    {
        byte[] kSecret = Encoding.UTF8.GetBytes("AWS4" + secretAccessKey);
        byte[] kDate = Hmac(kSecret, dateStamp);
        byte[] kRegion = Hmac(kDate, region);
        byte[] kService = Hmac(kRegion, service);
        return Hmac(kService, "aws4_request");
    }

    private static byte[] Hmac(byte[] key, string data) => HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(data));

    private static string HmacHex(byte[] key, string data) => Convert.ToHexStringLower(HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(data)));

    private static string Sha256Hex(string data) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(data)));
}
