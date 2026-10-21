using Unity.Netcode;

namespace Unity.Multiplayer.Testing
{
    /// <summary>Makes a <see cref="NetSession"/> over a NetworkManager a test created itself.</summary>
    /// <remarks>
    /// A game gets its session from <see cref="NetBootstrap.Session"/>; NetSession's constructor stays internal so the
    /// runtime API never takes an NGO type (BIBLE UM-LAW-2) and no game builds a second session beside its bootstrap.
    /// A test that drives the NGO integration harness's own managers (as <see cref="NetSessionTest"/> does) needs one per
    /// manager without a NetBootstrap (which would start serving by itself in a headless editor), so this assembly, which
    /// never ships in a player, is the one public way to build one.
    /// </remarks>
    public static class TestSessions
    {
        /// <summary>
        /// A session driving <paramref name="manager"/>, which from now on owns its start, stop and transport settings.
        /// Dispose it before the manager is destroyed.
        /// </summary>
        /// <exception cref="System.ArgumentNullException"><paramref name="manager"/> is null.</exception>
        public static NetSession Create(NetworkManager manager)
        {
            return new NetSession(manager);
        }

        /// <summary>The NetworkManager <paramref name="session"/> drives, for the NGO calls a test still has to make itself.</summary>
        public static NetworkManager ManagerOf(NetSession session)
        {
            return session.Manager;
        }
    }
}
