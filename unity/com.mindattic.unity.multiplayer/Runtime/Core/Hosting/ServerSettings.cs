using System;
using System.Collections.Generic;

namespace MindAttic.Unity.Multiplayer.Core
{
    /// <summary>
    /// The server package's own settings, beside the session's <see cref="NetConfig"/>: the health endpoint, the admin
    /// console, where logs and saves go and how long a clean shutdown may take. Filled from <c>server.json</c> and the
    /// command line by <see cref="ServerStartup"/>.
    /// </summary>
    public sealed class ServerSettings
    {
        /// <summary>The health endpoint's default TCP port.</summary>
        public const ushort DefaultHealthPort = 8081;

        /// <summary>The health endpoint's path.</summary>
        public const string HealthPath = "/healthz";

        /// <summary>The default logs folder, relative to the package folder.</summary>
        public const string DefaultLogsDirectory = "Logs";

        /// <summary>The default saves folder, relative to the package folder.</summary>
        public const string DefaultSavesDirectory = "Saves";

        /// <summary>The default seconds a clean shutdown may take before the process quits anyway.</summary>
        public const double DefaultShutdownGraceSeconds = 10.0;

        /// <summary>The longest accepted <see cref="ShutdownGraceSeconds"/>.</summary>
        public const double MaxShutdownGraceSeconds = 300.0;

        /// <summary>The default number of log files kept in <see cref="LogsDirectory"/>.</summary>
        public const int DefaultKeepLogFiles = 10;

        /// <summary>
        /// TCP port of the HTTP health endpoint (<c>GET /healthz</c>, see <see cref="HealthEndpoint"/>); 0 turns it off.
        /// </summary>
        public ushort HealthPort { get; set; } = DefaultHealthPort;

        /// <summary>Read admin commands from standard input (<see cref="AdminConsole"/>). Off by default.</summary>
        public bool AdminCommands { get; set; }

        /// <summary>Where the server writes its log files; relative paths are under the package folder.</summary>
        public string LogsDirectory { get; set; } = DefaultLogsDirectory;

        /// <summary>Where the game keeps persistent data; relative paths are under the package folder.</summary>
        public string SavesDirectory { get; set; } = DefaultSavesDirectory;

        /// <summary>Log files kept in <see cref="LogsDirectory"/>; older ones are deleted at start. 0 keeps every file.</summary>
        public int KeepLogFiles { get; set; } = DefaultKeepLogFiles;

        /// <summary>Seconds a clean shutdown (SIGTERM, SIGINT, <c>stop</c>) may take before the process quits anyway.</summary>
        public double ShutdownGraceSeconds { get; set; } = DefaultShutdownGraceSeconds;

        /// <summary>Returns a field-by-field copy.</summary>
        public ServerSettings Clone()
        {
            return (ServerSettings)MemberwiseClone();
        }

        /// <summary>Lists every problem; an empty list means the settings are valid.</summary>
        public IReadOnlyList<string> Validate()
        {
            var errors = new List<string>();
            if (string.IsNullOrWhiteSpace(LogsDirectory))
            {
                errors.Add("LogsDirectory must not be empty.");
            }

            if (string.IsNullOrWhiteSpace(SavesDirectory))
            {
                errors.Add("SavesDirectory must not be empty.");
            }

            if (KeepLogFiles < 0)
            {
                errors.Add("KeepLogFiles must not be negative; got " + KeepLogFiles + ".");
            }

            if (double.IsNaN(ShutdownGraceSeconds) || ShutdownGraceSeconds < 0 || ShutdownGraceSeconds > MaxShutdownGraceSeconds)
            {
                errors.Add("ShutdownGraceSeconds must be between 0 and " + MaxShutdownGraceSeconds + "; got " + ShutdownGraceSeconds + ".");
            }

            return errors;
        }

        /// <inheritdoc />
        public override string ToString()
        {
            return "health " + (HealthPort == 0 ? "off" : HealthPort + HealthPath) + ", admin " + (AdminCommands ? "on" : "off")
                + ", logs " + LogsDirectory + ", saves " + SavesDirectory;
        }
    }
}
