using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Unity.Multiplayer.Core
{
    /// <summary>
    /// Reads and writes <c>server.json</c>, the server package's settings file: one flat JSON object beside the server
    /// executable. The package's build writes it with every setting at its default, and the person running the server
    /// edits it; the environment and the command line still override it (<see cref="ServerStartup"/>).
    /// </summary>
    /// <remarks>
    /// <para>Keys (camelCase, matched ignoring case; every key is optional):</para>
    /// <list type="table">
    /// <item><term><c>name</c></term><description>Server name (<see cref="NetConfig.ServerName"/>).</description></item>
    /// <item><term><c>password</c></term><description>Server password, empty for none (<see cref="NetConfig.Password"/>).</description></item>
    /// <item><term><c>port</c></term><description>Game port, 1-65535 (<see cref="NetConfig.Port"/>).</description></item>
    /// <item><term><c>maxPlayers</c></term><description>Player cap (<see cref="NetConfig.MaxPlayers"/>).</description></item>
    /// <item><term><c>address</c></term><description>Local address to bind, empty for every interface (<see cref="NetConfig.Address"/>).</description></item>
    /// <item><term><c>transport</c></term><description><c>udp</c> or <c>websocket</c> (<see cref="NetConfig.Transport"/>).</description></item>
    /// <item><term><c>tickRate</c></term><description>Ticks per second (<see cref="NetConfig.TickRate"/>).</description></item>
    /// <item><term><c>reconnectGraceSeconds</c></term><description>Seconds a dropped player's seat is held (<see cref="NetConfig.ReconnectGraceSeconds"/>).</description></item>
    /// <item><term><c>joinCodes</c></term><description><c>auto</c>, <c>none</c>, <c>relay</c> or <c>rendezvous</c> (<see cref="NetConfig.JoinCodes"/>).</description></item>
    /// <item><term><c>rendezvousUrl</c></term><description>Rendezvous service URL (<see cref="NetConfig.RendezvousUrl"/>). Its secret belongs in <c>MINDATTIC_RENDEZVOUS_SECRET</c>, never this file.</description></item>
    /// <item><term><c>publicAddress</c> / <c>publicPort</c></term><description>Where players reach the server, when it differs (<see cref="NetConfig.PublicAddress"/>, <see cref="NetConfig.PublicPort"/>).</description></item>
    /// <item><term><c>healthPort</c></term><description>Health endpoint TCP port, 0 for off (<see cref="ServerSettings.HealthPort"/>).</description></item>
    /// <item><term><c>adminCommands</c></term><description><c>true</c> to read admin commands from standard input (<see cref="ServerSettings.AdminCommands"/>).</description></item>
    /// <item><term><c>logsDirectory</c> / <c>savesDirectory</c></term><description>Folders for logs and persistent data (<see cref="ServerSettings.LogsDirectory"/>, <see cref="ServerSettings.SavesDirectory"/>).</description></item>
    /// <item><term><c>keepLogFiles</c></term><description>Log files kept (<see cref="ServerSettings.KeepLogFiles"/>).</description></item>
    /// <item><term><c>shutdownGraceSeconds</c></term><description>Seconds a clean shutdown may take (<see cref="ServerSettings.ShutdownGraceSeconds"/>).</description></item>
    /// </list>
    /// <para>A key with a value of the wrong kind is an error and leaves that setting unchanged; an unknown key is a
    /// warning, so a file written for a newer build still loads.</para>
    /// </remarks>
    public static class ServerConfigFile
    {
        /// <summary>The settings file's name in the package folder.</summary>
        public const string FileName = "server.json";

        /// <summary>
        /// Applies the settings in <paramref name="json"/> to <paramref name="config"/> and <paramref name="settings"/>.
        /// </summary>
        /// <returns>Errors and warnings; when the text is not a JSON object nothing is applied and that is the one error.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="config"/> or <paramref name="settings"/> is null.</exception>
        public static ServerConfigResult Apply(string json, NetConfig config, ServerSettings settings)
        {
            if (config == null)
            {
                throw new ArgumentNullException(nameof(config));
            }

            if (settings == null)
            {
                throw new ArgumentNullException(nameof(settings));
            }

            var errors = new List<string>();
            var warnings = new List<string>();
            Dictionary<string, object> fields;
            if (!RendezvousJson.TryParseObject(json, out fields))
            {
                errors.Add(FileName + " is not a JSON object.");
                return new ServerConfigResult(errors, warnings);
            }

            foreach (KeyValuePair<string, object> field in fields)
            {
                string key = field.Key.ToLowerInvariant();
                object value = field.Value;
                switch (key)
                {
                    case "name":
                        if (TryString(field, errors, out string name))
                        {
                            config.ServerName = name;
                        }

                        break;
                    case "password":
                        if (TryString(field, errors, out string password))
                        {
                            config.Password = password;
                        }

                        break;
                    case "address":
                        if (TryString(field, errors, out string address))
                        {
                            config.Address = address;
                        }

                        break;
                    case "port":
                        if (TryInteger(field, 1, ushort.MaxValue, errors, out long port))
                        {
                            config.Port = (ushort)port;
                        }

                        break;
                    case "maxplayers":
                        if (TryInteger(field, 1, NetConfig.MaxMaxPlayers, errors, out long maxPlayers))
                        {
                            config.MaxPlayers = (int)maxPlayers;
                        }

                        break;
                    case "tickrate":
                        if (TryInteger(field, 1, NetConfig.MaxTickRate, errors, out long tickRate))
                        {
                            config.TickRate = (int)tickRate;
                        }

                        break;
                    case "transport":
                        if (TryString(field, errors, out string transportText))
                        {
                            if (NetCommandLine.TryParseTransport(transportText, out TransportKind transport))
                            {
                                config.Transport = transport;
                            }
                            else
                            {
                                errors.Add(Name(field) + " must be \"udp\" or \"websocket\"; got \"" + transportText + "\".");
                            }
                        }

                        break;
                    case "joincodes":
                        if (TryString(field, errors, out string joinText))
                        {
                            if (NetCommandLine.TryParseJoinCodes(joinText, out JoinCodeProvider provider))
                            {
                                config.JoinCodes = provider;
                            }
                            else
                            {
                                errors.Add(Name(field) + " must be \"auto\", \"none\", \"relay\" or \"rendezvous\"; got \"" + joinText + "\".");
                            }
                        }

                        break;
                    case "rendezvousurl":
                        if (TryString(field, errors, out string url))
                        {
                            if (url.Length == 0 || RendezvousClient.IsValidBaseUrl(url))
                            {
                                config.RendezvousUrl = url.Length == 0 ? null : url;
                            }
                            else
                            {
                                errors.Add(Name(field) + " must be an absolute http:// or https:// URL; got \"" + url + "\".");
                            }
                        }

                        break;
                    case "rendezvoussecret":
                        warnings.Add(Name(field) + " is ignored: put the rendezvous secret in the " + NetEnvironment.RendezvousSecretVariable
                            + " environment variable, never in " + FileName + ".");
                        break;
                    case "publicaddress":
                        if (TryString(field, errors, out string publicAddress))
                        {
                            config.PublicAddress = publicAddress.Length == 0 ? null : publicAddress;
                        }

                        break;
                    case "publicport":
                        if (TryInteger(field, 0, ushort.MaxValue, errors, out long publicPort))
                        {
                            config.PublicPort = (ushort)publicPort;
                        }

                        break;
                    case "reconnectgraceseconds":
                        if (TryNumber(field, 0, NetConfig.MaxReconnectGraceSeconds, errors, out double grace))
                        {
                            config.ReconnectGraceSeconds = grace;
                        }

                        break;
                    case "healthport":
                        if (TryInteger(field, 0, ushort.MaxValue, errors, out long healthPort))
                        {
                            settings.HealthPort = (ushort)healthPort;
                        }

                        break;
                    case "admincommands":
                        if (value is bool admin)
                        {
                            settings.AdminCommands = admin;
                        }
                        else
                        {
                            errors.Add(Name(field) + " must be true or false.");
                        }

                        break;
                    case "logsdirectory":
                        if (TryDirectory(field, errors, out string logs))
                        {
                            settings.LogsDirectory = logs;
                        }

                        break;
                    case "savesdirectory":
                        if (TryDirectory(field, errors, out string saves))
                        {
                            settings.SavesDirectory = saves;
                        }

                        break;
                    case "keeplogfiles":
                        if (TryInteger(field, 0, int.MaxValue, errors, out long keep))
                        {
                            settings.KeepLogFiles = (int)keep;
                        }

                        break;
                    case "shutdowngraceseconds":
                        if (TryNumber(field, 0, ServerSettings.MaxShutdownGraceSeconds, errors, out double shutdown))
                        {
                            settings.ShutdownGraceSeconds = shutdown;
                        }

                        break;
                    default:
                        warnings.Add(FileName + " has an unknown key \"" + field.Key + "\"; it is ignored.");
                        break;
                }
            }

            return new ServerConfigResult(errors, warnings);
        }

        /// <summary>
        /// Writes <c>server.json</c> holding <paramref name="config"/>'s and <paramref name="settings"/>'s values, one key
        /// per line, in the order the remarks list them. Leaves out the rendezvous secret, always; the password is
        /// written (empty when there is none) so the file shows where it goes.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="config"/> or <paramref name="settings"/> is null.</exception>
        public static string Compose(NetConfig config, ServerSettings settings)
        {
            if (config == null)
            {
                throw new ArgumentNullException(nameof(config));
            }

            if (settings == null)
            {
                throw new ArgumentNullException(nameof(settings));
            }

            var lines = new List<KeyValuePair<string, string>>
            {
                Pair("name", Text(config.ServerName)),
                Pair("password", Text(config.Password)),
                Pair("port", Number(config.Port)),
                Pair("maxPlayers", Number(config.MaxPlayers)),
                Pair("address", Text(config.Address)),
                Pair("transport", Text(config.Transport == TransportKind.WebSocket ? "websocket" : "udp")),
                Pair("tickRate", Number(config.TickRate)),
                Pair("reconnectGraceSeconds", config.ReconnectGraceSeconds.ToString("0.###", CultureInfo.InvariantCulture)),
                Pair("joinCodes", Text(config.JoinCodes.ToString().ToLowerInvariant())),
                Pair("rendezvousUrl", Text(config.RendezvousUrl)),
                Pair("publicAddress", Text(config.PublicAddress)),
                Pair("publicPort", Number(config.PublicPort)),
                Pair("healthPort", Number(settings.HealthPort)),
                Pair("adminCommands", settings.AdminCommands ? "true" : "false"),
                Pair("logsDirectory", Text(settings.LogsDirectory)),
                Pair("savesDirectory", Text(settings.SavesDirectory)),
                Pair("keepLogFiles", Number(settings.KeepLogFiles)),
                Pair("shutdownGraceSeconds", settings.ShutdownGraceSeconds.ToString("0.###", CultureInfo.InvariantCulture)),
            };

            var json = new StringBuilder("{\n");
            for (int i = 0; i < lines.Count; i++)
            {
                json.Append("  ");
                RendezvousJson.WriteString(json, lines[i].Key);
                json.Append(": ").Append(lines[i].Value);
                json.Append(i + 1 < lines.Count ? ",\n" : "\n");
            }

            return json.Append("}\n").ToString();
        }

        private static KeyValuePair<string, string> Pair(string key, string value)
        {
            return new KeyValuePair<string, string>(key, value);
        }

        private static string Text(string value)
        {
            var json = new StringBuilder();
            RendezvousJson.WriteString(json, value ?? string.Empty);
            return json.ToString();
        }

        private static string Number(long value)
        {
            return value.ToString(CultureInfo.InvariantCulture);
        }

        private static string Name(KeyValuePair<string, object> field)
        {
            return FileName + " \"" + field.Key + "\"";
        }

        private static bool TryString(KeyValuePair<string, object> field, List<string> errors, out string value)
        {
            if (field.Value == null)
            {
                value = string.Empty;
                return true;
            }

            value = field.Value as string;
            if (value == null)
            {
                errors.Add(Name(field) + " must be a string.");
                return false;
            }

            value = value.Trim();
            return true;
        }

        private static bool TryDirectory(KeyValuePair<string, object> field, List<string> errors, out string value)
        {
            if (!TryString(field, errors, out value))
            {
                return false;
            }

            if (value.Length == 0)
            {
                errors.Add(Name(field) + " must not be empty.");
                return false;
            }

            return true;
        }

        private static bool TryInteger(KeyValuePair<string, object> field, long min, long max, List<string> errors, out long value)
        {
            var single = new Dictionary<string, object> { { field.Key, field.Value } };
            long? number = RendezvousJson.GetInteger(single, field.Key);
            if (number.HasValue && number.Value >= min && number.Value <= max)
            {
                value = number.Value;
                return true;
            }

            value = 0;
            errors.Add(Name(field) + " must be a whole number from " + min + " to " + max + ".");
            return false;
        }

        private static bool TryNumber(KeyValuePair<string, object> field, double min, double max, List<string> errors, out double value)
        {
            if (field.Value is long l)
            {
                value = l;
            }
            else if (field.Value is double d)
            {
                value = d;
            }
            else
            {
                value = 0;
                errors.Add(Name(field) + " must be a number from " + min + " to " + max + ".");
                return false;
            }

            if (double.IsNaN(value) || value < min || value > max)
            {
                errors.Add(Name(field) + " must be a number from " + min + " to " + max + ".");
                return false;
            }

            return true;
        }
    }

    /// <summary>What <see cref="ServerConfigFile.Apply"/> found.</summary>
    public sealed class ServerConfigResult
    {
        /// <summary>Creates a result.</summary>
        public ServerConfigResult(IReadOnlyList<string> errors, IReadOnlyList<string> warnings)
        {
            Errors = errors ?? new string[0];
            Warnings = warnings ?? new string[0];
        }

        /// <summary>One message per setting that could not be applied. Never null.</summary>
        public IReadOnlyList<string> Errors { get; }

        /// <summary>One message per key that was ignored. Never null.</summary>
        public IReadOnlyList<string> Warnings { get; }

        /// <summary>True when <see cref="Errors"/> is empty.</summary>
        public bool IsValid
        {
            get { return Errors.Count == 0; }
        }
    }
}
