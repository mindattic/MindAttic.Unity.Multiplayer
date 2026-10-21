// Headless role driver for the nightly Docker Compose smoke test (RFC 0001, UM-T17: "server + 2 bot clients join by
// code and exchange RPCs"). Copied into the throwaway host project's Assets by tests/run-smoke.ps1 (and
// .github/workflows/nightly-smoke.yml), alongside the imported Minimal sample, so one Linux Player build plays every
// part; MINDATTIC_ROLE picks which at run time:
//   server - the dedicated server, started automatically by NetLaunch from "-mode server" on the command line. This
//            driver only watches for the join code the server publishes to the rendezvous service and writes it to
//            the shared volume so the bots can read it.
//   bot    - waits for that file, joins by code through the rendezvous service, then drives its square into the
//            arena wall (one [ToServer] call repeated, one [Synced] field read back) as proof the RPC round trip and
//            the join-code flow both work end to end.
// Status lands as files in the shared directory (MINDATTIC_SHARED_DIR, default /shared) and as log lines, not the
// process exit code: a Unity Player's exit code is not reliable in every runtime, so smoke/wait-for-smoke.sh polls
// "docker compose logs" for this driver's "OK:" / "FAIL:" lines instead.
using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Unity.Multiplayer;
using Unity.Multiplayer.Core;
using Unity.Multiplayer.Samples.Minimal;
using UnityEngine;

public sealed class SmokeDriver : MonoBehaviour
{
    private const string RoleEnv = "MINDATTIC_ROLE";
    private const string NameEnv = "MINDATTIC_BOT_NAME";
    private const string SharedDirEnv = "MINDATTIC_SHARED_DIR";
    private const string RendezvousEnv = "MINDATTIC_RENDEZVOUS_URL";
    private const string TimeoutEnv = "MINDATTIC_TIMEOUT_SECONDS";
    private const string CodeFileName = "join-code.txt";

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        string role = (Environment.GetEnvironmentVariable(RoleEnv) ?? string.Empty).Trim().ToLowerInvariant();
        if (role != "server" && role != "bot")
        {
            return; // not a smoke-test launch; leave the sample's normal menu in charge.
        }

