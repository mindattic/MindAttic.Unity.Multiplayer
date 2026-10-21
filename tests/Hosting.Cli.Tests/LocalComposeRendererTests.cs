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
    public void Render_mounts_saves_and_logs_under_the_package_images_workdir()
    {
        // UM-T51: the package's own Dockerfile sets WORKDIR /srv/game, so volumes mounted under
        // /app/ left the game writing Saves/Logs into the throwaway container layer, lost on every
        // 'local down/up'. The mount paths come from the same rule as the cloud compose (UM-T38).
        string yaml = LocalComposeRenderer.Render(TestManifests.WithHealthPort(), "/pkg", Env, null);
        Assert.That(yaml, Does.Contain("-saves:/srv/game/Saves\n"));
        Assert.That(yaml, Does.Contain("-logs:/srv/game/Logs\n"));
        Assert.That(yaml, Does.Not.Contain("/app/"));
    }

    [Test]
    public void Render_escapes_a_literal_dollar_against_compose_interpolation()
    {
        // UM-T53: the local compose file quoted only YAML's \ and ", so a '$' in a secret, password
        // or --env value was read by Compose as $VAR / ${VAR} and silently substituted. It now
        // quotes through the same CloudInitRenderer.YamlQuote as the cloud compose (UM-T42).
        var env = new Dictionary<string, string>(Env)
        {
            ["MINDATTIC_SERVER_PASSWORD"] = "pa$word${X}",
            ["MINDATTIC_RENDEZVOUS_SECRET"] = "s$cr\"t",
        };
        string yaml = LocalComposeRenderer.Render(TestManifests.WithHealthPort(), "/pkg", env, "ghcr.io/mindattic/rendezvous:latest");
        Assert.That(yaml, Does.Contain("MINDATTIC_SERVER_PASSWORD: \"pa$$word$${X}\"\n"));
        Assert.That(yaml, Does.Contain("Rendezvous__RegistrationSecret: \"s$$cr\\\"t\"\n"));
        Assert.That(yaml, Does.Not.Contain("pa$word"));
    }

    [Test]
    public void Render_throws_on_a_null_manifest()
    {
        Assert.Throws<ArgumentNullException>(() => LocalComposeRenderer.Render(null!, "/pkg", Env, null));
    }
}
