using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Multiplayer.Core;
using Unity.Netcode;
using UnityEngine;

namespace Unity.Multiplayer
{
    /// <summary>
    /// The session's votes, networked and server-authoritative (BIBLE UM-LAW-1): any player asks to start one of the
    /// game's registered <see cref="VoteKind"/>s (<see cref="StartVote"/>) and votes Yes or No (<see cref="CastVote"/>);
    /// the server decides through a Core <see cref="VoteBox"/>, runs a passing kind's effect, and every peer sees the
    /// question, the tally, the countdown and the result.
    /// </summary>
    /// <remarks>
    /// <para><b>Setting it up:</b> add a <see cref="NetVotes"/> component beside the scene's <see cref="NetBootstrap"/>, or
    /// in code call <see cref="RegisterPrefab"/> on every peer before the session starts and <see cref="Spawn"/> on the
    /// server once it runs. Then register kinds on the server with <see cref="AddKind"/> (and <see cref="AddKickKind"/>
    /// for the built-in kick).</para>
    /// <para><b>Server:</b> the roster is the session's connected clients when a vote starts; a client who disconnects is
    /// dropped from it and the quorum is recomputed. The time limit runs on the session's tick clock.</para>
    /// <para><b>Every peer:</b> <see cref="Status"/>, <see cref="LocalChoice"/> and <see cref="ChoiceOf"/>, announced by
    /// <see cref="VoteChanged"/> (at most once a frame, on the main thread); <see cref="VoteEnded"/> once per vote;
    /// <see cref="VoteRefused"/> on the peer whose start the server refused. With MindAttic Components installed and a
    /// <c>NetFrontEnd</c> in the scene, a ready-made overlay shows all of it (<c>VoteOverlay</c>, bound through the NGO-free
    /// <see cref="IVoteBoard"/> and <see cref="VoteBoards"/>).</para>
    /// </remarks>
    public partial class VoteBoard : NetEntity, IVoteBoard
    {
        /// <summary>Where the package's own VoteBoard prefab lives, for <c>Resources.Load</c>.</summary>
        public const string PrefabResourcePath = "MindAttic.Multiplayer/VoteBoard";

        /// <summary>The id of the built-in kick kind (<see cref="AddKickKind"/>).</summary>
        public const string KickKindId = "kick";

        /// <summary>Most vote starts a player may send a second.</summary>
        public const int StartRateLimit = 2;

        /// <summary>Most ballots a player may send a second (changing your mind is allowed, spamming isn't).</summary>
        public const int CastRateLimit = 4;

        private static readonly List<VoteBoard> s_Spawned = new List<VoteBoard>();

        // --- client -> server -----------------------------------------------------------------------------------

        [ToServer(RequireOwnership = false, RateLimit = StartRateLimit)]
        private partial void RequestVote(string kind, long number, string text);

        /// <summary>Votes Yes or No on the running vote; voting again replaces the earlier choice.</summary>
        [ToServer(RequireOwnership = false, RateLimit = CastRateLimit)]
        public partial void CastVote(bool yes);

        // --- server -> clients ----------------------------------------------------------------------------------

        [ToClients]
        private partial void Ballot(int sequence, ulong voter, int choice);

        [ToClients]
        private partial void Finished(int sequence, string kind, string title, long number, string text, ulong startedBy, int ending,
            int yes, int no, int voters, int quorum);

        [ToClient]
        private partial void Refused(ulong target, int refusal, string reason);

