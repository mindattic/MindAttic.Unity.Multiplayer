using System;
using System.Collections.Generic;
using MindAttic.Unity.Multiplayer.Core;
using NUnit.Framework;

namespace MindAttic.Unity.Multiplayer.Core.Tests
{
    [TestFixture]
    public class SessionStateMachineTests
    {
        private SessionStateMachine _machine;
        private List<SessionStateChange> _changes;

        [SetUp]
        public void SetUp()
        {
            _machine = new SessionStateMachine();
            _changes = new List<SessionStateChange>();
            _machine.StateChanged += c => _changes.Add(c);
        }

        private IEnumerable<string> Path()
        {
            foreach (var change in _changes)
            {
                yield return change.From + "->" + change.To;
            }
        }

        [Test]
        public void StartsIdleWithNoTopologyAndNoDisconnect()
        {
            Assert.That(_machine.State, Is.EqualTo(SessionState.Idle));
            Assert.That(_machine.Topology, Is.Null);
            Assert.That(_machine.LastDisconnect, Is.EqualTo(Disconnection.None));
            Assert.That(_machine.IsActive, Is.False);
            Assert.That(_machine.IsLive, Is.False);
        }

        [TestCase(Topology.Host)]
        [TestCase(Topology.Server)]
        public void ServerLifecycle_IdleStartingRunningStoppingIdle(Topology topology)
        {
            _machine.Start(topology);
            Assert.That(_machine.State, Is.EqualTo(SessionState.Starting));
            Assert.That(_machine.IsActive, Is.True);
            Assert.That(_machine.IsLive, Is.False);

            Assert.That(_machine.NotifyStarted(), Is.True);
            Assert.That(_machine.State, Is.EqualTo(SessionState.Running));
            Assert.That(_machine.IsLive, Is.True);

            Assert.That(_machine.Stop(), Is.True);
            Assert.That(_machine.State, Is.EqualTo(SessionState.Stopping));
            Assert.That(_machine.IsActive, Is.True);

            Assert.That(_machine.NotifyStopped(), Is.True);
            Assert.That(_machine.State, Is.EqualTo(SessionState.Idle));
            Assert.That(_machine.LastDisconnect.Reason, Is.EqualTo(DisconnectReason.LocalLeave));
            Assert.That(_machine.Topology, Is.EqualTo(topology));

            Assert.That(Path(), Is.EqualTo(new[] { "Idle->Starting", "Starting->Running", "Running->Stopping", "Stopping->Idle" }));
            Assert.That(_changes[3].Disconnection.Reason, Is.EqualTo(DisconnectReason.LocalLeave));
        }

        [Test]
        public void ClientLifecycle_IdleConnectingConnectedDisconnected()
        {
            _machine.Start(Topology.Client);
            Assert.That(_machine.State, Is.EqualTo(SessionState.Connecting));

            Assert.That(_machine.NotifyConnected(), Is.True);
            Assert.That(_machine.State, Is.EqualTo(SessionState.Connected));
            Assert.That(_machine.IsLive, Is.True);

            Assert.That(_machine.Stop(), Is.True);
            Assert.That(_machine.State, Is.EqualTo(SessionState.Disconnected));
            Assert.That(_machine.IsActive, Is.False);
            Assert.That(_machine.LastDisconnect.Reason, Is.EqualTo(DisconnectReason.LocalLeave));

            Assert.That(Path(), Is.EqualTo(new[] { "Idle->Connecting", "Connecting->Connected", "Connected->Disconnected" }));
        }

        [TestCase(DisconnectReason.ServerShutdown)]
        [TestCase(DisconnectReason.TimedOut)]
        [TestCase(DisconnectReason.Kicked)]
        [TestCase(DisconnectReason.TransportFailure)]
        public void Client_DroppedWhileConnected_RecordsReasonAndMessage(DisconnectReason reason)
        {
            _machine.Start(Topology.Client);
            _machine.NotifyConnected();

            Assert.That(_machine.NotifyDisconnected(reason, "detail"), Is.True);

            Assert.That(_machine.State, Is.EqualTo(SessionState.Disconnected));
            Assert.That(_machine.LastDisconnect, Is.EqualTo(new Disconnection(reason, "detail")));
            Assert.That(_changes[_changes.Count - 1].Disconnection.Reason, Is.EqualTo(reason));
        }

