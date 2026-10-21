using System;
using System.Collections.Generic;

namespace Unity.Multiplayer.Core
{
    /// <summary>
    /// The players in a session, by seat (UM-T55): the engine-free model behind the adapter's <c>NetRoster</c>. The server
    /// builds the whole list from its seats with <see cref="Replace"/>, which raises the events and returns the
    /// <see cref="RosterUpdate"/> to send; each client <see cref="Apply"/>s the updates it receives.
    /// </summary>
    /// <remarks>Not thread-safe; the adapter uses it from Unity's main thread. Handlers run synchronously, in seat order.</remarks>
    public sealed class PlayerRoster
    {
        private readonly SortedDictionary<int, NetPlayer> _bySeat = new SortedDictionary<int, NetPlayer>();

        /// <summary>A player took a seat (or this peer learned of one already seated).</summary>
        public event Action<NetPlayer> Joined;

        /// <summary>A player's seat was freed (they left for good, were kicked, their reconnect window ran out) or the session ended.</summary>
        public event Action<NetPlayer> Left;

        /// <summary>Something about a seated player changed: name, colour, connection (dropped, reconnected), host, ping.</summary>
        public event Action<NetPlayerChange> Changed;

        /// <summary>Every player, by seat.</summary>
        public IReadOnlyList<NetPlayer> Players
        {
            get { return new List<NetPlayer>(_bySeat.Values); }
        }

        /// <summary>How many seats are taken (held seats of dropped players included).</summary>
        public int Count
        {
            get { return _bySeat.Count; }
        }

        /// <summary>This peer's own player; null on a dedicated server, before the roster arrives, or after the session ends.</summary>
        public NetPlayer Local
        {
            get
            {
                foreach (NetPlayer player in _bySeat.Values)
                {
                    if (player.IsLocal)
                    {
                        return player;
                    }
                }

                return null;
            }
        }

        /// <summary>The player on connection <paramref name="clientId"/> (a connected one first, else a held seat last on it).</summary>
        public bool TryGet(ulong clientId, out NetPlayer player)
        {
            NetPlayer held = null;
            foreach (NetPlayer candidate in _bySeat.Values)
            {
                if (candidate.ClientId != clientId)
                {
                    continue;
                }

                if (candidate.IsConnected)
                {
                    player = candidate;
                    return true;
                }

                held = held ?? candidate;
            }

            player = held;
            return held != null;
        }

        /// <summary>The player in seat <paramref name="seat"/>.</summary>
        public bool TryGetBySeat(int seat, out NetPlayer player)
        {
            return _bySeat.TryGetValue(seat, out player);
        }

        /// <summary>
        /// Makes the roster exactly <paramref name="players"/> (one per seat), raising <see cref="Left"/>, <see cref="Joined"/>
        /// and <see cref="Changed"/> for the differences.
        /// </summary>
        /// <returns>The differences, as an incremental update to send to peers that already have the old roster.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="players"/> is null.</exception>
        /// <exception cref="ArgumentException">Two players share a seat.</exception>
        public RosterUpdate Replace(IEnumerable<NetPlayer> players)
        {
            if (players == null)
            {
                throw new ArgumentNullException(nameof(players));
            }

            var next = new SortedDictionary<int, NetPlayer>();
            foreach (NetPlayer player in players)
            {
                if (player == null)
                {
                    continue;
                }

                if (next.ContainsKey(player.Seat))
                {
                    throw new ArgumentException("Two players in seat " + player.Seat + ".", nameof(players));
                }

                next[player.Seat] = player;
            }

            var removed = new List<int>();
            foreach (int seat in _bySeat.Keys)
            {
                if (!next.ContainsKey(seat))
                {
                    removed.Add(seat);
                }
            }

            var upserted = new List<NetPlayer>();
            foreach (NetPlayer player in next.Values)
            {
                NetPlayer old;
                if (!_bySeat.TryGetValue(player.Seat, out old) || !old.Equals(player))
                {
                    upserted.Add(player);
                }
            }

            var update = new RosterUpdate(false, upserted, removed);
            Apply(update);
            return update;
        }

        /// <summary>Applies an update a server sent: a full one replaces the roster, an incremental one adds, changes and removes.</summary>
        /// <exception cref="ArgumentNullException"><paramref name="update"/> is null.</exception>
        public void Apply(RosterUpdate update)
        {
            if (update == null)
            {
                throw new ArgumentNullException(nameof(update));
            }

            if (update.Full)
            {
                var removed = new List<int>();
                var keep = new HashSet<int>();
                foreach (NetPlayer player in update.Players)
                {
                    keep.Add(player.Seat);
                }

                foreach (int seat in _bySeat.Keys)
                {
                    if (!keep.Contains(seat))
                    {
                        removed.Add(seat);
                    }
                }

                ApplyParts(update.Players, removed);
                return;
            }

            ApplyParts(update.Players, update.Removed);
        }