        // --- synced vote state (written by the server only) ------------------------------------------------------

#pragma warning disable CS0169, CS0414 // declarations the generator reads; the values live in the generated network variables
        [Synced(OnChanged = nameof(MarkBoolDirty))] private bool _syncOpen;
        [Synced(OnChanged = nameof(MarkIntDirty))] private int _syncSequence;
        [Synced(OnChanged = nameof(MarkKindDirty))] private FixedString64Bytes _syncKind;
        [Synced(OnChanged = nameof(MarkTitleDirty))] private FixedString128Bytes _syncTitle;
        [Synced(OnChanged = nameof(MarkDescriptionDirty))] private FixedString512Bytes _syncDescription;
        [Synced(OnChanged = nameof(MarkLongDirty))] private long _syncNumber;
        [Synced(OnChanged = nameof(MarkTitleDirty))] private FixedString128Bytes _syncText;
        [Synced(OnChanged = nameof(MarkUlongDirty))] private ulong _syncStarter;
        [Synced(OnChanged = nameof(MarkIntDirty))] private int _syncYes;
        [Synced(OnChanged = nameof(MarkIntDirty))] private int _syncNo;
        [Synced(OnChanged = nameof(MarkIntDirty))] private int _syncVoters;
        [Synced(OnChanged = nameof(MarkIntDirty))] private int _syncQuorum;
        [Synced(OnChanged = nameof(MarkIntDirty))] private int _syncSecondsLeft;
#pragma warning restore CS0169, CS0414

        // --- server state ---------------------------------------------------------------------------------------

        private readonly Dictionary<string, VoteKind> _kinds = new Dictionary<string, VoteKind>(StringComparer.Ordinal);
        private VoteRules _rules;
        private VoteBox _box;
        private NetSession _servedSession;
        private Func<IReadOnlyCollection<ulong>> _roster;

        /// <summary>The ballots last broadcast for vote <see cref="_sentSequence"/>, so only changes go out.</summary>
        private readonly Dictionary<ulong, VoteChoice> _sent = new Dictionary<ulong, VoteChoice>();
        private int _sentSequence;

        // --- every peer's view ----------------------------------------------------------------------------------

        private readonly Dictionary<ulong, VoteChoice> _ballots = new Dictionary<ulong, VoteChoice>();
        private int _ballotSequence;
        private bool _dirty;
        private VoteStatus _status;

        /// <summary>Raised on every peer when a VoteBoard has spawned there (the overlay binds here).</summary>
        public static event Action<VoteBoard> Spawned;

        /// <summary>Raised on every peer just before a VoteBoard despawns there.</summary>
        public static event Action<VoteBoard> Despawned;

        /// <summary>Raised on every peer when <see cref="Status"/> or a ballot changes, with the new status (null once no vote runs).</summary>
        public event Action<VoteStatus> VoteChanged;

        /// <summary>Raised on every peer once per vote when it ends, with how.</summary>
        public event Action<VoteResult> VoteEnded;

        /// <summary>Raised on the peer whose <see cref="StartVote"/> the server refused, with why and a sentence to show.</summary>
        public event Action<VoteRefusal, string> VoteRefused;

        /// <summary>The vote in progress as every peer sees it, or null when none runs.</summary>
        public VoteStatus Status => _status;

        /// <summary>True while a vote runs.</summary>
        public bool IsVoteOpen => _status != null;

        /// <summary>Where this peer's own player stands on the running vote (<see cref="VoteChoice.NotAVoter"/> when they joined after it started, or none runs).</summary>
        public VoteChoice LocalChoice => ChoiceOf(Session != null ? Session.LocalClientId : NetSender.ServerClientId);

        /// <summary>Server only: the vote rules once <see cref="Serve"/> has run; null elsewhere.</summary>
        public VoteBox Box => _box;

        /// <summary>
        /// The rules this board's votes follow. Set or change them on the server before <see cref="Serve"/> (or pass them
        /// to it); changes to a served board's rules apply from the next vote.
        /// </summary>
        public VoteRules Rules
        {
            get { return _box != null ? _box.Rules : (_rules ?? (_rules = new VoteRules())); }
        }

