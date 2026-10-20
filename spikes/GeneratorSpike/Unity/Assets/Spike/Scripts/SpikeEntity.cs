using Unity.Netcode;
using UnityEngine;

namespace MindAttic.Spike
{
    /// <summary>
    /// What a game would write. Nothing here is NGO plumbing: Ping's body, PingRpc and the
    /// NetworkVariable behind Total all come from MindAttic.Spike.Generators.
    /// </summary>
    public partial class SpikeEntity : NetworkBehaviour
    {
        [ToServer]
        public partial void Ping(int amount);

#pragma warning disable CS0169 // the generator reads this declaration; the value lives in the generated NetworkVariable
        [Synced] int _total;
#pragma warning restore CS0169

        /// <summary>The client id the server last saw a Ping from (server side only).</summary>
        public ulong LastSender { get; private set; } = ulong.MaxValue;
        public int PingsReceived { get; private set; }

        void OnPing(int amount, ulong sender)
        {
            // Runs on the server only (UM-LAW-1): intent in, authoritative state out.
            LastSender = sender;
            PingsReceived++;
            Total += amount;
            Debug.Log("[spike] server: Ping(" + amount + ") from client " + sender + " -> Total=" + Total);
        }
    }
}