        var holder = new GameObject("SmokeDriver");
        UnityEngine.Object.DontDestroyOnLoad(holder);
        holder.AddComponent<SmokeDriver>()._role = role;
    }

    private string _role;
    private string _name;
    private string _sharedDir;
    private float _deadline;

    private void Start()
    {
        _name = Environment.GetEnvironmentVariable(NameEnv);
        if (string.IsNullOrWhiteSpace(_name))
        {
            _name = _role;
        }

        _sharedDir = Environment.GetEnvironmentVariable(SharedDirEnv);
        if (string.IsNullOrWhiteSpace(_sharedDir))
        {
            _sharedDir = "/shared";
        }

        int timeoutSeconds;
        if (!int.TryParse(Environment.GetEnvironmentVariable(TimeoutEnv), out timeoutSeconds) || timeoutSeconds <= 0)
        {
            timeoutSeconds = 90;
        }

        _deadline = Time.realtimeSinceStartup + timeoutSeconds;

        try
        {
            Directory.CreateDirectory(_sharedDir);
        }
        catch (Exception ex)
        {
            Log("could not create the shared directory " + _sharedDir + ": " + ex.Message);
        }

        StartCoroutine(_role == "server" ? RunServer() : RunBot());
    }

    // Server: no need to start anything (the command line's "-mode server" already made NetLaunch do that); just
    // capture the join code the server publishes to the rendezvous service and hand it to the bots.
    private IEnumerator RunServer()
    {
        while (NetBootstrap.Instance == null || NetBootstrap.Instance.Session == null)
        {
            if (Time.realtimeSinceStartup > _deadline)
            {
                Fail("the server never started a session.");
                yield break;
            }

            yield return null;
        }

        NetSession session = NetBootstrap.Instance.Session;
        string published = null;
        session.JoinCodePublished += code => published = code.ToString();

        while (published == null && string.IsNullOrEmpty(session.PublishedCodeText) && Time.realtimeSinceStartup < _deadline)
        {
            yield return null;
        }

        string code = published ?? session.PublishedCodeText;
        if (string.IsNullOrEmpty(code))
        {
            Fail("the server never published a join code (is the rendezvous service reachable?).");
            yield break;
        }

        File.WriteAllText(Path.Combine(_sharedDir, CodeFileName), code.Trim());
        Log("OK: published join code " + code);
        WriteMarker(".ok", "published join code " + code);
        // Keeps running (no Finish/exit) so the bots can connect; the smoke workflow tears the whole compose stack
        // down once wait-for-smoke.sh is done.
    }

    // Bot: wait for the server's code, join by it, then drive the owned player into a wall and watch the server's
    // reply (the Synced Position) come back.
    private IEnumerator RunBot()
    {
        string codePath = Path.Combine(_sharedDir, CodeFileName);
        while (!File.Exists(codePath) && Time.realtimeSinceStartup < _deadline)
        {
            yield return new WaitForSecondsRealtime(0.5f);
        }

        if (!File.Exists(codePath))
        {
            Fail("never saw " + codePath + " (the server did not publish a code in time).");
            yield break;
        }

        string code = File.ReadAllText(codePath).Trim();
        while (NetBootstrap.Instance == null)
        {
            if (Time.realtimeSinceStartup > _deadline)
            {
                Fail("NetBootstrap never appeared.");
                yield break;
            }

            yield return null;
        }

        NetBootstrap bootstrap = NetBootstrap.Instance;
        NetConfig config = bootstrap.BuildConfig();
        string rendezvousUrl = Environment.GetEnvironmentVariable(RendezvousEnv);
        if (!string.IsNullOrWhiteSpace(rendezvousUrl))
        {
            config.RendezvousUrl = rendezvousUrl.Trim();
        }

        config.JoinCodes = JoinCodeProvider.Rendezvous;

        Task<bool> join = bootstrap.Session.JoinByCode(code, config);
        while (!join.IsCompleted && Time.realtimeSinceStartup < _deadline)
        {
            yield return null;
        }

        if (!join.IsCompleted || join.IsFaulted || !join.Result)
        {
            string why = join.IsFaulted ? join.Exception?.GetBaseException().Message : "timed out or refused";
            Fail("JoinByCode(" + code + ") failed: " + why);
            yield break;
        }

        while (!bootstrap.Session.IsActive && Time.realtimeSinceStartup < _deadline)
        {
            yield return null;
        }

        MinimalPlayer player = null;
        while (player == null && Time.realtimeSinceStartup < _deadline)
        {
            player = UnityEngine.Object.FindObjectsByType<MinimalPlayer>(FindObjectsSortMode.None).FirstOrDefault(p => p.IsOwner);
            yield return null;
        }

        if (player == null)
        {
            Fail("connected, but no owned MinimalPlayer ever spawned.");
            yield break;
        }

        // Drive the square into a wall: one [ToServer] call repeated, one [Synced] field (Position) read back once
        // the server replicates it. Reaching the wall proves the round trip (client -> server -> every client).
        Vector2 direction = Vector2.right;
        while (Time.realtimeSinceStartup < _deadline)
        {
            if (player.IsSpawned)
            {
                player.SetDirection(direction);
                if (player.Position.x >= MinimalPlayer.ArenaHalfSize - 0.05f)
                {
                    Succeed("reached the wall at " + player.Position + " after joining with code " + code + ".");
                    yield break;
                }
            }

            yield return new WaitForSecondsRealtime(0.1f);
        }

        Fail("never reached the wall (stuck at " + player.Position + ").");
    }

    private void Succeed(string message)
    {
        Log("OK: " + message);
        WriteMarker(".ok", message);
        Finish(0);
    }

    private void Fail(string message)
    {
        Debug.LogError("[SmokeDriver:" + _name + "] FAIL: " + message);
        WriteMarker(".fail", message);
        Finish(1);
    }

    private void Finish(int exitCode)
    {
        try
        {
            Environment.Exit(exitCode);
        }
        catch (Exception)
        {
            // Best effort; the compose smoke step and its own timeout clean up regardless.
        }
    }

    private void WriteMarker(string suffix, string message)
    {
        try
        {
            File.WriteAllText(Path.Combine(_sharedDir, _name + suffix), message + Environment.NewLine);
        }
        catch (Exception ex)
        {
            Debug.LogError("[SmokeDriver:" + _name + "] could not write its status file: " + ex.Message);
        }
    }

    private void Log(string message)
    {
        Debug.Log("[SmokeDriver:" + _name + "] " + message);
    }
}