        /// <summary>Where player <paramref name="clientId"/> stands on the running vote.</summary>
        public VoteChoice ChoiceOf(ulong clientId)
        {
            if (_status == null)
            {
                return VoteChoice.NotAVoter;
            }

            if (_box != null)
            {
                return _box.ChoiceOf(clientId);
            }

            if (_ballotSequence != _status.Sequence)
            {
                return VoteChoice.NotAVoter;
            }

            VoteChoice choice;
            return _ballots.TryGetValue(clientId, out choice) ? choice : VoteChoice.NotAVoter;
        }

        // --- finding and spawning -----------------------------------------------------------------------------------

        /// <summary>Every VoteBoard spawned in this process (one per session; several in a test with many peers).</summary>
        public static IReadOnlyList<VoteBoard> All => s_Spawned;

        /// <summary>The VoteBoard spawned on <paramref name="session"/>'s peer, or null when there is none (yet).</summary>
        public static VoteBoard For(NetSession session)
        {
            foreach (VoteBoard board in s_Spawned)
            {
                if (board != null && board.Session == session)
                {
                    return board;
                }
            }

            return null;
        }

        /// <summary>The package's VoteBoard prefab (a NetworkObject with a VoteBoard), from its Resources folder.</summary>
        public static GameObject LoadPrefab() => Resources.Load<GameObject>(PrefabResourcePath);

        /// <summary>
        /// Registers the package's VoteBoard prefab with <paramref name="session"/>. Every peer (server and clients) calls it
        /// before its session starts; calling it again changes nothing. <see cref="NetVotes"/> does it for you.
        /// </summary>
        /// <returns>True when it was newly registered.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="session"/> is null.</exception>
        /// <exception cref="InvalidOperationException">The prefab is missing, or the session is already running.</exception>
        public static bool RegisterPrefab(NetSession session)
        {
            if (session == null)
            {
                throw new ArgumentNullException(nameof(session));
            }

            GameObject prefab = LoadPrefab();
            if (prefab == null)
            {
                throw new InvalidOperationException("The package's Resources/" + PrefabResourcePath + " prefab is missing; reinstall com.mindattic.unity.multiplayer.");
            }

            return !session.Prefabs.Contains(prefab) && session.Prefabs.Register(prefab);
        }

        /// <summary>
        /// Server: spawns the session's VoteBoard from the package's prefab (registered with <see cref="RegisterPrefab"/> on
        /// every peer) and serves it with <paramref name="rules"/>. Register the game's kinds on the result.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="session"/> is null.</exception>
        /// <exception cref="InvalidOperationException">The session is not a running server, or the prefab is not registered.</exception>
        public static VoteBoard Spawn(NetSession session, VoteRules rules = null)
        {
            if (session == null)
            {
                throw new ArgumentNullException(nameof(session));
            }

            if (!session.IsServer || session.Clock == null)
            {
                throw new InvalidOperationException("VoteBoard.Spawn runs on a running Host or Server.");
            }

            GameObject prefab = LoadPrefab();
            if (prefab == null || !session.Prefabs.Contains(prefab))
            {
                throw new InvalidOperationException("Call VoteBoard.RegisterPrefab(session) on every peer before the session starts (or add a NetVotes component).");
            }

            // Spawned through this session's own NetworkManager (a test process can run several).
            NetworkObject spawned = session.Manager.SpawnManager.InstantiateAndSpawn(prefab.GetComponent<NetworkObject>());
            spawned.gameObject.name = "VoteBoard";
            VoteBoard board = spawned.GetComponent<VoteBoard>();
            board.Serve(rules);
            return board;
        }

        // --- server API ---------------------------------------------------------------------------------------------

