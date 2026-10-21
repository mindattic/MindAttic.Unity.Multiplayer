using Unity.Multiplayer.Hosting.Cli.Packaging;

namespace Unity.Multiplayer.Hosting.Cli.Tests;

[TestFixture]
public class ServerPackageManifestTests
{
    private const string ValidManifest = """
        {
          "format": 1,
          "profile": "GenericVps",
          "executable": "Game.x86_64",
          "start": "./start.sh",
          "config": "server.json",
          "transport": "udp",
          "ports": [
            { "name": "game", "port": 7777, "protocol": "udp" },
            { "name": "health", "port": 8080, "protocol": "tcp", "path": "/healthz" }
          ],
          "logs": "Logs",
          "saves": "Saves",
          "stopSignal": "SIGTERM",
          "stopGraceSeconds": 10
        }
        """;

    [Test]
    public void Parse_reads_every_field()
    {
        var manifest = ServerPackageManifest.Parse(ValidManifest);
        Assert.That(manifest.Profile, Is.EqualTo("GenericVps"));
        Assert.That(manifest.Executable, Is.EqualTo("Game.x86_64"));
        Assert.That(manifest.Ports, Has.Count.EqualTo(2));
        Assert.That(manifest.StopGraceSeconds, Is.EqualTo(10));
    }

    [Test]
    public void GamePort_and_HealthPort_are_split_out()
    {
        var manifest = ServerPackageManifest.Parse(ValidManifest);
        Assert.That(manifest.GamePort!.Port, Is.EqualTo(7777));
        Assert.That(manifest.GamePort!.Protocol, Is.EqualTo("udp"));
        Assert.That(manifest.HealthPort!.Port, Is.EqualTo(8080));
        Assert.That(manifest.HealthPort!.Path, Is.EqualTo("/healthz"));
    }

    [Test]
    public void Parse_throws_FormatException_for_missing_fields()
    {
        Assert.Throws<FormatException>(() => ServerPackageManifest.Parse("{ \"format\": 1 }"));
    }

    [Test]
    public void Load_throws_FileNotFoundException_when_the_manifest_is_absent()
    {
        string dir = Path.Combine(Path.GetTempPath(), "mp-host-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            Assert.Throws<FileNotFoundException>(() => ServerPackageManifest.Load(dir));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Test]
    public void Load_reads_a_manifest_from_disk()
    {
        string dir = Path.Combine(Path.GetTempPath(), "mp-host-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, ServerPackageManifest.FileName), ValidManifest);
            var manifest = ServerPackageManifest.Load(dir);
            Assert.That(manifest.Profile, Is.EqualTo("GenericVps"));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }
}
