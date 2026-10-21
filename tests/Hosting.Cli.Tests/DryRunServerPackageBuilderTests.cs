using Unity.Multiplayer.Hosting.Cli.Cli;
using Unity.Multiplayer.Hosting.Cli.Packaging;

namespace Unity.Multiplayer.Hosting.Cli.Tests;

[TestFixture]
public class DryRunServerPackageBuilderTests
{
    private string _dir = null!;

    [SetUp]
    public void SetUp() => _dir = Path.Combine(Path.GetTempPath(), "mp-host-dryrun-pkg-" + Guid.NewGuid().ToString("N"));

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
    }

    [Test]
    public async Task BuildAsync_never_launches_Unity_for_a_project_path_and_returns_a_synthesized_package()
    {
        var log = new RecordingDeployLog();
        var builder = new DryRunServerPackageBuilder();

        BuiltServerPackage built = await builder.BuildAsync(
            new ServerPackageBuildRequest("GenericVps", null, "/opt/Unity/Editor/Unity", "/proj"), log, CancellationToken.None);

        Assert.That(built.Manifest.Profile, Is.EqualTo("GenericVps"));
        Assert.That(built.Manifest.GamePort, Is.Not.Null);
        Assert.That(log.DryRunCalls, Has.Some.Matches<string>(c => c.Contains("/opt/Unity/Editor/Unity") && c.Contains("BuildFromCommandLine")));
    }

    [Test]
    public async Task BuildAsync_still_reads_an_already_built_package_since_that_is_only_a_disk_read()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, ServerPackageManifest.FileName), ValidManifestJson);
        var builder = new DryRunServerPackageBuilder();

        BuiltServerPackage built = await builder.BuildAsync(
            new ServerPackageBuildRequest("GenericVps", _dir, null, null), new RecordingDeployLog(), CancellationToken.None);

        Assert.That(built.Directory, Is.EqualTo(_dir));
        Assert.That(built.Manifest.Profile, Is.EqualTo("GenericVps"));
    }

    [Test]
    public void BuildAsync_throws_when_neither_a_package_nor_a_Unity_path_is_given()
    {
        var builder = new DryRunServerPackageBuilder();
        Assert.ThrowsAsync<ArgumentException>(() =>
            builder.BuildAsync(new ServerPackageBuildRequest("GenericVps", null, null, null), new RecordingDeployLog(), CancellationToken.None));
    }

    private const string ValidManifestJson = """
        {
          "format": 1, "profile": "GenericVps", "executable": "Game.x86_64", "start": "./start.sh",
          "config": "server.json", "transport": "udp",
          "ports": [ { "name": "game", "port": 7777, "protocol": "udp" } ],
          "logs": "Logs", "saves": "Saves", "stopSignal": "SIGTERM", "stopGraceSeconds": 10
        }
        """;
}