        /// <summary>
        /// Server only: runs this board's votes. The roster is <paramref name="roster"/>, by default the session's connected
        /// clients, and votes are timed on <paramref name="clock"/>, by default the session's. Kinds added before or after
        /// stay registered.
        /// </summary>
        /// <exception cref="InvalidOperationException">Not on the server, or no tick clock is available.</exception>
        public VoteBoard Serve(VoteRules rules = null, ITickClock clock = null, Func<IReadOnlyCollection<ulong>> roster = null)
        {
            if (!IsSpawned || !IsServer)
            {
                throw new InvalidOperationException("VoteBoard.Serve runs on the server, after the board has spawned.");
            }

            VoteRules use = rules ?? Rules;
            StopServing();

            _servedSession = Session;
            ITickClock ticks = clock ?? (_servedSession != null ? _servedSession.Clock : null);
            if (ticks == null)
            {
                throw new InvalidOperationException("VoteBoard.Serve needs a tick clock: the session has none yet, and none was given.");
            }

            _roster = roster ?? (() => _servedSession != null ? (IReadOnlyCollection<ulong>)_servedSession.ConnectedClientIds : Array.Empty<ulong>());
            _rules = use;
            _box = new VoteBox(ticks, use);
            foreach (VoteKind kind in _kinds.Values)
            {
                _box.Register(kind);
            }

            _box.Changed += OnBoxChanged;
            _box.Ended += OnBoxEnded;
            if (_servedSession != null)
            {
                _servedSession.ClientDisconnected += OnClientLeft;
            }

            return this;
        }

        /// <summary>Server: adds a kind of vote players may start (or replaces the one with the same id). Clients may call it too; it does nothing there.</summary>
        /// <exception cref="ArgumentNullException"><paramref name="kind"/> is null.</exception>
        public VoteBoard AddKind(VoteKind kind)
        {
            if (kind == null)
            {
                throw new ArgumentNullException(nameof(kind));
            }

            _kinds[kind.Id] = kind;
            _box?.Register(kind);
            return this;
        }

        /// <summary>Server: removes a kind. A running vote of that kind carries on.</summary>
        public bool RemoveKind(string id)
        {
            bool removed = id != null && _kinds.Remove(id);
            _box?.Unregister(id);
            return removed;
        }

        /// <summary>
        /// Server: adds the built-in kick vote (<see cref="KickKindId"/>, started with <see cref="StartKickVote"/>): "Kick
        /// Bob?", voted on by everyone but Bob; passing removes Bob through <see cref="NetSession.Kick"/> with
        /// <paramref name="reason"/>. The host's own player and the starter cannot be its target.
        /// </summary>
        public VoteBoard AddKickKind(string reason = null)
        {
            return AddKind(CreateKickKind(() => _servedSession ?? Session, reason));
        }

        /// <summary>The built-in kick kind for <paramref name="session"/>, for a game that runs its own <see cref="VoteBox"/>.</summary>
        public static VoteKind CreateKickKind(Func<NetSession> session, string reason = null)
        {
            if (session == null)
            {
                throw new ArgumentNullException(nameof(session));
            }

            string message = string.IsNullOrWhiteSpace(reason) ? "The other players voted to remove you." : reason;
            return new VoteKind(KickKindId, r => "Kick " + NameOf(session(), (ulong)r.Number) + "?")
            {
                Check = r =>
                {
                    NetSession s = session();
                    ulong target = (ulong)r.Number;
                    if (target == r.StartedBy) return "You can't start a vote to kick yourself.";
                    if (target == NetSender.ServerClientId) return "The host can't be kicked.";
                    if (s == null || !Contains(s.ConnectedClientIds, target)) return "That player isn't in the game.";
                    return null;
                },
                MayVote = (r, id) => id != (ulong)r.Number,
                Passed = v =>
                {
                    NetSession s = session();
                    if (s != null) s.Kick((ulong)v.Number, message);
                },
            };
        }

        /// <summary>Server only: stops running votes (an open vote is dropped without effect). Registered kinds are kept.</summary>
        public void StopServing()
        {
            if (_box == null)
            {
                return;
            }

            _box.Changed -= OnBoxChanged;
            _box.Ended -= OnBoxEnded;
            _box.Dispose();
            if (_servedSession != null)
            {
                _servedSession.ClientDisconnected -= OnClientLeft;
            }

            _box = null;
            _servedSession = null;
            _roster = null;
            if (IsSpawned && IsServer)
            {
                WriteStatus(null);
            }
        }

