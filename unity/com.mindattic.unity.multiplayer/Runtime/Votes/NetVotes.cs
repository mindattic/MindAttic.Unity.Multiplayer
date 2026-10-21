using System;
using System.Collections.Generic;
using Unity.Multiplayer.Core;
using UnityEngine;

namespace Unity.Multiplayer
{
    /// <summary>
    /// Votes for a game in one component: put it beside the scene's <see cref="NetBootstrap"/>. On every peer it registers
    /// the package's <see cref="VoteBoard"/> prefab before the session starts; on the server it spawns the board once the
    /// session runs, serves it with the Inspector's rules and registers the game's kinds (<see cref="AddKind"/>, any time,
    /// before or after the board exists) and, when ticked, the built-in kick vote. Clients find the board through
    /// <see cref="Board"/> or <see cref="VoteBoard.For"/>.
    /// </summary>
    /// <example>
    /// <code>
    /// NetVotes votes = GetComponent&lt;NetVotes&gt;();
    /// votes.AddKind(new VoteKind("restart", "Restart the round?") { Passed = v =&gt; RestartRound() });
    /// // any peer, e.g. from a button:
    /// votes.Board.StartVote("restart");
    /// </code>
    /// </example>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-900)]
    [AddComponentMenu("MindAttic/Multiplayer/Net Votes")]
    public sealed class NetVotes : MonoBehaviour
    {
        [Tooltip("The NetBootstrap whose session runs the votes; empty finds the one on this GameObject, else the scene's.")]
        [SerializeField] private NetBootstrap bootstrap;

        [Tooltip("Fraction of the voters whose Yes passes a vote, rounded up (2/3: 2 of 3, 3 of 4).")]
        [SerializeField, Range(0.01f, 1f)] private float quorumFraction = 2f / 3f;

        [Tooltip("Seconds before an undecided vote lapses.")]
        [SerializeField, Min(1f)] private float timeoutSeconds = (float)VoteRules.DefaultTimeoutSeconds;

        [Tooltip("Seconds a player whose vote failed waits before starting another (0: no wait).")]
        [SerializeField, Min(0f)] private float cooldownSeconds;

        [Tooltip("Only the host may start votes (players still vote).")]
        [SerializeField] private bool hostOnly;

        [Tooltip("Offer the built-in kick vote (VoteBoard.StartKickVote).")]
        [SerializeField] private bool kickVotes = true;

        [Tooltip("What a player removed by a kick vote is told.")]
        [SerializeField] private string kickMessage = "The other players voted to remove you.";

        private readonly List<VoteKind> _kinds = new List<VoteKind>();
        private NetSession _session;
        private VoteBoard _served;

        /// <summary>The first enabled NetVotes, for game code that has no reference to it.</summary>
        public static NetVotes Instance { get; private set; }

        /// <summary>Raised on the server when its board has spawned and is served (register late kinds here if you like).</summary>
        public event Action<VoteBoard> Serving;

        /// <summary>This peer's VoteBoard: the served one on the server, the spawned copy on a client; null until it exists.</summary>
        public VoteBoard Board
        {
            get { return _served != null ? _served : VoteBoard.For(_session); }
        }

        /// <summary>The session the votes run on; null before Start.</summary>
        public NetSession Session
        {
            get { return _session; }
        }

        /// <summary>The rules the Inspector describes (a fresh copy each time).</summary>
        public VoteRules Rules
        {
            get
            {
                return new VoteRules
                {
                    QuorumFraction = Mathf.Clamp(quorumFraction, 0.01f, 1f),
                    TimeoutSeconds = Mathf.Max(1f, timeoutSeconds),
                    CooldownSeconds = Mathf.Max(0f, cooldownSeconds),
                    MayStart = hostOnly ? (Func<ulong, bool>)(id => id == NetSender.ServerClientId) : null,
                };
            }
        }

        /// <summary>Whether the built-in kick vote is offered. Set it before the session starts.</summary>
        public bool KickVotes
        {
            get { return kickVotes; }
            set { kickVotes = value; }
        }

        /// <summary>
        /// Adds a kind of vote (or replaces the one with the same id). Call it on any peer, any time; it only takes effect on
        /// the server, where it is registered on the board now or as soon as the board spawns.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="kind"/> is null.</exception>
        public NetVotes AddKind(VoteKind kind)
        {
            if (kind == null)
            {
                throw new ArgumentNullException(nameof(kind));
            }

            _kinds.RemoveAll(k => k.Id == kind.Id);
            _kinds.Add(kind);
            if (_served != null)
            {
                _served.AddKind(kind);
            }

            return this;
        }

        private void Awake()
        {
            if (Instance == null)
            {
                Instance = this;
            }
        }

        private void Start()
        {
            NetBootstrap found = bootstrap != null ? bootstrap : GetComponent<NetBootstrap>();
            if (found == null)
            {
                found = NetBootstrap.Instance;
            }

            if (found == null || found.Session == null)
            {
                Debug.LogError("[MindAttic.Multiplayer] NetVotes needs a NetBootstrap: run MindAttic > Multiplayer > Set Up Scene, or assign one.", this);
                enabled = false;
                return;
            }

            _session = found.Session;
            if (_session.IsActive)
            {
                Debug.LogWarning("[MindAttic.Multiplayer] NetVotes started after its session; votes need the prefab registered before the session starts.", this);
            }
            else
            {
                VoteBoard.RegisterPrefab(_session);
            }

            _session.Started += OnStarted;
            _session.Ended += OnEnded;
            if (_session.IsServer && _session.Clock != null)
            {
                OnStarted();
            }
        }

        private void OnDestroy()
        {
            if (_session != null)
            {
                _session.Started -= OnStarted;
                _session.Ended -= OnEnded;
            }

            if (Instance == this)
            {
                Instance = null;
            }
        }

        private void OnStarted()
        {
            if (_session == null || !_session.IsServer || _served != null)
            {
                return;
            }

            _served = VoteBoard.Spawn(_session, Rules);
            if (kickVotes)
            {
                _served.AddKickKind(kickMessage);
            }

            foreach (VoteKind kind in _kinds)
            {
                _served.AddKind(kind);
            }

            Serving?.Invoke(_served);
        }

        private void OnEnded(Disconnection reason)
        {
            _served = null;
        }
    }
}
