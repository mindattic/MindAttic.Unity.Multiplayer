using Unity.Multiplayer.Hosting.Cli.Packaging;

namespace Unity.Multiplayer.Hosting.Cli.Tests;

/// <summary>A minimal, valid manifest shared by provisioner/pipeline tests.</summary>
internal static class TestManifests
{
    public static ServerPackageManifest WithHealthPort() => new(
        Format: 1,
        Profile: "GenericVps",
        Executable: "Game.x86_64",
        Start: "./start.sh",
        Config: "server.json",
        Transport: "udp",
        Ports: new List<ManifestPort>
        {
            new("game", 7777, "udp", null),
            new("health", 8080, "tcp", "/healthz"),
        },
        Logs: "Logs",
        Saves: "Saves",
        StopSignal: "SIGTERM",
        StopGraceSeconds: 10);

    public static ServerPackageManifest WithoutHealthPort() => new(
        Format: 1,
        Profile: "RunPod",
        Executable: "Game.x86_64",
        Start: "./start.sh",
        Config: "server.json",
        Transport: "websocket",
        Ports: new List<ManifestPort> { new("game", 7777, "tcp", null) },
        Logs: "Logs",
        Saves: "Saves",
        StopSignal: "SIGTERM",
        StopGraceSeconds: 10);
}