        /// <summary>Adds or changes one player; true when anything changed.</summary>
        /// <exception cref="ArgumentNullException"><paramref name="player"/> is null.</exception>
        public bool Set(NetPlayer player)
        {
            if (player == null)
            {
                throw new ArgumentNullException(nameof(player));
            }

            NetPlayer old;
            if (_bySeat.TryGetValue(player.Seat, out old))
            {
                if (old.Equals(player))
                {
                    return false;
                }

                _bySeat[player.Seat] = player;
                Changed?.Invoke(new NetPlayerChange(old, player));
                return true;
            }

            _bySeat[player.Seat] = player;
            Joined?.Invoke(player);
            return true;
        }

        /// <summary>Removes the player in seat <paramref name="seat"/>; true when there was one.</summary>
        public bool Remove(int seat)
        {
            NetPlayer old;
            if (!_bySeat.TryGetValue(seat, out old))
            {
                return false;
            }

            _bySeat.Remove(seat);
            Left?.Invoke(old);
            return true;
        }

        /// <summary>Empties the roster (the session ended), raising <see cref="Left"/> for each player.</summary>
        public void Clear()
        {
            var seats = new List<int>(_bySeat.Keys);
            foreach (int seat in seats)
            {
                Remove(seat);
            }
        }

        private void ApplyParts(IReadOnlyList<NetPlayer> players, IReadOnlyList<int> removed)
        {
            for (int i = 0; i < removed.Count; i++)
            {
                Remove(removed[i]);
            }

            for (int i = 0; i < players.Count; i++)
            {
                if (players[i] != null)
                {
                    Set(players[i]);
                }
            }
        }
    }

    /// <summary>A change to a roster, as a server sends it: the players added or changed, and the seats freed.</summary>
    public sealed class RosterUpdate
    {
        private static readonly NetPlayer[] NoPlayers = new NetPlayer[0];
        private static readonly int[] NoSeats = new int[0];

        /// <summary>An update.</summary>
        /// <param name="full">True when <paramref name="players"/> is the whole roster (a peer drops every seat not in it).</param>
        /// <param name="players">The players added or changed (all of them when <paramref name="full"/>).</param>
        /// <param name="removed">The seats freed (ignored when <paramref name="full"/>).</param>
        public RosterUpdate(bool full, IReadOnlyList<NetPlayer> players, IReadOnlyList<int> removed)
        {
            Full = full;
            Players = players ?? NoPlayers;
            Removed = full ? NoSeats : (removed ?? NoSeats);
        }

        /// <summary>The whole roster, for a peer that has none yet (a new or returning connection).</summary>
        public static RosterUpdate Snapshot(IReadOnlyList<NetPlayer> players)
        {
            return new RosterUpdate(true, players, null);
        }

        /// <summary>True when <see cref="Players"/> is the whole roster.</summary>
        public bool Full { get; }

        /// <summary>The players added or changed (all of them when <see cref="Full"/>).</summary>
        public IReadOnlyList<NetPlayer> Players { get; }

        /// <summary>The seats freed.</summary>
        public IReadOnlyList<int> Removed { get; }

        /// <summary>True when an incremental update carries nothing.</summary>
        public bool IsEmpty
        {
            get { return !Full && Players.Count == 0 && Removed.Count == 0; }
        }

        /// <inheritdoc />
        public override string ToString()
        {
            return (Full ? "Snapshot of " : "Update: ") + Players.Count + " player(s)" + (Removed.Count > 0 ? ", " + Removed.Count + " removed" : string.Empty);
        }
    }

    /// <summary>A player's request to change their name and/or colour, which the server checks with <see cref="Validate"/>.</summary>
    public readonly struct PlayerChangeRequest
    {
        /// <summary>A request; null leaves that part as it is.</summary>
        public PlayerChangeRequest(string displayName, PlayerColor? color)
        {
            DisplayName = displayName;
            Color = color;
        }

        /// <summary>The new name, or null to keep the current one.</summary>
        public string DisplayName { get; }

        /// <summary>The new colour, or null to keep the current one.</summary>
        public PlayerColor? Color { get; }

        /// <summary>
        /// Why the server refuses this request, or null when it is acceptable: it must change something, the name must pass
        /// <see cref="PlayerNames.Validate"/> and the colour <see cref="PlayerColors.Validate"/>.
        /// </summary>
        public string Validate()
        {
            if (DisplayName == null && !Color.HasValue)
            {
                return "Nothing to change.";
            }

            string problem = DisplayName != null ? PlayerNames.Validate(DisplayName) : null;
            if (problem == null && Color.HasValue)
            {
                problem = PlayerColors.Validate(Color.Value);
            }

            return problem;
        }

        /// <inheritdoc />
        public override string ToString()
        {
            return "Change to " + (DisplayName != null ? "'" + DisplayName + "'" : "the same name") + (Color.HasValue ? " " + Color.Value : string.Empty);
        }
    }
}
