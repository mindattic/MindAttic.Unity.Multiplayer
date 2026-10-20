using System;

namespace MindAttic.Unity.Multiplayer.Core
{
    /// <summary>
    /// Decides whether a process starts a session by itself at launch, and as what. The rule NetBootstrap
    /// follows, kept engine-free so it is tested by plain <c>dotnet test</c>.
    /// </summary>
    /// <remarks>
    /// <list type="number">
    /// <item>A <c>-mode</c> on the command line always wins: the process starts as that topology.</item>
    /// <item>Otherwise a headless process (batch mode, <c>-nographics</c>, a Dedicated Server build) with
    /// <c>serveWhenHeadless</c> set starts as a dedicated <see cref="Topology.Server"/>.</item>
    /// <item>Otherwise it starts only when <c>autoStart</c> is set, as the configured topology.</item>
    /// </list>
    /// </remarks>
    public static class NetLaunch
    {
        /// <summary>
        /// Applies the rule above. Returns true when the session should start now; may set
        /// <paramref name="config"/>'s topology to <see cref="Topology.Server"/> (rule 2).
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="config"/> or <paramref name="commandLine"/> is null.</exception>
        public static bool ShouldStart(NetConfig config, NetCommandLineResult commandLine, bool headless, bool autoStart, bool serveWhenHeadless)
        {
            if (config == null)
            {
                throw new ArgumentNullException(nameof(config));
            }

            if (commandLine == null)
            {
                throw new ArgumentNullException(nameof(commandLine));
            }

            if (commandLine.TopologySpecified)
            {
                return true;
            }

            if (headless && serveWhenHeadless)
            {
                config.Topology = Topology.Server;
                return true;
            }

            return autoStart;
        }
    }
}
