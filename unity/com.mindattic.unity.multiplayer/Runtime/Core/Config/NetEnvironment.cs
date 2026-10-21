using System;

namespace Unity.Multiplayer.Core
{
    /// <summary>
    /// Reads join-code settings from environment variables onto a <see cref="NetConfig"/>: how a container or a
    /// hosting provider hands a dedicated server its rendezvous URL and secret without putting the secret on a
    /// command line other processes can read. Applied before the command line, so an argument still wins.
    /// </summary>
    /// <remarks>
    /// <list type="table">
    /// <item><term><c>MINDATTIC_RENDEZVOUS_URL</c></term><description><see cref="NetConfig.RendezvousUrl"/>.</description></item>
    /// <item><term><c>MINDATTIC_RENDEZVOUS_SECRET</c></term><description><see cref="NetConfig.RendezvousSecret"/>.</description></item>
    /// <item><term><c>MINDATTIC_PUBLIC_ADDRESS</c></term><description><see cref="NetConfig.PublicAddress"/>.</description></item>
    /// <item><term><c>MINDATTIC_SERVER_PASSWORD</c></term><description><see cref="NetConfig.Password"/>.</description></item>
    /// </list>
    /// Unset or empty variables change nothing. RunPod's own <c>RUNPOD_PUBLIC_IP</c> and <c>RUNPOD_TCP_PORT_*</c> are
    /// read later, by <see cref="ServerAdvertisement.For"/>.
    /// </remarks>
    public static class NetEnvironment
    {
        /// <summary>Environment variable for <see cref="NetConfig.RendezvousUrl"/>.</summary>
        public const string RendezvousUrlVariable = "MINDATTIC_RENDEZVOUS_URL";

        /// <summary>Environment variable for <see cref="NetConfig.RendezvousSecret"/>.</summary>
        public const string RendezvousSecretVariable = "MINDATTIC_RENDEZVOUS_SECRET";

        /// <summary>Environment variable for <see cref="NetConfig.PublicAddress"/>.</summary>
        public const string PublicAddressVariable = "MINDATTIC_PUBLIC_ADDRESS";

        /// <summary>Environment variable for <see cref="NetConfig.Password"/>.</summary>
        public const string ServerPasswordVariable = "MINDATTIC_SERVER_PASSWORD";

        /// <summary>Applies every set variable to <paramref name="config"/>.</summary>
        /// <param name="config">The config to fill.</param>
        /// <param name="environment">Reads a variable (null when unset); null reads the process environment.</param>
        /// <exception cref="ArgumentNullException"><paramref name="config"/> is null.</exception>
        public static void Apply(NetConfig config, Func<string, string> environment = null)
        {
            if (config == null)
            {
                throw new ArgumentNullException(nameof(config));
            }

            Func<string, string> env = environment ?? Environment.GetEnvironmentVariable;
            string url = env(RendezvousUrlVariable);
            if (!string.IsNullOrWhiteSpace(url))
            {
                config.RendezvousUrl = url.Trim();
            }

            string secret = env(RendezvousSecretVariable);
            if (!string.IsNullOrEmpty(secret))
            {
                config.RendezvousSecret = secret;
            }

            string publicAddress = env(PublicAddressVariable);
            if (!string.IsNullOrWhiteSpace(publicAddress))
            {
                config.PublicAddress = publicAddress.Trim();
            }

            string password = env(ServerPasswordVariable);
            if (!string.IsNullOrEmpty(password))
            {
                config.Password = password;
            }
        }
    }
}
