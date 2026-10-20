using System;
using System.Globalization;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;

namespace MindAttic.Spike
{
    /// <summary>
    /// Command-line driver for the standalone runs:
    ///   -spikeMode server|host|client  -spikeAddress 127.0.0.1  -spikePort 7777  -spikeTimeout 60
    /// server/host: spawns one SpikeEntity per remote client, owned by it; exits 0 once a client that pinged leaves.
    /// client: calls Ping(7) on its own SpikeEntity; exits 0 once Total == 7 has replicated back.
    /// A timeout exits 1.
    /// </summary>
    public sealed class SpikeBootstrap : MonoBehaviour
    {
        public GameObject entityPrefab;
        public const int PingAmount = 7;

        string _mode;
        float _deadline;
        bool _pinged;
        bool _serverSawPing;
        bool _done;

        void Start()
        {
            Application.runInBackground = true;
            Application.targetFrameRate = 30;
            _mode = Arg("-spikeMode", "host");
            string address = Arg("-spikeAddress", "127.0.0.1");
            ushort port = ushort.Parse(Arg("-spikePort", "7777"), CultureInfo.InvariantCulture);
            _deadline = Time.realtimeSinceStartup + float.Parse(Arg("-spikeTimeout", "60"), CultureInfo.InvariantCulture);

            NetworkManager nm = NetworkManager.Singleton;
            nm.AddNetworkPrefab(entityPrefab);
            nm.GetComponent<UnityTransport>().SetConnectionData(address, port, "0.0.0.0");
            nm.OnClientConnectedCallback += OnClientConnected;
            nm.OnClientDisconnectCallback += OnClientDisconnected;

            bool ok;
            switch (_mode)
            {
                case "server": ok = nm.StartServer(); break;
                case "host": ok = nm.StartHost(); break;
                case "client": ok = nm.StartClient(); break;
                default: ok = false; break;
            }
            Debug.Log("[spike] mode=" + _mode + " address=" + address + ":" + port + " started=" + ok
                + " platform=" + Application.platform + " backend=" + Backend() + " batch=" + Application.isBatchMode);
            if (!ok) Quit(1);
        }

        static string Backend()
        {
#if ENABLE_IL2CPP
            return "IL2CPP";
#else
            return "Mono";
#endif
        }

        void OnClientConnected(ulong clientId)
        {
            NetworkManager nm = NetworkManager.Singleton;
            if (!nm.IsServer || clientId == NetworkManager.ServerClientId) return;
            NetworkObject no = Instantiate(entityPrefab).GetComponent<NetworkObject>();
            no.SpawnWithOwnership(clientId);
            Debug.Log("[spike] server: client " + clientId + " connected, spawned SpikeEntity " + no.NetworkObjectId);
        }

        void OnClientDisconnected(ulong clientId)
        {
            if (_done) return;
            NetworkManager nm = NetworkManager.Singleton;
            if (nm.IsServer)
            {
                if (clientId == NetworkManager.ServerClientId) return;
                Debug.Log("[spike] server: client " + clientId + " left (ping seen=" + _serverSawPing + ")");
                if (_serverSawPing) { Debug.Log("[spike] PASS server"); Quit(0); }
            }
            else
            {
                Debug.LogError("[spike] FAIL client: disconnected before Total replicated");
                Quit(1);
            }
        }

        void Update()
        {
            if (_done) return;
            NetworkManager nm = NetworkManager.Singleton;

            if (nm.IsListening && nm.IsServer)
            {
                foreach (NetworkObject no in nm.SpawnManager.SpawnedObjectsList)
                {
                    SpikeEntity e = no.GetComponent<SpikeEntity>();
                    if (e != null && e.PingsReceived > 0) _serverSawPing = true;
                }
            }

            if (nm.IsListening && nm.IsClient && !nm.IsServer)
            {
                foreach (NetworkObject no in nm.SpawnManager.SpawnedObjectsList)
                {
                    SpikeEntity e = no.GetComponent<SpikeEntity>();
                    if (e == null || !e.IsOwner) continue;
                    if (!_pinged)
                    {
                        _pinged = true;
                        Debug.Log("[spike] client " + nm.LocalClientId + ": calling Ping(" + PingAmount + ") on entity " + no.NetworkObjectId);
                        e.Ping(PingAmount);   // the partial method; the generated body sends PingRpc
                    }
                    else if (e.Total == PingAmount)
                    {
                        Debug.Log("[spike] PASS client: Total=" + e.Total + " replicated from the server");
                        _done = true;
                        nm.Shutdown();
                        Quit(0);
                        return;
                    }
                }
            }

            if (Time.realtimeSinceStartup > _deadline)
            {
                Debug.LogError("[spike] FAIL " + _mode + ": timed out (pinged=" + _pinged + ", serverSawPing=" + _serverSawPing + ")");
                Quit(1);
            }
        }

        void Quit(int code)
        {
            _done = true;
            Debug.Log("[spike] exit " + code);
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit(code);
#endif
        }

        static string Arg(string name, string fallback)
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
                if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase)) return args[i + 1];
            return fallback;
        }
    }
}