        /// <summary>Server only: calls off the running vote with no effect.</summary>
        /// <returns>False when none was running.</returns>
        public bool CancelVote() => _box != null && _box.Cancel();

        // --- any peer -----------------------------------------------------------------------------------------------

        /// <summary>
        /// Asks the server to start a <paramref name="kind"/> vote, with an optional number and text payload. Refused (see
        /// <see cref="VoteRefused"/>) while a vote runs, for an unknown kind, or when the kind's check says no.
        /// </summary>
        public void StartVote(string kind, long number = 0, string text = null)
        {
            RequestVote(kind ?? string.Empty, number, text ?? string.Empty);
        }

        /// <summary>Asks the server to start the built-in kick vote against <paramref name="clientId"/>.</summary>
        public void StartKickVote(ulong clientId)
        {
            StartVote(KickKindId, (long)clientId);
        }

        // --- lifecycle ----------------------------------------------------------------------------------------------

        /// <inheritdoc />
        protected override void OnSpawned()
        {
            if (!s_Spawned.Contains(this))
            {
                s_Spawned.Add(this);
            }

            _dirty = true;
            Spawned?.Invoke(this);
            VoteBoards.Add(this);
        }

        /// <inheritdoc />
        protected override void OnDespawned()
        {
            StopServing();
            s_Spawned.Remove(this);
            Despawned?.Invoke(this);
            VoteBoards.Remove(this);
        }

        private void OnDestroy()
        {
            StopServing();
            s_Spawned.Remove(this);
            VoteBoards.Remove(this);
        }

        private void Update()
        {
            if (!_dirty || !IsSpawned)
            {
                return;
            }

            _dirty = false;
            VoteStatus next = SyncOpen
                ? new VoteStatus(SyncSequence, SyncKind.ToString(), SyncTitle.ToString(), SyncDescription.ToString(), SyncNumber,
                    SyncText.ToString(), SyncStarter, SyncYes, SyncNo, SyncVoters, SyncQuorum, SyncSecondsLeft)
                : null;
            _status = next;
            VoteChanged?.Invoke(_status);
        }

        // --- server handlers ------------------------------------------------------------------------------------

        private void OnRequestVote(string kind, long number, string text, NetSender sender)
        {
            if (_box == null)
            {
                Debug.LogWarning("[MindAttic.Multiplayer] VoteBoard: a vote request arrived before VoteBoard.Serve; ignored.", this);
                return;
            }

            VoteStartResult result = _box.Start(new VoteRequest(kind, sender.ClientId, number, text), _roster());
            if (!result.Started)
            {
                Refused(sender.ClientId, (int)result.Refusal, result.Reason);
            }
        }

        private void OnCastVote(bool yes, NetSender sender)
        {
            _box?.Cast(sender.ClientId, yes);
        }

        private void OnClientLeft(ulong clientId)
        {
            _box?.RemoveVoter(clientId);
        }

        private void OnBoxChanged(VoteStatus status)
        {
            Vote vote = _box.Current;
            if (vote != null)
            {
                if (_sentSequence != vote.Sequence)
                {
                    _sent.Clear();
                    _sentSequence = vote.Sequence;
                }

                // Departed voters drop off every peer's view.
                var gone = new List<ulong>();
                foreach (ulong id in _sent.Keys)
                {
                    if (vote.ChoiceOf(id) == VoteChoice.NotAVoter)
                    {
                        gone.Add(id);
                    }
                }

                foreach (ulong id in gone)
                {
                    _sent.Remove(id);
                    Ballot(_sentSequence, id, (int)VoteChoice.NotAVoter);
                }

                foreach (ulong voter in vote.Voters)
                {
                    VoteChoice choice = vote.ChoiceOf(voter);
                    VoteChoice known;
                    if (_sent.TryGetValue(voter, out known) && known == choice)
                    {
                        continue;
                    }

                    _sent[voter] = choice;
                    Ballot(_sentSequence, voter, (int)choice);
                }
            }

            WriteStatus(status);
        }