        [Test]
        public void Client_RejectedWhileConnecting_GoesDisconnectedWithReason()
        {
            _machine.Start(Topology.Client);

            _machine.NotifyDisconnected(DisconnectReason.Rejected, "Game version 1.1 does not match server 1.2.");

            Assert.That(_machine.State, Is.EqualTo(SessionState.Disconnected));
            Assert.That(_machine.LastDisconnect.Reason, Is.EqualTo(DisconnectReason.Rejected));
            Assert.That(_machine.LastDisconnect.Message, Does.Contain("1.2"));
            Assert.That(_machine.LastDisconnect.ToString(), Does.StartWith("Rejected: "));
        }

        [Test]
        public void Client_CanStartAgainFromDisconnected_ClearingLastDisconnect()
        {
            _machine.Start(Topology.Client);
            _machine.NotifyDisconnected(DisconnectReason.ConnectFailed);

            _machine.Start(Topology.Client);

            Assert.That(_machine.State, Is.EqualTo(SessionState.Connecting));
            Assert.That(_machine.LastDisconnect, Is.EqualTo(Disconnection.None));
        }

        [Test]
        public void Reset_MovesDisconnectedToIdleAndKeepsReason()
        {
            _machine.Start(Topology.Client);
            _machine.NotifyDisconnected(DisconnectReason.TimedOut);

            Assert.That(_machine.Reset(), Is.True);

            Assert.That(_machine.State, Is.EqualTo(SessionState.Idle));
            Assert.That(_machine.LastDisconnect.Reason, Is.EqualTo(DisconnectReason.TimedOut));
            Assert.That(_machine.Reset(), Is.False);
        }

        [Test]
        public void Server_StartFailure_ReturnsToIdleWithReason()
        {
            _machine.Start(Topology.Server);

            Assert.That(_machine.NotifyDisconnected(DisconnectReason.TransportFailure, "port 7777 in use"), Is.True);

            Assert.That(_machine.State, Is.EqualTo(SessionState.Idle));
            Assert.That(_machine.LastDisconnect.Reason, Is.EqualTo(DisconnectReason.TransportFailure));
            Assert.That(_machine.LastDisconnect.Message, Is.EqualTo("port 7777 in use"));
        }

        [Test]
        public void Host_TransportLostWhileRunning_ReturnsToIdleWithReason()
        {
            _machine.Start(Topology.Host);
            _machine.NotifyStarted();

            _machine.NotifyDisconnected(DisconnectReason.TransportFailure);

            Assert.That(_machine.State, Is.EqualTo(SessionState.Idle));
            Assert.That(_machine.LastDisconnect.Reason, Is.EqualTo(DisconnectReason.TransportFailure));
        }

        [Test]
        public void Server_DisconnectWhileStopping_CompletesLocalStop()
        {
            _machine.Start(Topology.Server);
            _machine.NotifyStarted();
            _machine.Stop();

            Assert.That(_machine.NotifyDisconnected(DisconnectReason.ServerShutdown), Is.True);

            Assert.That(_machine.State, Is.EqualTo(SessionState.Idle));
            Assert.That(_machine.LastDisconnect.Reason, Is.EqualTo(DisconnectReason.LocalLeave));
        }

        [Test]
        public void Server_StopWhileStarting_GoesStopping()
        {
            _machine.Start(Topology.Host);
            Assert.That(_machine.Stop(), Is.True);
            Assert.That(_machine.State, Is.EqualTo(SessionState.Stopping));
            Assert.That(_machine.NotifyStarted(), Is.False, "a late 'started' must not revive a stopping session");
            Assert.That(_machine.State, Is.EqualTo(SessionState.Stopping));
        }

        [Test]
        public void Server_CanRestartAfterStopping()
        {
            _machine.Start(Topology.Server);
            _machine.NotifyStarted();
            _machine.Stop();
            _machine.NotifyStopped();

            _machine.Start(Topology.Host);

            Assert.That(_machine.State, Is.EqualTo(SessionState.Starting));
            Assert.That(_machine.Topology, Is.EqualTo(Topology.Host));
            Assert.That(_machine.LastDisconnect, Is.EqualTo(Disconnection.None));
        }

        [TestCase(Topology.Host)]
        [TestCase(Topology.Client)]
        public void Start_WhileActive_Throws(Topology second)
        {
            _machine.Start(Topology.Server);
            var ex = Assert.Throws<InvalidOperationException>(() => _machine.Start(second));
            Assert.That(ex.Message, Does.Contain("Starting"));
            Assert.That(_machine.State, Is.EqualTo(SessionState.Starting));
        }

        [Test]
        public void Start_WhileStopping_Throws()
        {
            _machine.Start(Topology.Server);
            _machine.Stop();
            Assert.Throws<InvalidOperationException>(() => _machine.Start(Topology.Server));
        }

