using System.Collections;
using NUnit.Framework;
using Unity.Netcode;
using Unity.Netcode.TestHelpers.Runtime;
using UnityEngine;
using UnityEngine.TestTools;

namespace MindAttic.Spike.Tests
{
    /// <summary>
    /// UM-T4: host + 1 client in one process. The client calls the generated partial method; the
    /// generated [Rpc(SendTo.Server)] stub has to survive NGO's ILPP, reach OnPing on the host with
    /// the right sender, and the generated NetworkVariable has to replicate the result back.
    /// </summary>
    public class SpikeRpcTests : NetcodeIntegrationTest
    {
        protected override int NumberOfClients => 1;

        GameObject _prefab;

        protected override void OnServerAndClientsCreated()
        {
            _prefab = CreateNetworkObjectPrefab("SpikeEntity");
            _prefab.AddComponent<SpikeEntity>();
            base.OnServerAndClientsCreated();
        }

        [UnityTest]
        public IEnumerator GeneratedServerRpcAndNetworkVariableRoundTrip()
        {
            NetworkManager client = m_ClientNetworkManagers[0];
            GameObject spawned = SpawnObject(_prefab, client);
            ulong id = spawned.GetComponent<NetworkObject>().NetworkObjectId;
            SpikeEntity onServer = spawned.GetComponent<SpikeEntity>();

            yield return WaitForConditionOrTimeOut(() => client.SpawnManager.SpawnedObjects.ContainsKey(id));
            AssertOnTimeout("the client never saw the spawned SpikeEntity");
            SpikeEntity onClient = client.SpawnManager.SpawnedObjects[id].GetComponent<SpikeEntity>();
            Assert.IsTrue(onClient.IsOwner, "the client should own its SpikeEntity");

            onClient.Ping(7);
            onClient.Ping(5);

            yield return WaitForConditionOrTimeOut(() => onServer.PingsReceived == 2 && onClient.Total == 12);
            AssertOnTimeout("Ping never reached the server, or Total never replicated (server pings="
                + onServer.PingsReceived + ", server Total=" + onServer.Total + ", client Total=" + onClient.Total + ")");

            Assert.AreEqual(client.LocalClientId, onServer.LastSender, "RpcParams sender should be the calling client");
            Assert.AreEqual(12, onServer.Total);
            Assert.AreEqual(0, onClient.PingsReceived, "OnPing must run only on the server");
        }
    }
}
