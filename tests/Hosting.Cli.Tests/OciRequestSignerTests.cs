using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Unity.Multiplayer.Hosting.Cli.Provisioning.Oci;

namespace Unity.Multiplayer.Hosting.Cli.Tests;

[TestFixture]
public class OciRequestSignerTests
{
    private static readonly DateTime FixedNow = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
    private const string TenancyOcid = "ocid1.tenancy.oc1..tenancy";
    private const string UserOcid = "ocid1.user.oc1..user";
    private const string Fingerprint = "aa:bb:cc:dd";

    private static (RSA Rsa, string PrivateKeyPem) GenerateKeyPair()
    {
        RSA rsa = RSA.Create(2048);
        return (rsa, rsa.ExportRSAPrivateKeyPem());
    }

    [Test]
    public void Sign_is_deterministic_for_the_same_inputs()
    {
        var (_, pem) = GenerateKeyPair();
        var a = OciRequestSigner.Sign("get", "/20160918/instances/x", "iaas.us-ashburn-1.oraclecloud.com", null, TenancyOcid, UserOcid, Fingerprint, pem, FixedNow);
        var b = OciRequestSigner.Sign("get", "/20160918/instances/x", "iaas.us-ashburn-1.oraclecloud.com", null, TenancyOcid, UserOcid, Fingerprint, pem, FixedNow);

        Assert.That(a.Authorization, Is.EqualTo(b.Authorization));
        Assert.That(a.Date, Is.EqualTo(b.Date));
    }

    [Test]
    public void Authorization_header_names_the_keyId_and_algorithm()
    {
        var (_, pem) = GenerateKeyPair();
        var signed = OciRequestSigner.Sign("get", "/20160918/instances/x", "iaas.us-ashburn-1.oraclecloud.com", null, TenancyOcid, UserOcid, Fingerprint, pem, FixedNow);

        Assert.That(signed.Authorization, Does.Contain($"keyId=\"{TenancyOcid}/{UserOcid}/{Fingerprint}\""));
        Assert.That(signed.Authorization, Does.Contain("algorithm=\"rsa-sha256\""));
        Assert.That(signed.Authorization, Does.Contain("headers=\"(request-target) host date\""));
    }

    [Test]
    public void A_get_request_signs_no_body_headers()
    {
        var (_, pem) = GenerateKeyPair();
        var signed = OciRequestSigner.Sign("get", "/20160918/instances/x", "host", null, TenancyOcid, UserOcid, Fingerprint, pem, FixedNow);

        Assert.That(signed.ContentLength, Is.Null);
        Assert.That(signed.ContentType, Is.Null);
        Assert.That(signed.XContentSha256, Is.Null);
    }

    [Test]
    public void A_post_request_signs_the_body_headers_and_changes_signature_with_the_body()
    {
        var (_, pem) = GenerateKeyPair();
        var withBodyA = OciRequestSigner.Sign("post", "/20160918/instances", "host", "{\"a\":1}", TenancyOcid, UserOcid, Fingerprint, pem, FixedNow);
        var withBodyB = OciRequestSigner.Sign("post", "/20160918/instances", "host", "{\"a\":2}", TenancyOcid, UserOcid, Fingerprint, pem, FixedNow);

        Assert.That(withBodyA.ContentType, Is.EqualTo("application/json"));
        Assert.That(withBodyA.XContentSha256, Is.Not.Null);
        Assert.That(withBodyA.Authorization, Does.Contain("headers=\"(request-target) host date content-length content-type x-content-sha256\""));
        Assert.That(withBodyA.Authorization, Is.Not.EqualTo(withBodyB.Authorization));
        Assert.That(withBodyA.XContentSha256, Is.Not.EqualTo(withBodyB.XContentSha256));
    }

    [Test]
    public void The_signature_verifies_against_the_matching_public_key()
    {
        var (rsa, pem) = GenerateKeyPair();
        var signed = OciRequestSigner.Sign("post", "/20160918/instances", "iaas.us-ashburn-1.oraclecloud.com", "{\"a\":1}", TenancyOcid, UserOcid, Fingerprint, pem, FixedNow);

        string signingString = string.Join('\n',
            "(request-target): post /20160918/instances",
            "host: iaas.us-ashburn-1.oraclecloud.com",
            $"date: {signed.Date}",
            $"content-length: {signed.ContentLength}",
            "content-type: application/json",
            $"x-content-sha256: {signed.XContentSha256}");

        string base64Signature = Regex.Match(signed.Authorization, "signature=\"([^\"]+)\"").Groups[1].Value;
        bool verified = rsa.VerifyData(Encoding.UTF8.GetBytes(signingString), Convert.FromBase64String(base64Signature), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

        Assert.That(verified, Is.True);
    }

    [Test]
    public void The_signature_fails_to_verify_against_a_different_public_key()
    {
        var (_, pem) = GenerateKeyPair();
        RSA otherKey = RSA.Create(2048);
        var signed = OciRequestSigner.Sign("get", "/20160918/instances/x", "host", null, TenancyOcid, UserOcid, Fingerprint, pem, FixedNow);

        string signingString = string.Join('\n', "(request-target): get /20160918/instances/x", "host: host", $"date: {signed.Date}");
        string base64Signature = Regex.Match(signed.Authorization, "signature=\"([^\"]+)\"").Groups[1].Value;
        bool verified = otherKey.VerifyData(Encoding.UTF8.GetBytes(signingString), Convert.FromBase64String(base64Signature), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

        Assert.That(verified, Is.False);
    }

    [Test]
    public void Sign_throws_when_a_required_value_is_missing()
    {
        var (_, pem) = GenerateKeyPair();
        Assert.Throws<ArgumentException>(() => OciRequestSigner.Sign("get", "/x", "host", null, "", UserOcid, Fingerprint, pem, FixedNow));
        Assert.Throws<ArgumentException>(() => OciRequestSigner.Sign("get", "/x", "host", null, TenancyOcid, "", Fingerprint, pem, FixedNow));
        Assert.Throws<ArgumentException>(() => OciRequestSigner.Sign("get", "/x", "host", null, TenancyOcid, UserOcid, Fingerprint, "", FixedNow));
    }
}