        [Test]
        public void Start_UndefinedTopology_Throws()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => _machine.Start((Topology)99));
            Assert.That(_machine.State, Is.EqualTo(SessionState.Idle));
        }

        [Test]
        public void NotifyDisconnected_WithNoneReason_Throws()
        {
            _machine.Start(Topology.Client);
            Assert.Throws<ArgumentException>(() => _machine.NotifyDisconnected(DisconnectReason.None));
            Assert.That(_machine.State, Is.EqualTo(SessionState.Connecting));
        }

        [Test]
        public void LateAndDuplicateNotifications_AreIgnored()
        {
            Assert.That(_machine.NotifyStarted(), Is.False);
            Assert.That(_machine.NotifyConnected(), Is.False);
            Assert.That(_machine.NotifyStopped(), Is.False);
            Assert.That(_machine.NotifyDisconnected(DisconnectReason.TimedOut), Is.False);
            Assert.That(_machine.Stop(), Is.False);

            _machine.Start(Topology.Client);
            Assert.That(_machine.NotifyStarted(), Is.False, "a client never enters Running");
            _machine.NotifyConnected();
            Assert.That(_machine.NotifyConnected(), Is.False);
            _machine.NotifyDisconnected(DisconnectReason.ServerShutdown);
            Assert.That(_machine.NotifyDisconnected(DisconnectReason.TimedOut), Is.False);
            Assert.That(_machine.LastDisconnect.Reason, Is.EqualTo(DisconnectReason.ServerShutdown), "the first reason wins");
            Assert.That(_machine.Stop(), Is.False);

            Assert.That(_changes.Count, Is.EqualTo(3));
        }

        [Test]
        public void StateChanged_SeesNewStateInsideHandler()
        {
            var seen = new List<SessionState>();
            _machine.StateChanged += c => seen.Add(_machine.State);

            _machine.Start(Topology.Client);
            _machine.NotifyConnected();

            Assert.That(seen, Is.EqualTo(new[] { SessionState.Connecting, SessionState.Connected }));
        }

        [Test]
        public void IsAllowed_MatchesTheDocumentedGraph()
        {
            var allowed = new HashSet<(SessionState, SessionState)>
            {
                (SessionState.Idle, SessionState.Starting),
                (SessionState.Idle, SessionState.Connecting),
                (SessionState.Starting, SessionState.Running),
                (SessionState.Starting, SessionState.Stopping),
                (SessionState.Starting, SessionState.Idle),
                (SessionState.Running, SessionState.Stopping),
                (SessionState.Running, SessionState.Idle),
                (SessionState.Stopping, SessionState.Idle),
                (SessionState.Connecting, SessionState.Connected),
                (SessionState.Connecting, SessionState.Disconnected),
                (SessionState.Connected, SessionState.Disconnected),
                (SessionState.Disconnected, SessionState.Connecting),
                (SessionState.Disconnected, SessionState.Starting),
                (SessionState.Disconnected, SessionState.Idle),
            };

            foreach (SessionState from in Enum.GetValues(typeof(SessionState)))
            {
                foreach (SessionState to in Enum.GetValues(typeof(SessionState)))
                {
                    Assert.That(SessionStateMachine.IsAllowed(from, to), Is.EqualTo(allowed.Contains((from, to))), from + " -> " + to);
                }
            }
        }

        [Test]
        public void Disconnection_EqualityAndNullMessage()
        {
            var a = new Disconnection(DisconnectReason.Kicked, null);
            var b = new Disconnection(DisconnectReason.Kicked, "");
            Assert.That(a.Message, Is.Empty);
            Assert.That(a, Is.EqualTo(b));
            Assert.That(a.GetHashCode(), Is.EqualTo(b.GetHashCode()));
            Assert.That(a, Is.Not.EqualTo(new Disconnection(DisconnectReason.Kicked, "spam")));
            Assert.That(default(Disconnection), Is.EqualTo(Disconnection.None));
            Assert.That(a.ToString(), Is.EqualTo("Kicked"));
        }

        [Test]
        public void SessionStateChange_ToStringShowsReason()
        {
            Assert.That(new SessionStateChange(SessionState.Idle, SessionState.Connecting, Disconnection.None).ToString(), Is.EqualTo("Idle -> Connecting"));
            Assert.That(new SessionStateChange(SessionState.Connected, SessionState.Disconnected, new Disconnection(DisconnectReason.TimedOut, null)).ToString(),
                Is.EqualTo("Connected -> Disconnected (TimedOut)"));
        }
    }
}