        private void OnBoxEnded(VoteResult result)
        {
            Finished(result.Sequence, result.Kind, result.Title, result.Number, result.Text, result.StartedBy, (int)result.Ending,
                result.Yes, result.No, result.Voters, result.Quorum);
        }

        private void WriteStatus(VoteStatus status)
        {
            if (status != null)
            {
                SyncSequence = status.Sequence;
                SyncKind = Fixed64(status.Kind);
                SyncTitle = Fixed128(status.Title);
                SyncDescription = Fixed512(status.Description);
                SyncNumber = status.Number;
                SyncText = Fixed128(status.Text);
                SyncStarter = status.StartedBy;
                SyncYes = status.Yes;
                SyncNo = status.No;
                SyncVoters = status.Voters;
                SyncQuorum = status.Quorum;
                SyncSecondsLeft = status.SecondsLeft;
            }

            SyncOpen = status != null;
        }

        // --- every peer -----------------------------------------------------------------------------------------

        private void OnBallot(int sequence, ulong voter, int choice)
        {
            if (sequence != _ballotSequence)
            {
                if (sequence < _ballotSequence)
                {
                    return; // a late ballot from a vote already replaced
                }

                _ballots.Clear();
                _ballotSequence = sequence;
            }

            if ((VoteChoice)choice == VoteChoice.NotAVoter)
            {
                _ballots.Remove(voter);
            }
            else
            {
                _ballots[voter] = (VoteChoice)choice;
            }

            _dirty = true;
        }

        private void OnFinished(int sequence, string kind, string title, long number, string text, ulong startedBy, int ending,
            int yes, int no, int voters, int quorum)
        {
            VoteEnded?.Invoke(new VoteResult(sequence, kind, title, number, text, startedBy, (VoteEnding)ending, yes, no, voters, quorum));
        }

        private void OnRefused(int refusal, string reason)
        {
            VoteRefused?.Invoke((VoteRefusal)refusal, reason);
        }

        private void MarkBoolDirty(bool previous, bool current) => _dirty = true;

        private void MarkIntDirty(int previous, int current) => _dirty = true;

        private void MarkLongDirty(long previous, long current) => _dirty = true;

        private void MarkUlongDirty(ulong previous, ulong current) => _dirty = true;

        private void MarkKindDirty(FixedString64Bytes previous, FixedString64Bytes current) => _dirty = true;

        private void MarkTitleDirty(FixedString128Bytes previous, FixedString128Bytes current) => _dirty = true;

        private void MarkDescriptionDirty(FixedString512Bytes previous, FixedString512Bytes current) => _dirty = true;

        // --- helpers --------------------------------------------------------------------------------------------

        private static FixedString64Bytes Fixed64(string s)
        {
            var fixedString = new FixedString64Bytes();
            fixedString.CopyFromTruncated(s ?? string.Empty);
            return fixedString;
        }

        private static FixedString128Bytes Fixed128(string s)
        {
            var fixedString = new FixedString128Bytes();
            fixedString.CopyFromTruncated(s ?? string.Empty);
            return fixedString;
        }

        private static FixedString512Bytes Fixed512(string s)
        {
            var fixedString = new FixedString512Bytes();
            fixedString.CopyFromTruncated(s ?? string.Empty);
            return fixedString;
        }

        private static string NameOf(NetSession session, ulong clientId)
        {
            if (session != null)
            {
                NetSender sender = session.GetSender(clientId);
                if (!string.IsNullOrWhiteSpace(sender.DisplayName))
                {
                    return sender.DisplayName;
                }
            }

            return "player " + clientId;
        }

        private static bool Contains(IReadOnlyList<ulong> ids, ulong id)
        {
            for (int i = 0; i < ids.Count; i++)
            {
                if (ids[i] == id)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
