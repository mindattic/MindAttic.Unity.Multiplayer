using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace MindAttic.Unity.Multiplayer.Core
{
    /// <summary>
    /// Loads a dedicated server's settings the way the server package promises: the defaults, then <c>server.json</c>,
    /// then the environment (<see cref="NetEnvironment"/>), then the command line (<see cref="NetCommandLine"/> and the
    /// package arguments below), each layer overriding the one before.
    /// </summary>
    /// <remarks>
    /// <para>Package arguments (names ignore case; the value is the next argument or follows <c>=</c>):</para>
    /// <list type="table">
    /// <item><term><c>-config</c></term><description>The settings file to read instead of <c>server.json</c> in the package folder (relative to the package folder).</description></item>
    /// <item><term><c>-healthport</c></term><description>Health endpoint TCP port, 0 for off (<see cref="ServerSettings.HealthPort"/>).</description></item>
    /// <item><term><c>-admin</c></term><description><c>on</c> or <c>off</c>; alone it means on (<see cref="ServerSettings.AdminCommands"/>).</description></item>
    /// <item><term><c>-logs</c> / <c>-saves</c></term><description>Logs and saves folders (<see cref="ServerSettings.LogsDirectory"/>, <see cref="ServerSettings.SavesDirectory"/>).</description></item>
    /// </list>
    /// <para>The session arguments <c>-port -maxPlayers -name -password</c> and the rest are <see cref="NetCommandLine"/>'s.</para>
    /// </remarks>
    public static class ServerStartup
    {
        /// <summary>Selects the settings file.</summary>
        public const string ConfigArg = "-config";

        /// <summary>Sets the health endpoint port.</summary>
        public const string HealthPortArg = "-healthport";

        /// <summary>Turns the admin console on or off.</summary>
        public const string AdminArg = "-admin";

        /// <summary>Sets the logs folder.</summary>
        public const string LogsArg = "-logs";

        /// <summary>Sets the saves folder.</summary>
        public const string SavesArg = "-saves";

        /// <summary>Applies every layer to <paramref name="config"/> and returns the package settings with what went wrong.</summary>
        /// <param name="config">The session settings to fill (the game's defaults, from the scene's NetBootstrap).</param>
        /// <param name="args">The process command line.</param>
        /// <param name="packageDirectory">The package folder (where the executable and <c>server.json</c> are).</param>
        /// <param name="environment">Reads an environment variable (null when unset); null reads the process environment.</param>
        /// <param name="readFile">Reads a file's text, or null when it does not exist; null reads the disk.</param>
        /// <exception cref="ArgumentNullException"><paramref name="config"/>, <paramref name="args"/> or <paramref name="packageDirectory"/> is null.</exception>
        public static ServerStartupResult Load(NetConfig config, IReadOnlyList<string> args, string packageDirectory,
            Func<string, string> environment = null, Func<string, string> readFile = null)
        {
            if (config == null)
            {
                throw new ArgumentNullException(nameof(config));
            }

            if (args == null)
            {
                throw new ArgumentNullException(nameof(args));
            }

            if (packageDirectory == null)
            {
                throw new ArgumentNullException(nameof(packageDirectory));
            }

            Func<string, string> read = readFile ?? ReadFileOrNull;
            var errors = new List<string>();
            var warnings = new List<string>();
            var settings = new ServerSettings();

            string configArg = FindValue(args, ConfigArg);
            string configPath = Path.Combine(packageDirectory, string.IsNullOrWhiteSpace(configArg) ? ServerConfigFile.FileName : configArg.Trim());
            string json = read(configPath);
            bool configFound = json != null;
            if (configFound)
            {
                ServerConfigResult file = ServerConfigFile.Apply(json, config, settings);
                errors.AddRange(file.Errors);
                warnings.AddRange(file.Warnings);
            }
            else if (configArg != null)
            {
                errors.Add(ConfigArg + " names " + configPath + ", which does not exist.");
            }

            NetEnvironment.Apply(config, environment);
            NetCommandLineResult commandLine = NetCommandLine.Apply(config, args);
            errors.AddRange(commandLine.Errors);
            errors.AddRange(ApplyPackageArguments(settings, args));

            // Whatever the layers said, this is a dedicated server.
            config.Topology = Topology.Server;
            errors.AddRange(config.Validate());
            errors.AddRange(settings.Validate());
            if (settings.HealthPort != 0 && settings.HealthPort == config.Port && config.Transport == TransportKind.WebSocket)
            {
                errors.Add("The health port " + settings.HealthPort + " is the WebSocket game port; give it another (healthPort / " + HealthPortArg + ").");
            }

            return new ServerStartupResult(settings, new ServerPaths(packageDirectory, settings), configFound ? configPath : null,
                commandLine, errors, warnings);
        }

        /// <summary>
        /// Applies the package arguments (<c>-healthport -admin -logs -saves</c>) to <paramref name="settings"/>.
        /// </summary>
        /// <returns>One message per argument with a missing or unreadable value.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="settings"/> or <paramref name="args"/> is null.</exception>
        public static IReadOnlyList<string> ApplyPackageArguments(ServerSettings settings, IReadOnlyList<string> args)
        {
            if (settings == null)
            {
                throw new ArgumentNullException(nameof(settings));
            }

            if (args == null)
            {
                throw new ArgumentNullException(nameof(args));
            }

            var errors = new List<string>();
            for (int i = 0; i < args.Count; i++)
            {
                string name = args[i];
                if (string.IsNullOrEmpty(name) || name[0] != '-')
                {
                    continue;
                }

                string value = null;
                int equals = name.IndexOf('=');
                if (equals > 0)
                {
                    value = name.Substring(equals + 1);
                    name = name.Substring(0, equals);
                }

                if (!Is(name, HealthPortArg) && !Is(name, AdminArg) && !Is(name, LogsArg) && !Is(name, SavesArg))
                {
                    continue;
                }

                if (value == null && i + 1 < args.Count && !string.IsNullOrEmpty(args[i + 1]) && args[i + 1][0] != '-')
                {
                    value = args[++i];
                }

                if (Is(name, AdminArg))
                {
                    switch ((value ?? "on").Trim().ToLowerInvariant())
                    {
                        case "on":
                        case "true":
                        case "yes":
                        case "1":
                            settings.AdminCommands = true;
                            break;
                        case "off":
                        case "false":
                        case "no":
                        case "0":
                            settings.AdminCommands = false;
                            break;
                        default:
                            errors.Add(AdminArg + " must be on or off; got '" + value + "'.");
                            break;
                    }

                    continue;
                }

                if (string.IsNullOrWhiteSpace(value))
                {
                    errors.Add(name + " needs a value.");
                    continue;
                }

                value = value.Trim();
                if (Is(name, HealthPortArg))
                {
                    if (ushort.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out ushort port))
                    {
                        settings.HealthPort = port;
                    }
                    else
                    {
                        errors.Add(HealthPortArg + " must be a number from 0 (off) to 65535; got '" + value + "'.");
                    }
                }
                else if (Is(name, LogsArg))
                {
                    settings.LogsDirectory = value;
                }
                else
                {
                    settings.SavesDirectory = value;
                }
            }

            return errors;
        }

        private static string FindValue(IReadOnlyList<string> args, string argName)
        {
            string found = null;
            for (int i = 0; i < args.Count; i++)
            {
                string arg = args[i];
                if (string.IsNullOrEmpty(arg))
                {
                    continue;
                }

                if (arg.StartsWith(argName + "=", StringComparison.OrdinalIgnoreCase))
                {
                    found = arg.Substring(argName.Length + 1);
                }
                else if (Is(arg, argName))
                {
                    found = i + 1 < args.Count && !string.IsNullOrEmpty(args[i + 1]) && args[i + 1][0] != '-' ? args[i + 1] : string.Empty;
                }
            }

            return found;
        }

        private static string ReadFileOrNull(string path)
        {
            return File.Exists(path) ? File.ReadAllText(path) : null;
        }

        private static bool Is(string a, string b)
        {
            return string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>What <see cref="ServerStartup.Load"/> found.</summary>
    public sealed class ServerStartupResult
    {
        /// <summary>Creates a result.</summary>
        public ServerStartupResult(ServerSettings settings, ServerPaths paths, string configPath, NetCommandLineResult commandLine,
            IReadOnlyList<string> errors, IReadOnlyList<string> warnings)
        {
            Settings = settings ?? throw new ArgumentNullException(nameof(settings));
            Paths = paths ?? throw new ArgumentNullException(nameof(paths));
            ConfigPath = configPath;
            CommandLine = commandLine;
            Errors = errors ?? new string[0];
            Warnings = warnings ?? new string[0];
        }

        /// <summary>The package settings.</summary>
        public ServerSettings Settings { get; }

        /// <summary>The package folder, logs and saves.</summary>
        public ServerPaths Paths { get; }

        /// <summary>The settings file read, or null when there was none.</summary>
        public string ConfigPath { get; }

        /// <summary>What the session command line held.</summary>
        public NetCommandLineResult CommandLine { get; }

        /// <summary>Every problem in any layer, and in the final settings. Never null.</summary>
        public IReadOnlyList<string> Errors { get; }

        /// <summary>Ignored settings-file keys. Never null.</summary>
        public IReadOnlyList<string> Warnings { get; }

        /// <summary>True when <see cref="Errors"/> is empty.</summary>
        public bool IsValid
        {
            get { return Errors.Count == 0; }
        }
    }

    /// <summary>The folders a running server package uses.</summary>
    public sealed class ServerPaths
    {
        /// <summary>Resolves <paramref name="settings"/>' folders against <paramref name="packageDirectory"/>.</summary>
        /// <exception cref="ArgumentNullException">An argument is null.</exception>
        public ServerPaths(string packageDirectory, ServerSettings settings)
        {
            if (packageDirectory == null)
            {
                throw new ArgumentNullException(nameof(packageDirectory));
            }

            if (settings == null)
            {
                throw new ArgumentNullException(nameof(settings));
            }

            PackageDirectory = Path.GetFullPath(packageDirectory);
            LogsDirectory = Resolve(settings.LogsDirectory, ServerSettings.DefaultLogsDirectory);
            SavesDirectory = Resolve(settings.SavesDirectory, ServerSettings.DefaultSavesDirectory);
        }

        /// <summary>The package folder, full path.</summary>
        public string PackageDirectory { get; }

        /// <summary>The logs folder, full path.</summary>
        public string LogsDirectory { get; }

        /// <summary>The saves folder, full path: where a game keeps everything that must survive a restart or an update.</summary>
        public string SavesDirectory { get; }

        /// <summary>Creates the logs and saves folders when they are missing.</summary>
        public void EnsureCreated()
        {
            Directory.CreateDirectory(LogsDirectory);
            Directory.CreateDirectory(SavesDirectory);
        }

        /// <summary>A path under <see cref="SavesDirectory"/>; refuses a relative path that climbs out of it.</summary>
        /// <exception cref="ArgumentException"><paramref name="relativePath"/> is empty, rooted or leaves the saves folder.</exception>
        public string SavePath(string relativePath)
        {
            if (string.IsNullOrWhiteSpace(relativePath) || Path.IsPathRooted(relativePath))
            {
                throw new ArgumentException("Give a path relative to the saves folder.", nameof(relativePath));
            }

            string root = SavesDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            string full = Path.GetFullPath(Path.Combine(root, relativePath));
            if (!full.StartsWith(root, StringComparison.Ordinal))
            {
                throw new ArgumentException("'" + relativePath + "' leaves the saves folder.", nameof(relativePath));
            }

            return full;
        }

        private string Resolve(string directory, string fallback)
        {
            string dir = string.IsNullOrWhiteSpace(directory) ? fallback : directory.Trim();
            return Path.GetFullPath(Path.IsPathRooted(dir) ? dir : Path.Combine(PackageDirectory, dir));
        }
    }
}
