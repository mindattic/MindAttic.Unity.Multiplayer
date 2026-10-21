using Unity.Multiplayer.Hosting.Cli.Cli;
using Unity.Multiplayer.Hosting.Cli.Packaging;
using Unity.Multiplayer.Hosting.Cli.Processes;
using Unity.Multiplayer.Hosting.Cli.Tests.Fakes;

namespace Unity.Multiplayer.Hosting.Cli.Tests;

[TestFixture]
public class ServerPackageBuilderTests
{
    private string _dir = null!;

    [SetUp]
    public void SetUp() => _dir = Path.Combine(Path.GetTempPath(), "mp-host-pkg-" + Guid.NewGuid().ToString("N"));

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
    }

    [Test]
    public async Task BuildAsync_reads_an_already_built_package_without_touching_Unity()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, ServerPackageManifest.FileName), ValidManifestJson);
        var processes = new FakeProcessRunner();
        var builder = new ServerPackageBuilder(processes);

        var built = await builder.BuildAsync(new ServerPackageBuildRequest("GenericVps", _dir, null, null), new RecordingDeployLog(), CancellationToken.None);

        Assert.That(built.Directory, Is.EqualTo(_dir));
        Assert.That(built.Manifest.Profile, Is.EqualTo("GenericVps"));
        Assert.That(processes.Calls, Is.Empty);
    }

    [Test]
    public void BuildAsync_throws_when_neither_a_package_nor_a_Unity_path_is_given()
    {
        var builder = new ServerPackageBuilder(new FakeProcessRunner());
        Assert.ThrowsAsync<ArgumentException>(() =>
            builder.BuildAsync(new ServerPackageBuildRequest("GenericVps", null, null, null), new RecordingDeployLog(), CancellationToken.None));
    }

    [Test]
    public void BuildAsync_surfaces_a_failed_Unity_batch_build()
    {
        var processes = new FakeProcessRunner { Outcome = new ProcessOutcome(1, "", "build error") };
        var builder = new ServerPackageBuilder(processes);
        Assert.ThrowsAsync<InvalidOperationException>(() =>
            builder.BuildAsync(new ServerPackageBuildRequest("GenericVps", null, "/unity/Unity.exe", "/proj"), new RecordingDeployLog(), CancellationToken.None));
    }

    [Test]
    public async Task BuildAsync_invokes_Unitys_exact_batch_mode_contract()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, ServerPackageManifest.FileName), ValidManifestJson);

        FakeProcessRunner? processes = null;
        processes = new FakeProcessRunner();
        // Simulate Unity writing the manifest to whatever -serverOutput it was given by pointing
        // the builder at a fixed, pre-seeded directory isn't possible (the path is a fresh GUID),
        // so this test only asserts the invocation shape; ServerPackageManifest.Load's own
        // behaviour is covered by ServerPackageManifestTests.
        processes.Outcome = new ProcessOutcome(1, "", "simulated: no real Unity install");
        var builder = new ServerPackageBuilder(processes);
        Assert.ThrowsAsync<InvalidOperationException>(() =>
            builder.BuildAsync(new ServerPackageBuildRequest("RunPod", null, "/opt/Unity/Editor/Unity", "/proj"), new RecordingDeployLog(), CancellationToken.None));

        var call = processes.Calls.Single();
        Assert.That(call.FileName, Is.EqualTo("/opt/Unity/Editor/Unity"));
        Assert.That(call.Arguments, Does.Contain("-batchmode"));
        Assert.That(call.Arguments, Does.Contain("-executeMethod"));
        Assert.That(call.Arguments, Does.Contain("Unity.Multiplayer.Editor.ServerPackageBuild.BuildFromCommandLine"));
        Assert.That(call.Arguments, Does.Contain("-serverProfile"));
        Assert.That(call.Arguments, Does.Contain("RunPod"));
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
