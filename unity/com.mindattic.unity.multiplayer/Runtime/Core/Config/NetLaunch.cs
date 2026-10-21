using System;

namespace Unity.Multiplayer.Core
{
    /// <summary>
    /// Decides whether a process starts a session by itself at launch, and as what. The rule NetBootstrap
    /// follows, kept engine-free so it is tested by plain <c>dotnet test</c>.
    /// </summary>
    /// <remarks>
    /// <list type="number">
    /// <item>A bot (<c>-bot &lt;role&gt;</c>) never starts by itself: the bot runner joins once it has a join code.</item>
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

            if (commandLine.IsBot)
            {
                return false;
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

        /// <summary>
        /// True when a player-facing front end (menus, dialogs) may be created: never in a headless process (a
        /// dedicated server, a CI run), and never when <c>-mode</c> or <c>-bot</c> launched the process into a role by
        /// itself (a server, or a bot client), since nobody is there to click.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="commandLine"/> is null.</exception>
        public static bool ShowsMenus(NetCommandLineResult commandLine, bool headless)
        {
            if (commandLine == null)
            {
                throw new ArgumentNullException(nameof(commandLine));
            }

            return !headless && !commandLine.TopologySpecified && !commandLine.IsBot;
        }
    }
}
