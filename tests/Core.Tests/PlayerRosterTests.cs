using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace Unity.Multiplayer.Core.Tests
{
    /// <summary>UM-T55: the rules for a player's name and colour, checked by the server on a rename.</summary>
    [TestFixture]
    public class PlayerNamesTests
    {
        [TestCase("Ann")]
        [TestCase("Ann Marie")]
        [TestCase("Zoë-Ann_42")]
        [TestCase("O'Brien (team red)!")]
        [TestCase("李小龍")]
        [TestCase("Ünïcödé ★")]
        [TestCase("😀 smile")]
        [TestCase("abcdefghijklmnopqrstuvwxyz012345")] // exactly 32
        public void AcceptableNames_PassValidation(string name)
        {
            Assert.That(PlayerNames.Validate(name), Is.Null);
            Assert.That(PlayerNames.IsValid(name), Is.True);
        }

        [TestCase(null, "empty")]
        [TestCase("", "empty")]
        [TestCase("   ", "empty")]
        [TestCase("abcdefghijklmnopqrstuvwxyz0123456", "at most 32")]
        [TestCase(" Ann", "start or end with a space")]
        [TestCase("Ann ", "start or end with a space")]
        [TestCase("Ann  Lee", "two spaces")]
        [TestCase("Ann\tLee", "plain spaces")]
        [TestCase("Ann Lee", "plain spaces")]
        [TestCase("Ann\nLee", "plain spaces")]
        [TestCase("Ann\u0007", "unprintable")]
        [TestCase("Ann​Lee", "invisible")]
        [TestCase("‮nnA", "invisible")]
        [TestCase("Ann", "invisible")]
        public void BadNames_AreRefusedWithAReason(string name, string reason)
        {
            Assert.That(PlayerNames.Validate(name), Does.Contain(reason));
        }

        [Test]
        public void BrokenSurrogates_AreRefused()
        {
            // Built in code: an attribute argument would have the lone surrogate replaced on the way in.
            Assert.That(PlayerNames.Validate("Ann" + (char)0xD83D), Does.Contain("broken"));
            Assert.That(PlayerNames.Validate("Ann" + (char)0xDE00 + "x"), Does.Contain("broken"));
            Assert.That(PlayerNames.Clean("Ann" + (char)0xD83D), Is.EqualTo("Ann"));
        }

        [TestCase("  Ann   Lee  ", "Ann Lee")]
        [TestCase("Ann​‮ Lee", "Ann Lee")]
        [TestCase("\t\n", "Player")]
        [TestCase(null, "Player")]
        [TestCase("abcdefghijklmnopqrstuvwxyz0123456789", "abcdefghijklmnopqrstuvwxyz012345")]
        public void Clean_MakesAnyNameAcceptable(string name, string expected)
        {
            string clean = PlayerNames.Clean(name);
            Assert.That(clean, Is.EqualTo(expected));
            Assert.That(PlayerNames.Validate(clean), Is.Null);
        }

        [Test]
        public void Clean_NeverSplitsASurrogatePair()
        {
            string name = new string('a', 31) + "😀";
            Assert.That(PlayerNames.Clean(name), Is.EqualTo(new string('a', 31)));
        }
    }

    [TestFixture]
    public class PlayerColorsTests
    {
        [Test]
        public void TheDefaultWhite_IsUnset_AndResolvesToTheSeatsPaletteColour()
        {
            Assert.That(PlayerColors.IsUnset(PlayerColor.White), Is.True);
            Assert.That(PlayerColors.IsUnset(new PlayerColor(10, 20, 30, 0)), Is.True);
            Assert.That(PlayerColors.IsUnset(new PlayerColor(10, 20, 30)), Is.False);
            Assert.That(PlayerColors.Resolve(PlayerColor.White, 1), Is.EqualTo(PlayerColors.Palette[1]));
            Assert.That(PlayerColors.Resolve(PlayerColor.White, 9), Is.EqualTo(PlayerColors.Palette[1]), "wraps after eight");
            Assert.That(PlayerColors.Resolve(new PlayerColor(10, 20, 30, 128), 3), Is.EqualTo(new PlayerColor(10, 20, 30)), "a chosen colour, made opaque");
        }

        [Test]
        public void NeighbouringSeats_GetDifferentColours()
        {
            Assert.That(Enumerable.Range(0, 8).Select(PlayerColors.ForSeat).Distinct().Count(), Is.EqualTo(8));
            Assert.That(PlayerColors.PaletteNames.Count, Is.EqualTo(PlayerColors.Palette.Count));
        }

        [TestCase("#3498DB", 0x34, 0x98, 0xDB, 0xFF)]
        [TestCase("3498db", 0x34, 0x98, 0xDB, 0xFF)]
        [TestCase(" #3498DB80 ", 0x34, 0x98, 0xDB, 0x80)]
        public void TryParse_ReadsHex(string text, int r, int g, int b, int a)
        {
            Assert.That(PlayerColors.TryParse(text, out PlayerColor color), Is.True);
            Assert.That(color, Is.EqualTo(new PlayerColor((byte)r, (byte)g, (byte)b, (byte)a)));
            Assert.That(PlayerColors.ToHex(color), Is.EqualTo("#3498DB"));
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("#12345")]
        [TestCase("#GG0000")]
        [TestCase("red")]
        [TestCase("#1234567")]
        public void TryParse_RefusesAnythingElse(string text)
        {
            Assert.That(PlayerColors.TryParse(text, out _), Is.False);
        }

        [Test]
        public void Validate_WantsAnOpaqueColour()
        {
            Assert.That(PlayerColors.Validate(new PlayerColor(1, 2, 3)), Is.Null);
            Assert.That(PlayerColors.Validate(new PlayerColor(1, 2, 3, 200)), Does.Contain("opaque"));
        }

        [Test]
        public void NearestPaletteIndex_FindsTheClosest()
        {
            Assert.That(PlayerColors.NearestPaletteIndex(new PlayerColor(0x30, 0x90, 0xE0)), Is.EqualTo(1));
            Assert.That(PlayerColors.NearestPaletteIndex(PlayerColors.Palette[6]), Is.EqualTo(6));
        }

        [Test]
        public void ChangeRequest_ValidatesNameAndColour()
        {
            Assert.That(new PlayerChangeRequest("Bobby", null).Validate(), Is.Null);
            Assert.That(new PlayerChangeRequest(null, new PlayerColor(1, 2, 3)).Validate(), Is.Null);
            Assert.That(new PlayerChangeRequest(null, null).Validate(), Does.Contain("Nothing"));
            Assert.That(new PlayerChangeRequest("Bo​b", null).Validate(), Does.Contain("invisible"));
            Assert.That(new PlayerChangeRequest("Bob", new PlayerColor(1, 2, 3, 0)).Validate(), Does.Contain("opaque"));
        }
    }

    [TestFixture]
    public class PlayerRosterTests
    {
        private static readonly PlayerColor Red = PlayerColors.Palette[0];
        private static readonly PlayerColor Blue = PlayerColors.Palette[1];

        private PlayerRoster _roster;
        private List<string> _events;

        [SetUp]
        public void SetUp()
        {
            _roster = new PlayerRoster();
            _events = new List<string>();
            _roster.Joined += p => _events.Add("joined " + p.DisplayName);
            _roster.Left += p => _events.Add("left " + p.DisplayName);
            _roster.Changed += c => _events.Add("changed " + c.Previous.DisplayName + "->" + c.Player.DisplayName);
        }

        private static NetPlayer Player(int seat, ulong client, string name, PlayerColor color, bool connected = true, int ping = 20, bool local = false)
        {
            return new NetPlayer(seat, client, name, color, client == 0, connected, ping, local);
        }

        [Test]
        public void Replace_RaisesJoinedLeftAndChanged_AndReturnsTheDifference()
        {
            _roster.Replace(new[] { Player(0, 0, "Ann", Red, local: true), Player(1, 1, "Bob", Blue) });
            Assert.That(_events, Is.EqualTo(new[] { "joined Ann", "joined Bob" }));
            Assert.That(_roster.Local.DisplayName, Is.EqualTo("Ann"));

            _events.Clear();
            RosterUpdate update = _roster.Replace(new[] { Player(0, 0, "Ann", Red, local: true), Player(2, 2, "Cat", Red) });
            Assert.That(_events, Is.EqualTo(new[] { "left Bob", "joined Cat" }));
            Assert.That(update.Full, Is.False);
            Assert.That(update.Players.Select(p => p.DisplayName), Is.EqualTo(new[] { "Cat" }), "Ann did not change, so she is not sent");
            Assert.That(update.Removed, Is.EqualTo(new[] { 1 }));

            _events.Clear();
            update = _roster.Replace(new[] { Player(0, 0, "Ann", Red, local: true), Player(2, 2, "Cathy", Red) });
            Assert.That(_events, Is.EqualTo(new[] { "changed Cat->Cathy" }));
            Assert.That(update.Players.Single().DisplayName, Is.EqualTo("Cathy"));

            _events.Clear();
            Assert.That(_roster.Replace(_roster.Players).IsEmpty, Is.True);
            Assert.That(_events, Is.Empty, "nothing changed, nothing raised");
        }

        [Test]
        public void Apply_ASnapshotDropsSeatsItDoesNotList_AndAnUpdateOnlyTouchesWhatItCarries()
        {
            _roster.Apply(RosterUpdate.Snapshot(new[] { Player(0, 0, "Ann", Red), Player(1, 5, "Bob", Blue) }));
            _roster.Apply(new RosterUpdate(false, new[] { Player(1, 5, "Bob", Blue, connected: false, ping: -1) }, null));
            Assert.That(_roster.TryGetBySeat(1, out NetPlayer bob) && !bob.IsConnected, Is.True, "Bob dropped; his seat is held");
            Assert.That(_roster.TryGet(5, out NetPlayer byClient) && byClient == bob, Is.True, "a held seat is still found by its last client id");

            _roster.Apply(new RosterUpdate(false, new[] { Player(1, 7, "Bob", Blue) }, null));
            Assert.That(_roster.TryGet(7, out bob) && bob.IsConnected && bob.Seat == 1, Is.True, "back on a new connection, same seat");
            Assert.That(_roster.TryGet(5, out _), Is.False);

            _events.Clear();
            _roster.Apply(RosterUpdate.Snapshot(new[] { Player(1, 7, "Bob", Blue) }));
            Assert.That(_events, Is.EqualTo(new[] { "left Ann" }));
            _roster.Clear();
            Assert.That(_roster.Count, Is.Zero);
            Assert.That(_events.Last(), Is.EqualTo("left Bob"));
        }

        [Test]
        public void Change_SaysWhatChanged()
        {
            NetPlayer before = Player(1, 5, "Bob", Blue);
            Assert.That(new NetPlayerChange(before, Player(1, 5, "Bob", Blue, ping: 99)).OnlyPingChanged, Is.True);
            var renamed = new NetPlayerChange(before, before.WithIdentity("Bobby", Red));
            Assert.That(renamed.NameChanged && renamed.ColorChanged && !renamed.OnlyPingChanged, Is.True);
            Assert.That(new NetPlayerChange(before, Player(1, 6, "Bob", Blue)).ConnectionChanged, Is.True);
        }

        [Test]
        public void FromSeat_CleansTheNameResolvesTheColourAndMarksTheHost()
        {
            var seats = new SeatRegistry(4, 30);
            seats.Claim(0, new ConnectionPayload("1", ProtocolHash.None, "host", "Ann", PlayerColor.White), 0);
            seats.Claim(3, new ConnectionPayload("1", ProtocolHash.None, "bob", "Bob​", new PlayerColor(1, 2, 3)), 0);

            NetPlayer ann = NetPlayer.FromSeat(seats.Seats[0], true, 55, 0);
            Assert.That(ann.IsHost && ann.IsLocal && ann.IsConnected, Is.True);
            Assert.That(ann.PingMs, Is.Zero, "the host's own round trip is zero");
            Assert.That(ann.Color, Is.EqualTo(PlayerColors.ForSeat(0)), "never picked: the seat's colour");

            NetPlayer bob = NetPlayer.FromSeat(seats.Seats[1], true, 42, 0);
            Assert.That(bob.DisplayName, Is.EqualTo("Bob"), "an invisible character from the payload is dropped");
            Assert.That(bob.Color, Is.EqualTo(new PlayerColor(1, 2, 3)));
            Assert.That(bob.IsHost || bob.IsLocal, Is.False);
            Assert.That(bob.PingMs, Is.EqualTo(42));

            seats.Release(3, 1);
            NetPlayer held = NetPlayer.FromSeat(seats.Seats[1], true, 42, 0);
            Assert.That(held.IsConnected, Is.False);
            Assert.That(held.PingMs, Is.EqualTo(NetPlayer.UnknownPing));

            Assert.That(NetPlayer.FromSeat(seats.Seats[0], false, 9, null).IsHost, Is.False, "a dedicated server has no host player");
        }

        [Test]
        public void SeatRegistry_Rename_KeepsTheNewNameForAReconnect()
        {
            var seats = new SeatRegistry(4, 30);
            seats.Claim(1, new ConnectionPayload("1", ProtocolHash.None, "bob", "Bob", Blue), 0);
            Seat renamed = seats.Rename(1, "Bobby", Red);
            Assert.That(renamed.DisplayName, Is.EqualTo("Bobby"));
            Assert.That(renamed.Color, Is.EqualTo(Red));
            Assert.That(seats.Rename(99, "X", Red), Is.Null);

            seats.Release(1, 1);
            Assert.That(seats.TryGetByToken("bob", out Seat held) && held.DisplayName == "Bobby", Is.True);
        }
    }

    [TestFixture]
    public class RosterWireTests
    {
        [Test]
        public void AnUpdate_RoundTrips()
        {
            var players = new[]
            {
                new NetPlayer(0, 0, "Ann", PlayerColors.Palette[0], true, true, 0),
                new NetPlayer(3, 77, "李小龍 😀", new PlayerColor(1, 2, 3), false, false, NetPlayer.UnknownPing),
            };
            var update = new RosterUpdate(false, players, new[] { 2, 5 });

            Assert.That(RosterWire.TryDecodeUpdate(RosterWire.EncodeUpdate(update), out RosterUpdate back), Is.True);
            Assert.That(back.Full, Is.False);
            Assert.That(back.Players, Is.EqualTo(players));
            Assert.That(back.Removed, Is.EqualTo(new[] { 2, 5 }));

            Assert.That(RosterWire.TryDecodeUpdate(RosterWire.EncodeUpdate(RosterUpdate.Snapshot(players)), out back), Is.True);
            Assert.That(back.Full, Is.True);
            Assert.That(back.Players, Is.EqualTo(players));
        }

        [Test]
        public void LocalNeverTravels()
        {
            var me = new NetPlayer(1, 4, "Me", PlayerColors.Palette[1], false, true, 12, isLocal: true);
            RosterWire.TryDecodeUpdate(RosterWire.EncodeUpdate(RosterUpdate.Snapshot(new[] { me })), out RosterUpdate back);
            Assert.That(back.Players[0].IsLocal, Is.False);
            Assert.That(back.Players[0], Is.EqualTo(me.WithLocal(false)));
        }

        [Test]
        public void ARequestAndItsReply_RoundTrip()
        {
            byte[] bytes = RosterWire.EncodeChangeRequest(42, new PlayerChangeRequest("Bobby", new PlayerColor(9, 8, 7)));
            Assert.That(RosterWire.TryDecodeChangeRequest(bytes, out uint id, out PlayerChangeRequest request), Is.True);
            Assert.That(id, Is.EqualTo(42));
            Assert.That(request.DisplayName, Is.EqualTo("Bobby"));
            Assert.That(request.Color, Is.EqualTo(new PlayerColor(9, 8, 7)));

            RosterWire.TryDecodeChangeRequest(RosterWire.EncodeChangeRequest(1, new PlayerChangeRequest(null, null)), out _, out request);
            Assert.That(request.DisplayName, Is.Null);
            Assert.That(request.Color.HasValue, Is.False);

            Assert.That(RosterWire.TryDecodeChangeReply(RosterWire.EncodeChangeReply(42, false, "A name cannot be empty."), out id, out bool accepted, out string reason), Is.True);
            Assert.That(id, Is.EqualTo(42));
            Assert.That(accepted, Is.False);
            Assert.That(reason, Is.EqualTo("A name cannot be empty."));
            RosterWire.TryDecodeChangeReply(RosterWire.EncodeChangeReply(7, true, "ignored"), out _, out accepted, out reason);
            Assert.That(accepted && reason.Length == 0, Is.True);
        }

        [Test]
        public void MalformedBytes_AreRefusedWithoutThrowing()
        {
            byte[] good = RosterWire.EncodeUpdate(RosterUpdate.Snapshot(new[] { new NetPlayer(0, 0, "Ann", PlayerColor.White, true, true, 0) }));
            Assert.That(RosterWire.TryDecodeUpdate(null, out _), Is.False);
            Assert.That(RosterWire.TryDecodeUpdate(new byte[0], out _), Is.False);
            Assert.That(RosterWire.TryDecodeUpdate(new byte[] { 9, 0, 0, 0 }, out _), Is.False, "another version");
            for (int cut = 1; cut < good.Length; cut++)
            {
                Assert.That(RosterWire.TryDecodeUpdate(good.Take(cut).ToArray(), out _), Is.False, "truncated at " + cut);
            }

            Assert.That(RosterWire.TryDecodeUpdate(good.Concat(new byte[] { 1 }).ToArray(), out _), Is.False, "trailing bytes");
            byte[] twoInOneSeat = RosterWire.EncodeUpdate(new RosterUpdate(false, new[]
            {
                new NetPlayer(1, 1, "A", PlayerColor.White, false, true, 0),
                new NetPlayer(1, 2, "B", PlayerColor.White, false, true, 0),
            }, null));
            Assert.That(RosterWire.TryDecodeUpdate(twoInOneSeat, out _), Is.False, "two players in one seat");
            Assert.That(RosterWire.TryDecodeChangeRequest(new byte[] { RosterWire.Version, 1, 0 }, out _, out _), Is.False);
            Assert.That(RosterWire.TryDecodeChangeReply(new byte[] { RosterWire.Version }, out _, out _, out _), Is.False);
        }
    }
}
