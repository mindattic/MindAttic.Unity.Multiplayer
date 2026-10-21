using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Unity.Multiplayer.Core
{
    /// <summary>One file (or folder) of a server package, relative to the package folder.</summary>
    public sealed class ServerPackageFile
    {
        private ServerPackageFile(string relativePath, string contents, bool isDirectory, bool isExecutable)
        {
            if (string.IsNullOrWhiteSpace(relativePath) || relativePath.StartsWith("/", StringComparison.Ordinal)
                || relativePath.IndexOf('\\') >= 0 || relativePath.IndexOf("..", StringComparison.Ordinal) >= 0)
            {
                throw new ArgumentException("'" + relativePath + "' is not a forward-slash path inside the package.", nameof(relativePath));
            }

            RelativePath = relativePath;
            Contents = contents;
            IsDirectory = isDirectory;
            IsExecutable = isExecutable;
        }

        /// <summary>The path inside the package, with forward slashes.</summary>
        public string RelativePath { get; }

        /// <summary>The text (LF line endings, UTF-8 without a byte order mark); null for a folder.</summary>
        public string Contents { get; }

        /// <summary>True for a folder the package must contain (it may be empty).</summary>
        public bool IsDirectory { get; }

        /// <summary>True when the file must be executable on Linux (a script).</summary>
        public bool IsExecutable { get; }

        /// <summary>A text file.</summary>
        /// <exception cref="ArgumentException"><paramref name="relativePath"/> is empty, rooted, uses backslashes or climbs out.</exception>
        public static ServerPackageFile Text(string relativePath, string contents, bool executable = false)
        {
            return new ServerPackageFile(relativePath, contents ?? string.Empty, false, executable);
        }

        /// <summary>A folder.</summary>
        /// <exception cref="ArgumentException"><paramref name="relativePath"/> is empty, rooted, uses backslashes or climbs out.</exception>
        public static ServerPackageFile Directory(string relativePath)
        {
            return new ServerPackageFile(relativePath.TrimEnd('/'), null, true, false);
        }

        /// <inheritdoc />
        public override string ToString()
        {
            return IsDirectory ? RelativePath + "/" : RelativePath;
        }
    }

    /// <summary>
    /// The provider-neutral server package: what every host gets, whichever <see cref="IHostingProfile"/> it is built
    /// for. A self-contained Linux x86_64 Dedicated Server build of the game plus:
    /// <list type="bullet">
    /// <item><description><c>server.json</c>: the settings, overridden by the environment and command line (<see cref="ServerStartup"/>);</description></item>
    /// <item><description><c>start.sh</c>: starts the server with the package's settings and forwards its arguments
    /// (<c>-port -maxPlayers -name -password</c>); it <c>exec</c>s, so SIGTERM and SIGINT reach the server, which shuts down cleanly;</description></item>
    /// <item><description><c>server-package.json</c>: the manifest a deploy tool reads (profile, executable, declared ports, health path);</description></item>
    /// <item><description><c>Logs/</c> and <c>Saves/</c>: log files, and the game's persistent data;</description></item>
    /// <item><description>the profile's own files (a Dockerfile, a compose file, notes).</description></item>
    /// </list>
    /// </summary>
    public sealed class ServerPackageSpec
    {
        /// <summary>The start script's name.</summary>
        public const string StartScriptName = "start.sh";

        /// <summary>The manifest's name.</summary>
        public const string ManifestFileName = "server-package.json";

        /// <summary>The manifest format version written.</summary>
        public const int ManifestFormat = 1;

        /// <summary>The extension Unity gives a Linux x86_64 executable.</summary>
        public const string ExecutableExtension = ".x86_64";

        /// <summary>The Unity engine library every Linux player ships beside its executable.</summary>
        public const string UnityPlayerLibrary = "UnityPlayer.so";

        private ServerPackageSpec(IHostingProfile profile, string executableName, NetConfig config, ServerSettings settings)
        {
            Profile = profile;
            ExecutableName = executableName;
            Config = config;
            Settings = settings;
        }

        /// <summary>The profile the package is built for.</summary>
        public IHostingProfile Profile { get; }

        /// <summary>The server executable's file name, such as <c>MyGame.x86_64</c>.</summary>
        public string ExecutableName { get; }

        /// <summary>The settings written to <c>server.json</c>; the topology is always <see cref="Topology.Server"/>.</summary>
        public NetConfig Config { get; }

        /// <summary>The package settings written to <c>server.json</c>.</summary>
        public ServerSettings Settings { get; }

        /// <summary>More arguments <c>start.sh</c> always passes, before the ones it is given.</summary>
        public List<string> ExtraArgs { get; } = new List<string>();

        /// <summary>Unity's data folder beside the executable: its name without the extension, plus <c>_Data</c>.</summary>
        public string DataFolderName
        {
            get
            {
                string name = ExecutableName.EndsWith(ExecutableExtension, StringComparison.Ordinal)
                    ? ExecutableName.Substring(0, ExecutableName.Length - ExecutableExtension.Length)
                    : ExecutableName;
                return name + "_Data";
            }
        }

        /// <summary>The ports the server listens on: the game port (UDP, or TCP for WebSocket) and the health port when on.</summary>
        public IReadOnlyList<DeclaredPort> Ports
        {
            get
            {
                var ports = new List<DeclaredPort>
                {
                    new DeclaredPort(DeclaredPort.GameName, Config.Port,
                        Config.Transport == TransportKind.WebSocket ? PortProtocol.Tcp : PortProtocol.Udp,
                        Config.Transport == TransportKind.WebSocket ? "Players connect over WebSocket." : "Players connect over UDP."),
                };
                if (Settings.HealthPort != 0)
                {
                    ports.Add(new DeclaredPort(DeclaredPort.HealthName, Settings.HealthPort, PortProtocol.Tcp,
                        "HTTP GET " + ServerSettings.HealthPath + ": 200 while running, 503 while starting or stopping."));
                }

                return ports;
            }
        }

        /// <summary>
        /// The spec <paramref name="profile"/> builds: copies of <paramref name="config"/> and <paramref name="settings"/>
        /// (defaults when null), switched to a dedicated server, then adjusted by <see cref="IHostingProfile.Configure"/>.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="profile"/> is null.</exception>
        /// <exception cref="ArgumentException"><paramref name="executableName"/> is not a safe file name.</exception>
        public static ServerPackageSpec For(IHostingProfile profile, string executableName, NetConfig config = null, ServerSettings settings = null)
        {
            if (profile == null)
            {
                throw new ArgumentNullException(nameof(profile));
            }

            if (!IsSafeFileName(executableName))
            {
                throw new ArgumentException("'" + executableName + "' is not a valid server executable name (letters, digits, '.', '_' and '-' only).", nameof(executableName));
            }

            NetConfig copy = (config ?? new NetConfig()).Clone();
            copy.Topology = Topology.Server;
            copy.RendezvousSecret = null;
            var spec = new ServerPackageSpec(profile, executableName, copy, (settings ?? new ServerSettings()).Clone());
            profile.Configure(spec);
            spec.Config.Topology = Topology.Server;
            return spec;
        }

        /// <summary>True for a non-empty file name of letters, digits, '.', '_' and '-' that is not '.' or '..'.</summary>
        public static bool IsSafeFileName(string name)
        {
            if (string.IsNullOrEmpty(name) || name == "." || name == "..")
            {
                return false;
            }

            foreach (char c in name)
            {
                bool ok = (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == '.' || c == '_' || c == '-';
                if (!ok)
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>Lists every problem with the spec; an empty list means it can be built.</summary>
        public IReadOnlyList<string> Validate()
        {
            var errors = new List<string>();
            errors.AddRange(Config.Validate());
            errors.AddRange(Settings.Validate());
            if (Settings.HealthPort != 0 && Settings.HealthPort == Config.Port && Config.Transport == TransportKind.WebSocket)
            {
                errors.Add("The health port " + Settings.HealthPort + " is also the WebSocket game port.");
            }

            foreach (string arg in ExtraArgs)
            {
                if (arg == null || arg.IndexOf('\n') >= 0 || arg.IndexOf('\r') >= 0)
                {
                    errors.Add("Extra server arguments must be single-line text.");
                    break;
                }
            }

            return errors;
        }

        /// <summary>
        /// The files the package holds besides Unity's build output, in a fixed order: <c>server.json</c>, <c>start.sh</c>,
        /// the manifest, <c>Logs/</c>, <c>Saves/</c>, then the profile's files.
        /// </summary>
        /// <exception cref="InvalidOperationException">The profile adds a file the package already has.</exception>
        public IReadOnlyList<ServerPackageFile> ComposeFiles()
        {
            var files = new List<ServerPackageFile>
            {
                ServerPackageFile.Text(ServerConfigFile.FileName, ServerConfigFile.Compose(Config, Settings)),
                ServerPackageFile.Text(StartScriptName, ComposeStartScript(), true),
                ServerPackageFile.Text(ManifestFileName, ComposeManifest()),
            };

            if (IsInsidePackage(Settings.LogsDirectory))
            {
                files.Add(ServerPackageFile.Directory(Settings.LogsDirectory.Replace('\\', '/')));
            }

            if (IsInsidePackage(Settings.SavesDirectory))
            {
                files.Add(ServerPackageFile.Directory(Settings.SavesDirectory.Replace('\\', '/')));
            }

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (ServerPackageFile file in files)
            {
                seen.Add(file.RelativePath);
            }

            seen.Add(ExecutableName);
            seen.Add(DataFolderName);
            seen.Add(UnityPlayerLibrary);
            IReadOnlyList<ServerPackageFile> extra = Profile.CreateFiles(this) ?? new ServerPackageFile[0];
            foreach (ServerPackageFile file in extra)
            {
                if (file == null)
                {
                    continue;
                }

                if (!seen.Add(file.RelativePath))
                {
                    throw new InvalidOperationException("Hosting profile " + Profile.Id + " adds " + file.RelativePath + ", which the package already has.");
                }

                files.Add(file);
            }

            return files;
        }

        /// <summary>
        /// Every path the finished package must contain: Unity's executable, data folder and engine library, then
        /// <see cref="ComposeFiles"/>'. A build is complete when each exists.
        /// </summary>
        public IReadOnlyList<ServerPackageFile> ExpectedLayout()
        {
            var layout = new List<ServerPackageFile>
            {
                ServerPackageFile.Text(ExecutableName, null, true),
                ServerPackageFile.Directory(DataFolderName),
                ServerPackageFile.Text(UnityPlayerLibrary, null),
            };
            layout.AddRange(ComposeFiles());
            return layout;
        }

        /// <summary>The arguments <c>start.sh</c> starts the executable with, executable first; its own arguments follow.</summary>
        public IReadOnlyList<string> LaunchArguments()
        {
            var args = new List<string>
            {
                "./" + ExecutableName,
                "-batchmode",
                "-nographics",
                "-logFile",
                "-",
                NetCommandLine.ModeArg,
                "server",
                ServerStartup.ConfigArg,
                ServerConfigFile.FileName,
            };
            foreach (string arg in ExtraArgs)
            {
                if (!string.IsNullOrEmpty(arg))
                {
                    args.Add(arg);
                }
            }

            return args;
        }

        /// <summary>The POSIX shell start script.</summary>
        public string ComposeStartScript()
        {
            var text = new StringBuilder();
            text.Append("#!/bin/sh\n");
            text.Append("# Generated by Unity.Multiplayer (Build Server Package > ").Append(Profile.Id).Append("). Rebuilding rewrites it.\n");
            text.Append("#\n");
            text.Append("# Starts the dedicated server from this folder. Settings come from server.json, then the environment\n");
            text.Append("# (MINDATTIC_RENDEZVOUS_URL, MINDATTIC_RENDEZVOUS_SECRET, MINDATTIC_PUBLIC_ADDRESS, MINDATTIC_SERVER_PASSWORD),\n");
            text.Append("# then the arguments given here, which win:\n");
            text.Append("#\n");
            text.Append("#   ./start.sh -port 7778 -maxPlayers 4 -name \"Friday game\" -password hunter2\n");
            text.Append("#\n");
            text.Append("# The server logs to stdout and to ").Append(Settings.LogsDirectory).Append("/, keeps game data in ").Append(Settings.SavesDirectory)
                .Append("/, and shuts down\n");
            text.Append("# cleanly on SIGTERM or SIGINT (docker stop, systemctl stop, Ctrl+C): exec below hands it this process.\n");
            text.Append("set -e\n");
            text.Append("cd \"$(dirname \"$0\")\"\n");
            text.Append("mkdir -p ").Append(ShellQuote(Settings.LogsDirectory)).Append(' ').Append(ShellQuote(Settings.SavesDirectory)).Append('\n');
            text.Append("chmod +x ").Append(ShellQuote("./" + ExecutableName)).Append(" 2>/dev/null || true\n");
            text.Append("exec");
            foreach (string arg in LaunchArguments())
            {
                text.Append(' ').Append(ShellQuote(arg));
            }

            text.Append(" \"$@\"\n");
            return text.ToString();
        }

        /// <summary>The manifest JSON a deploy tool reads.</summary>
        public string ComposeManifest()
        {
            var json = new StringBuilder("{\n");
            Field(json, "format", ManifestFormat.ToString(CultureInfo.InvariantCulture));
            Field(json, "profile", Quote(Profile.Id));
            Field(json, "executable", Quote(ExecutableName));
            Field(json, "start", Quote("./" + StartScriptName));
            Field(json, "config", Quote(ServerConfigFile.FileName));
            Field(json, "transport", Quote(Config.Transport == TransportKind.WebSocket ? "websocket" : "udp"));
            json.Append("  \"ports\": [\n");
            IReadOnlyList<DeclaredPort> ports = Ports;
            for (int i = 0; i < ports.Count; i++)
            {
                DeclaredPort port = ports[i];
                json.Append("    { \"name\": ").Append(Quote(port.Name))
                    .Append(", \"port\": ").Append(port.Port.ToString(CultureInfo.InvariantCulture))
                    .Append(", \"protocol\": ").Append(Quote(port.ProtocolName));
                if (port.Name == DeclaredPort.HealthName)
                {
                    json.Append(", \"path\": ").Append(Quote(ServerSettings.HealthPath));
                }

                json.Append(" }").Append(i + 1 < ports.Count ? ",\n" : "\n");
            }

            json.Append("  ],\n");
            Field(json, "logs", Quote(Settings.LogsDirectory));
            Field(json, "saves", Quote(Settings.SavesDirectory));
            Field(json, "stopSignal", Quote("SIGTERM"));
            Field(json, "stopGraceSeconds", Settings.ShutdownGraceSeconds.ToString("0.###", CultureInfo.InvariantCulture), last: true);
            return json.Append("}\n").ToString();
        }

        /// <summary>Quotes <paramref name="value"/> for a POSIX shell when it needs it.</summary>
        public static string ShellQuote(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return "''";
            }

            foreach (char c in value)
            {
                bool plain = (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9')
                    || c == '.' || c == '_' || c == '-' || c == '/' || c == ':' || c == '=' || c == '+' || c == ',';
                if (!plain)
                {
                    return "'" + value.Replace("'", "'\\''") + "'";
                }
            }

            return value;
        }

        /// <summary>True when <paramref name="directory"/> is a relative path that stays inside the package folder.</summary>
        public static bool IsInsidePackage(string directory)
        {
            return !string.IsNullOrWhiteSpace(directory) && !System.IO.Path.IsPathRooted(directory)
                && !directory.StartsWith("/", StringComparison.Ordinal) && directory.IndexOf("..", StringComparison.Ordinal) < 0;
        }

        internal static string Quote(string value)
        {
            var json = new StringBuilder();
            RendezvousJson.WriteString(json, value ?? string.Empty);
            return json.ToString();
        }

        private static void Field(StringBuilder json, string key, string rawValue, bool last = false)
        {
            json.Append("  ").Append(Quote(key)).Append(": ").Append(rawValue).Append(last ? "\n" : ",\n");
        }
    }
}
