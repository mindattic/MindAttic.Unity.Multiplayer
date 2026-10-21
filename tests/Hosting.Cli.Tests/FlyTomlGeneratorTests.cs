using Unity.Multiplayer.Hosting.Cli.Provisioning.Fly;

namespace Unity.Multiplayer.Hosting.Cli.Tests;

[TestFixture]
public class FlyTomlGeneratorTests
{
    [Test]
    public void Render_declares_a_udp_service_for_the_game_port_and_the_bind_host_env_var()
    {
        string toml = FlyTomlGenerator.Render(
            "my-app", "iad", "shared-cpu-1x", "ghcr.io/mindattic/game:v1",
            TestManifests.WithHealthPort(), FlyTomlGenerator.ProcessGroupRendezvousMode);

        Assert.That(toml, Does.Contain("app = \"my-app\""));
        Assert.That(toml, Does.Contain("primary_region = \"iad\""));
        Assert.That(toml, Does.Contain("protocol = \"udp\""));
        Assert.That(toml, Does.Contain("internal_port = 7777"));
        Assert.That(toml, Does.Contain("MINDATTIC_BIND_HOST = \"fly-global-services\""));
    }

    [Test]
    public void Render_adds_a_tcp_check_for_the_declared_health_port()
    {
        string toml = FlyTomlGenerator.Render(
            "my-app", "iad", "shared-cpu-1x", "ghcr.io/mindattic/game:v1",
            TestManifests.WithHealthPort(), FlyTomlGenerator.ProcessGroupRendezvousMode);

        Assert.That(toml, Does.Contain("[[services.tcp_checks]]"));
        Assert.That(toml, Does.Contain("port = 8080"));
    }

    [Test]
    public void Render_in_process_group_mode_declares_a_second_process_and_tcp_service_for_rendezvous()
    {
        string toml = FlyTomlGenerator.Render(
            "my-app", "iad", "shared-cpu-1x", "ghcr.io/mindattic/game:v1",
            TestManifests.WithHealthPort(), FlyTomlGenerator.ProcessGroupRendezvousMode, "ghcr.io/mindattic/rendezvous:latest");

        Assert.That(toml, Does.Contain("[processes]"));
        Assert.That(toml, Does.Contain("rendezvous = \"\""));
        Assert.That(toml, Does.Contain("processes = [\"rendezvous\"]"));
        Assert.That(toml, Does.Contain("protocol = \"tcp\""));
    }

    [Test]
    public void Render_in_app_mode_omits_the_rendezvous_process_group()
    {
        string toml = FlyTomlGenerator.Render(
            "my-app", "iad", "shared-cpu-1x", "ghcr.io/mindattic/game:v1",
            TestManifests.WithHealthPort(), FlyTomlGenerator.AppRendezvousMode);

        Assert.That(toml, Does.Not.Contain("[processes]"));
        Assert.That(toml, Does.Not.Contain("rendezvous"));
    }

    [Test]
    public void Render_throws_when_the_manifest_has_no_game_port()
    {
        var noPorts = TestManifests.WithHealthPort() with { Ports = new List<Unity.Multiplayer.Hosting.Cli.Packaging.ManifestPort>() };
        Assert.Throws<ArgumentException>(() => FlyTomlGenerator.Render(
            "my-app", "iad", "shared-cpu-1x", "ghcr.io/mindattic/game:v1", noPorts, FlyTomlGenerator.ProcessGroupRendezvousMode));
    }
}
