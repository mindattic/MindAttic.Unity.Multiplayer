using Unity.Multiplayer.Hosting.Cli.Provisioning.Aws;

namespace Unity.Multiplayer.Hosting.Cli.Tests;

[TestFixture]
public class AwsSigV4SignerTests
{
    private static readonly DateTime FixedNow = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

    [Test]
    public void Sign_is_deterministic_for_the_same_inputs()
    {
        var a = AwsSigV4Signer.Sign("POST", "ec2.us-east-1.amazonaws.com", "/", "", new Dictionary<string, string> { ["content-type"] = "application/x-www-form-urlencoded" }, "Action=DescribeInstances", "AKIA", "secret", "us-east-1", "ec2", FixedNow);
        var b = AwsSigV4Signer.Sign("POST", "ec2.us-east-1.amazonaws.com", "/", "", new Dictionary<string, string> { ["content-type"] = "application/x-www-form-urlencoded" }, "Action=DescribeInstances", "AKIA", "secret", "us-east-1", "ec2", FixedNow);

        Assert.That(a.Authorization, Is.EqualTo(b.Authorization));
        Assert.That(a.AmzDate, Is.EqualTo("20260101T120000Z"));
    }

    [Test]
    public void Sign_changes_the_signature_when_the_body_changes()
    {
        var a = AwsSigV4Signer.Sign("POST", "ec2.us-east-1.amazonaws.com", "/", "", new Dictionary<string, string>(), "Action=A", "AKIA", "secret", "us-east-1", "ec2", FixedNow);
        var b = AwsSigV4Signer.Sign("POST", "ec2.us-east-1.amazonaws.com", "/", "", new Dictionary<string, string>(), "Action=B", "AKIA", "secret", "us-east-1", "ec2", FixedNow);

        Assert.That(a.Authorization, Is.Not.EqualTo(b.Authorization));
    }

    [Test]
    public void Sign_changes_the_signature_when_the_secret_changes()
    {
        var a = AwsSigV4Signer.Sign("POST", "ec2.us-east-1.amazonaws.com", "/", "", new Dictionary<string, string>(), "x", "AKIA", "secret-one", "us-east-1", "ec2", FixedNow);
        var b = AwsSigV4Signer.Sign("POST", "ec2.us-east-1.amazonaws.com", "/", "", new Dictionary<string, string>(), "x", "AKIA", "secret-two", "us-east-1", "ec2", FixedNow);

        Assert.That(a.Authorization, Is.Not.EqualTo(b.Authorization));
    }

    [Test]
    public void Authorization_header_names_the_access_key_scope_and_signed_headers()
    {
        var signed = AwsSigV4Signer.Sign("POST", "lightsail.us-east-1.amazonaws.com", "/", "",
            new Dictionary<string, string> { ["content-type"] = "application/x-amz-json-1.1", ["x-amz-target"] = "Lightsail_20161128.GetInstance" },
            "{}", "AKIAEXAMPLE", "secret", "us-east-1", "lightsail", FixedNow);

        Assert.That(signed.Authorization, Does.StartWith("AWS4-HMAC-SHA256 Credential=AKIAEXAMPLE/20260101/us-east-1/lightsail/aws4_request"));
        Assert.That(signed.Authorization, Does.Contain("SignedHeaders=content-type;host;x-amz-date;x-amz-target"));
    }

    [Test]
    public void Sign_throws_when_credentials_are_missing()
    {
        Assert.Throws<ArgumentException>(() =>
            AwsSigV4Signer.Sign("GET", "h", "/", "", new Dictionary<string, string>(), "", "", "secret", "us-east-1", "ec2", FixedNow));
        Assert.Throws<ArgumentException>(() =>
            AwsSigV4Signer.Sign("GET", "h", "/", "", new Dictionary<string, string>(), "", "AKIA", "", "us-east-1", "ec2", FixedNow));
    }

    [Test]
    public void SignWithPayloadHash_matches_Sign_for_the_same_body()
    {
        var byBody = AwsSigV4Signer.Sign("PUT", "bucket.s3.us-east-1.amazonaws.com", "/key", "",
            new Dictionary<string, string>(), "zip-bytes-as-text", "AKIA", "secret", "us-east-1", "s3", FixedNow);
        var byHash = AwsSigV4Signer.SignWithPayloadHash("PUT", "bucket.s3.us-east-1.amazonaws.com", "/key", "",
            new Dictionary<string, string>(), AwsSigV4Signer.Sha256Hex(System.Text.Encoding.UTF8.GetBytes("zip-bytes-as-text")),
            "AKIA", "secret", "us-east-1", "s3", FixedNow);

        Assert.That(byHash.Authorization, Is.EqualTo(byBody.Authorization));
    }

    [Test]
    public void Sha256Hex_of_bytes_is_the_same_digest_Sign_would_compute_from_the_equivalent_string()
    {
        byte[] bytes = System.Text.Encoding.UTF8.GetBytes("hello world");
        string fromBytes = AwsSigV4Signer.Sha256Hex(bytes);

        // SignWithPayloadHash trusts the caller's hash verbatim into the canonical request, so a
        // Sign() over the equivalent string body produces the same Authorization only when the two
        // payload hashes agree -- this is what actually matters for binary (non-UTF8-roundtrippable) bodies.
        var viaString = AwsSigV4Signer.Sign("PUT", "h", "/", "", new Dictionary<string, string>(), "hello world", "AKIA", "secret", "us-east-1", "s3", FixedNow);
        var viaHash = AwsSigV4Signer.SignWithPayloadHash("PUT", "h", "/", "", new Dictionary<string, string>(), fromBytes, "AKIA", "secret", "us-east-1", "s3", FixedNow);
        Assert.That(viaHash.Authorization, Is.EqualTo(viaString.Authorization));
    }
}
