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
}
