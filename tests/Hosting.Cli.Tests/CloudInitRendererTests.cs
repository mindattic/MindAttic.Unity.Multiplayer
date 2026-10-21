using Unity.Multiplayer.Hosting.Cli.CloudInit;

namespace Unity.Multiplayer.Hosting.Cli.Tests;

[TestFixture]
public class CloudInitRendererTests
{
    [Test]
    public void RenderCompose_references_the_pushed_image_not_a_local_build()
    {
        string compose = CloudInitRenderer.RenderCompose(
            TestManifests.WithHealthPort(), "ghcr.io/mindattic/game:v1", "ghcr.io/mindattic/rendezvous:latest",
            new Dictionary<string, string> { ["MINDATTIC_RENDEZVOUS_SECRET"] = "s3cr3t" });

        Assert.That(compose, Does.Contain("image: ghcr.io/mindattic/game:v1"));
        Assert.That(compose, Does.Not.Contain("build: ."));
        Assert.That(compose, Does.Contain("image: ghcr.io/mindattic/rendezvous:latest"));
    }

    [Test]
    public void RenderCompose_binds_the_health_port_to_localhost_only()
    {
        string compose = CloudInitRenderer.RenderCompose(
            TestManifests.WithHealthPort(), "img", "rzv", new Dictionary<string, string>());
        Assert.That(compose, Does.Contain("127.0.0.1:8080:8080/tcp"));
        Assert.That(compose, Does.Contain("\"7777:7777/udp\""));
    }

    [Test]
    public void RenderCompose_writes_every_environment_variable()
    {
        string compose = CloudInitRenderer.RenderCompose(
            TestManifests.WithHealthPort(), "img", "rzv",
            new Dictionary<string, string> { ["MINDATTIC_PUBLIC_ADDRESS"] = "203.0.113.5" });
        Assert.That(compose, Does.Contain("MINDATTIC_PUBLIC_ADDRESS"));
        Assert.That(compose, Does.Contain("203.0.113.5"));
    }

    [Test]
    public void RenderBootstrapScript_logs_in_pulls_and_starts_then_removes_the_token()
    {
        string script = CloudInitRenderer.RenderBootstrapScript("ghcr.io", "octocat");
        Assert.That(script, Does.Contain("docker login ghcr.io --username octocat --password-stdin"));
        Assert.That(script, Does.Contain("docker compose -f docker-compose.yml pull"));
        Assert.That(script, Does.Contain("docker compose -f docker-compose.yml up -d"));
        Assert.That(script, Does.Contain("rm -f /tmp/mp-host-ghcr-token"));
    }

    [Test]
    public void RenderUserData_embeds_the_ssh_key_compose_file_token_and_bootstrap_script()
    {
        string userData = CloudInitRenderer.RenderUserData(
            TestManifests.WithHealthPort(), "ghcr.io/mindattic/game:v1", "ghcr.io/mindattic/rendezvous:latest",
            new Dictionary<string, string> { ["MINDATTIC_RENDEZVOUS_SECRET"] = "s3cr3t" },
            "ghcr.io", "octocat", "ghp_token", "ssh-ed25519 AAAA... me@laptop");

        Assert.That(userData, Does.StartWith("#cloud-init\n"));
        Assert.That(userData, Does.Contain("ssh_authorized_keys:"));
        Assert.That(userData, Does.Contain("ssh-ed25519 AAAA... me@laptop"));
        Assert.That(userData, Does.Contain("path: /root/docker-compose.yml"));
        Assert.That(userData, Does.Contain("path: /tmp/mp-host-ghcr-token"));
        Assert.That(userData, Does.Contain("path: /root/mp-host-bootstrap.sh"));
        Assert.That(userData, Does.Contain("ghcr.io/mindattic/game:v1"));
        Assert.That(userData, Does.Contain("ghp_token"));
        Assert.That(userData, Does.Contain("runcmd:"));
        Assert.That(userData, Does.Contain("mp-host-bootstrap.sh"));
    }

    [Test]
    public void RenderUserData_trims_the_public_key_so_a_trailing_newline_from_a_read_file_does_not_break_the_yaml()
    {
        string userData = CloudInitRenderer.RenderUserData(
            TestManifests.WithoutHealthPort(), "img", "rzv", new Dictionary<string, string>(),
            "ghcr.io", "octocat", "token", "ssh-ed25519 AAAA...\n");
        Assert.That(userData, Does.Contain("  - ssh-ed25519 AAAA...\n"));
        Assert.That(userData, Does.Not.Contain("ssh-ed25519 AAAA...\n\n"));
    }
}
