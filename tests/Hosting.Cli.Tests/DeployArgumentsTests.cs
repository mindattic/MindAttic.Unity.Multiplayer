using Unity.Multiplayer.Hosting.Cli.Commands;

namespace Unity.Multiplayer.Hosting.Cli.Tests;

[TestFixture]
public class DeployArgumentsTests
{
    [Test]
    public void BuildImageReference_combines_registry_repository_and_tag()
    {
        var args = DeployArguments.Parse(new[] { "deploy", "--image", "mindattic/game", "--tag", "v3" });
        Assert.That(DeployArguments.BuildImageReference(args, "ghcr.io"), Is.EqualTo("ghcr.io/mindattic/game:v3"));
    }

    [Test]
    public void BuildImageReference_defaults_the_tag_to_latest()
    {
        var args = DeployArguments.Parse(new[] { "deploy", "--image", "mindattic/game" });
        Assert.That(DeployArguments.BuildImageReference(args, "ghcr.io"), Is.EqualTo("ghcr.io/mindattic/game:latest"));
    }

    [Test]
    public void BuildEnvironment_includes_the_rendezvous_defaults()
    {
        var raw = new[]
        {
            "deploy", "--rendezvous-url", "http://rendezvous:8080", "--rendezvous-secret", "s3cr3t", "--public-address", "203.0.113.5",
        };
        var args = DeployArguments.Parse(raw);
        var env = DeployArguments.BuildEnvironment(args, raw, "s3cr3t");
        Assert.That(env["MINDATTIC_RENDEZVOUS_URL"], Is.EqualTo("http://rendezvous:8080"));
        Assert.That(env["MINDATTIC_RENDEZVOUS_SECRET"], Is.EqualTo("s3cr3t"));
        Assert.That(env["MINDATTIC_PUBLIC_ADDRESS"], Is.EqualTo("203.0.113.5"));
        Assert.That(env.ContainsKey("MINDATTIC_SERVER_PASSWORD"), Is.False);
    }

    [Test]
    public void BuildEnvironment_adds_the_server_password_when_given()
    {
        var raw = new[]
        {
            "deploy", "--rendezvous-url", "u", "--rendezvous-secret", "s", "--public-address", "a", "--server-password", "hunter2",
        };
        var env = DeployArguments.BuildEnvironment(DeployArguments.Parse(raw), raw, "s");
        Assert.That(env["MINDATTIC_SERVER_PASSWORD"], Is.EqualTo("hunter2"));
    }

    [Test]
    public void BuildEnvironment_applies_every_repeated_env_override_on_top_of_the_defaults()
    {
        var raw = new[]
        {
            "deploy", "--rendezvous-url", "u", "--rendezvous-secret", "s", "--public-address", "a",
            "--env", "MINDATTIC_RENDEZVOUS_URL=http://override:9999",
            "--env", "CUSTOM_FLAG=1",
        };
        var env = DeployArguments.BuildEnvironment(DeployArguments.Parse(raw), raw, "s");
        Assert.That(env["MINDATTIC_RENDEZVOUS_URL"], Is.EqualTo("http://override:9999"));
        Assert.That(env["CUSTOM_FLAG"], Is.EqualTo("1"));
    }

    [Test]
    public void BuildEnvironment_throws_for_a_malformed_env_override()
    {
        var raw = new[]
        {
            "deploy", "--rendezvous-url", "u", "--rendezvous-secret", "s", "--public-address", "a", "--env", "not-a-pair",
        };
        Assert.Throws<ArgumentException>(() => DeployArguments.BuildEnvironment(DeployArguments.Parse(raw), raw, "s"));
    }

    [Test]
    public void BuildPackageRequest_carries_the_package_directory_or_the_unity_paths()
    {
        var args = DeployArguments.Parse(new[] { "deploy", "--package", "C:/built/package" });
        var request = DeployArguments.BuildPackageRequest(args, "GenericVps");
        Assert.That(request.PackageDirectory, Is.EqualTo("C:/built/package"));
        Assert.That(request.ProfileId, Is.EqualTo("GenericVps"));
    }

    [Test]
    public void HealthTimeout_and_JoinCodeTimeout_default_sensibly()
    {
        var args = DeployArguments.Parse(new[] { "deploy" });
        Assert.That(DeployArguments.HealthTimeout(args), Is.EqualTo(TimeSpan.FromSeconds(120)));
        Assert.That(DeployArguments.JoinCodeTimeout(args), Is.EqualTo(TimeSpan.FromSeconds(60)));
    }
}
