using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Unity.Multiplayer.Hosting.Cli.Provisioning.Oci;

/// <summary>
/// Oracle Cloud Infrastructure's "Request Signing Version 1" -- the RSA-SHA256 scheme every OCI
/// REST call requires in place of a bearer token (an API signing key, not a password: see
/// <c>docs/articles/hosting/oracle-oci.md</c> for how an operator creates one). Shared by
/// <see cref="HttpOciApiClient"/> so the signing-string/headers algorithm (OCI's published spec) is
/// implemented and tested exactly once, pure and deterministic given <paramref name="utcNow"/> --
/// just like <see cref="Aws.AwsSigV4Signer"/> for AWS -- so NUnit can assert on an exact signature
/// (and round-trip it through <see cref="RSA.VerifyData(byte[],byte[],HashAlgorithmName,RSASignaturePadding)"/>)
/// instead of just "a header was added".
/// </summary>
public static class OciRequestSigner
{
    /// <summary>The headers a signed request must carry, beyond whatever the caller already set.</summary>
    public sealed record SignedHeaders(string Date, string Authorization, string? XContentSha256, string? ContentLength, string? ContentType);

    /// <summary>
    /// Signs one request. <paramref name="body"/> is null for a GET/DELETE with no body; a POST/PUT
    /// signs three extra headers (<c>content-length</c>, <c>content-type</c>, <c>x-content-sha256</c>)
    /// on top of the always-signed <c>(request-target)</c>/<c>host</c>/<c>date</c>, per OCI's spec for
    /// a body-carrying request.
    /// </summary>
    public static SignedHeaders Sign(
        string method,
        string requestPath,
        string host,
        string? body,
        string tenancyOcid,
        string userOcid,
        string fingerprint,
        string privateKeyPem,
        DateTime utcNow)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(method);
        ArgumentException.ThrowIfNullOrWhiteSpace(requestPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(host);
        ArgumentException.ThrowIfNullOrWhiteSpace(tenancyOcid);
        ArgumentException.ThrowIfNullOrWhiteSpace(userOcid);
        ArgumentException.ThrowIfNullOrWhiteSpace(fingerprint);
        ArgumentException.ThrowIfNullOrWhiteSpace(privateKeyPem);

        string lowerMethod = method.ToLowerInvariant();
        string date = utcNow.ToString("r", CultureInfo.InvariantCulture);
        bool hasBody = lowerMethod is "post" or "put";

        var headerNames = new List<string> { "(request-target)", "host", "date" };
        var headerValues = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["(request-target)"] = $"{lowerMethod} {requestPath}",
            ["host"] = host,
            ["date"] = date,
        };

        string? xContentSha256 = null;
        string? contentLength = null;
        string? contentType = null;
        if (hasBody)
        {
            string bodyText = body ?? "";
            xContentSha256 = Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(bodyText)));
            contentLength = Encoding.UTF8.GetByteCount(bodyText).ToString(CultureInfo.InvariantCulture);
            contentType = "application/json";
            headerNames.Add("content-length");
            headerNames.Add("content-type");
            headerNames.Add("x-content-sha256");
            headerValues["content-length"] = contentLength;
            headerValues["content-type"] = contentType;
            headerValues["x-content-sha256"] = xContentSha256;
        }

        string signingString = string.Join('\n', headerNames.Select(n => $"{n}: {headerValues[n]}"));
        using RSA rsa = RSA.Create();
        rsa.ImportFromPem(privateKeyPem);
        byte[] signatureBytes = rsa.SignData(Encoding.UTF8.GetBytes(signingString), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        string signature = Convert.ToBase64String(signatureBytes);

        string keyId = $"{tenancyOcid}/{userOcid}/{fingerprint}";
        string authorization = "Signature version=\"1\"," +
            $"headers=\"{string.Join(' ', headerNames)}\"," +
            $"keyId=\"{keyId}\",algorithm=\"rsa-sha256\",signature=\"{signature}\"";

        return new SignedHeaders(date, authorization, xContentSha256, contentLength, contentType);
    }
}
