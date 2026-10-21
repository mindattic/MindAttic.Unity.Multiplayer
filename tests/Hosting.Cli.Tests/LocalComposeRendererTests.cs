using Unity.Multiplayer.Hosting.Cli.Local;

namespace Unity.Multiplayer.Hosting.Cli.Tests;

[TestFixture]
public class LocalComposeRendererTests
{
    private static readonly Dictionary<string, string> Env = new() { ["MINDATTIC_PUBLIC_ADDRESS"] = "192.168.1.42" };

    [Test]
    public void Render_builds_from_the_package_directory_instead_of_pulling_an_image()
    {
        string yaml = LocalComposeRenderer.Render(TestManifests.WithHealthPort(), "C:/pkg/GenericVps", Env, null);
        Assert.That(yaml, Does.Contain("build:"));
        Assert.That(yaml, Does.Contain("GenericVps"));
        Assert.That(yaml, Does.Not.Contain("image: ghcr.io"));
    }

    [Test]
    public void Render_binds_the_game_port_on_every_interface_and_the_health_port_to_localhost_only()
    {
        string yaml = LocalComposeRenderer.Render(TestManifests.WithHealthPort(), "/pkg", Env, null);
        Assert.That(yaml, Does.Contain("\"7777:7777/udp\""));
        Assert.That(yaml, Does.Contain("\"127.0.0.1:8080:8080/tcp\""));
    }

    [Test]
    public void Render_omits_the_rendezvous_service_when_no_image_is_given()
    {
        string yaml = LocalComposeRenderer.Render(TestManifests.WithHealthPort(), "/pkg", Env, null);
        Assert.That(yaml, Does.Not.Contain("rendezvous"));
    }

    [Test]
    public void Render_includes_the_rendezvous_service_when_an_image_is_given()
    {
        var env = new Dictionary<string, string>(Env) { ["MINDATTIC_RENDEZVOUS_SECRET"] = "s3cr3t" };
        string yaml = LocalComposeRenderer.Render(TestManifests.WithHealthPort(), "/pkg", env, "ghcr.io/mindattic/rendezvous:latest");
        Assert.That(yaml, Does.Contain("rendezvous:"));
        Assert.That(yaml, Does.Contain("ghcr.io/mindattic/rendezvous:latest"));
        Assert.That(yaml, Does.Contain("8080:8080/tcp"));
    }

    [Test]
    public void Render_throws_on_a_null_manifest()
    {
        Assert.Throws<ArgumentNullException>(() => LocalComposeRenderer.Render(null!, "/pkg", Env, null));
    }
}
