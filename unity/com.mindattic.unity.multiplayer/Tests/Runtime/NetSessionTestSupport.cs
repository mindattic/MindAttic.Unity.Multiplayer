using Unity.Netcode;

namespace Unity.Multiplayer.Tests
{
    internal static class NetSessionTestSupport
    {
        /// <summary>
        /// Readies harness-created managers for NetSession to start. Scene management is off because these
        /// tests start NGO themselves, so the harness never registers the test-runner scene with it; connect and
        /// disconnect need no scene sync.
        /// </summary>
        public static void PrepareManagers(NetworkManager[] managers)
        {
            foreach (NetworkManager manager in managers)
            {
                manager.NetworkConfig.EnableSceneManagement = false;
            }
        }
    }
}
