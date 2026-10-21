using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace Unity.Multiplayer.Core.Tests
{
    [TestFixture]
    public class RendezvousJsonTests
    {
        [Test]
        public void WriteThenRead_RoundTripsFlatObjects()
        {
            string json = RendezvousJson.Write(new Dictionary<string, object>
            {
                { "s", "a \"quoted\" \\ line\nbreak" }, { "n", 7777 }, { "b", true }, { "z", null }, { "l", 1234567890123L },
            });

            Assert.That(RendezvousJson.TryParseObject(json, out Dictionary<string, object> fields), Is.True, json);
            Assert.That(RendezvousJson.GetString(fields, "s"), Is.EqualTo("a \"quoted\" \\ line\nbreak"));
            Assert.That(RendezvousJson.GetInteger(fields, "n"), Is.EqualTo(7777));
            Assert.That(fields["b"], Is.EqualTo(true));
            Assert.That(fields["z"], Is.Null);
            Assert.That(RendezvousJson.GetInteger(fields, "l"), Is.EqualTo(1234567890123L));
        }

        [Test]
        public void Read_ServerStyleBody_WithUnicodeEscapesAndNesting()
        {
            const string body = " { \"code\" : \"ABC\\u0044\", \"expiresAt\":\"2026-10-20T12:01:00+00:00\", \"extra\":{\"a\":[1,{\"b\":\"}\"}]}, \"ttl\": 60 } ";

            Assert.That(RendezvousJson.TryParseObject(body, out Dictionary<string, object> fields), Is.True);
            Assert.That(RendezvousJson.GetString(fields, "code"), Is.EqualTo("ABCD"));
            Assert.That(fields["extra"], Is.Null, "nested values are skipped");
            Assert.That(RendezvousJson.GetInteger(fields, "ttl"), Is.EqualTo(60));
            Assert.That(RendezvousJson.GetString(fields, "missing"), Is.Null);
        }

        [TestCase("")]
        [TestCase("[1,2]")]
        [TestCase("{\"a\":}")]
        [TestCase("{\"a\":1")]
        [TestCase("{\"a\":1} trailing")]
        [TestCase("<html>")]
        public void Read_RejectsWhatIsNotAnObject(string text)
        {
            Assert.That(RendezvousJson.TryParseObject(text, out _), Is.False);
        }
    }

    [TestFixture]
    public class RelayCodesTests
    {
        [Test]
        public void RelayAlphabet_IsInsideTheJoinCodeAlphabet()
        {
            foreach (char c in RelayCodes.Alphabet)
            {
                Assert.That(JoinCodeFormat.IsAlphabetCharacter(c), Is.True, c.ToString());
            }
        }

        [TestCase("BCDF67")]
        [TestCase("wq7mnp9r")]
        [TestCase("6789BCDFGHJK")]
        public void RelayCode_RoundTripsThroughAJoinCode(string relay)
        {
            JoinCode code = RelayCodes.ToJoinCode(relay);

            Assert.That(code.Data, Is.EqualTo(relay.ToUpperInvariant()));
            Assert.That(code.Length, Is.EqualTo(relay.Length + 1), "the relay code plus a check character");
            Assert.That(JoinCode.TryParse(JoinCodeFormat.Format(code.Value).ToLowerInvariant(), out JoinCode typed), Is.True);
            Assert.That(RelayCodes.TryGetRelayCode(typed, out string back), Is.True);
            Assert.That(back, Is.EqualTo(relay.ToUpperInvariant()));
        }

        [TestCase("BCDF6")]          // too short
        [TestCase("6789BCDFGHJKL")]  // too long
        [TestCase("BCDF6A")]         // A is not in Relay's alphabet
        [TestCase("")]
        public void NonRelayCodes_AreRefused(string relay)
        {
            Assert.Throws<NotSupportedException>(() => RelayCodes.ToJoinCode(relay));
        }

        [Test]
        public void ARendezvousCodeOutsideTheRelayAlphabet_IsNotRelayShaped()
        {
            JoinCode code = JoinCode.FromData("ABCDEFGH");
            Assert.That(RelayCodes.TryGetRelayCode(code, out _), Is.False);
            Assert.That(RelayCodes.TryGetRelayCode(JoinCode.Empty, out _), Is.False);
        }
    }

    [TestFixture]
    public class JoinCodeDefaultsTests
    {
        [TestCase(Topology.Host, JoinCodeProvider.Auto, JoinCodeProvider.Relay)]
        [TestCase(Topology.Server, JoinCodeProvider.Auto, JoinCodeProvider.Rendezvous)]
        [TestCase(Topology.Client, JoinCodeProvider.Auto, JoinCodeProvider.None)]
        [TestCase(Topology.Host, JoinCodeProvider.Rendezvous, JoinCodeProvider.Rendezvous)]
        [TestCase(Topology.Server, JoinCodeProvider.Relay, JoinCodeProvider.Relay)]
        [TestCase(Topology.Host, JoinCodeProvider.None, JoinCodeProvider.None)]
        public void ForPublishing(Topology topology, JoinCodeProvider configured, JoinCodeProvider expected)
        {
            Assert.That(JoinCodeDefaults.ForPublishing(topology, configured), Is.EqualTo(expected));
        }

        [Test]
        public void ADedicatedServerWithARendezvousUrl_SelfRegisters()
        {
            var config = new NetConfig { Topology = Topology.Server, RendezvousUrl = "https://rv.example.com" };
            Assert.That(JoinCodeDefaults.ShouldSelfRegister(config), Is.True);

            Assert.That(JoinCodeDefaults.ShouldSelfRegister(new NetConfig { Topology = Topology.Server }), Is.False, "no URL");
            config.JoinCodes = JoinCodeProvider.None;
            Assert.That(JoinCodeDefaults.ShouldSelfRegister(config), Is.False, "codes off");
            config.JoinCodes = JoinCodeProvider.Auto;
            config.Topology = Topology.Host;
            Assert.That(JoinCodeDefaults.ShouldSelfRegister(config), Is.False, "a host uses Relay by default");
        }
    }

    [TestFixture]
    public class ServerAdvertisementTests
    {
        private static Func<string, string> Env(params string[] pairs)
        {
            var map = new Dictionary<string, string>();
            for (int i = 0; i + 1 < pairs.Length; i += 2)
            {
                map[pairs[i]] = pairs[i + 1];
            }

            return name => map.TryGetValue(name, out string value) ? value : null;
        }

        [Test]
        public void Plain_AdvertisesTheListenPortAndLetsTheServiceFillTheAddress()
        {
            ServerAdvertisement ad = ServerAdvertisement.For(new NetConfig { Topology = Topology.Server, Port = 7777 }, Env());

            Assert.That(ad.Address, Is.Null);
            Assert.That(ad.Port, Is.EqualTo(7777));
            Assert.That(ad.Transport, Is.EqualTo(TransportKind.Udp));
            Assert.That(ad.Warnings, Is.Empty);
        }

        [Test]
        public void RunPod_WebSocket_AdvertisesTheMappedTcpPortAndPublicIp()
        {
            var config = new NetConfig { Topology = Topology.Server, Port = 7777, Transport = TransportKind.WebSocket };
            ServerAdvertisement ad = ServerAdvertisement.For(config, Env("RUNPOD_TCP_PORT_7777", "40123", "RUNPOD_PUBLIC_IP", "69.30.85.12"));

            Assert.That(ad.Address, Is.EqualTo("69.30.85.12"));
            Assert.That(ad.Port, Is.EqualTo(40123));
            Assert.That(ad.Transport, Is.EqualTo(TransportKind.WebSocket));
            Assert.That(ad.Source, Is.EqualTo("RunPod"));
            Assert.That(ad.Warnings, Is.Empty);
        }

        [Test]
        public void RunPod_OnlyTheListenPortsMappingCounts()
        {
            var config = new NetConfig { Topology = Topology.Server, Port = 7777, Transport = TransportKind.WebSocket };
            ServerAdvertisement ad = ServerAdvertisement.For(config, Env("RUNPOD_TCP_PORT_22", "40022", "RUNPOD_PUBLIC_IP", "69.30.85.12"));

            Assert.That(ad.Port, Is.EqualTo(7777));
            Assert.That(ad.Address, Is.Null);
        }

        [Test]
        public void RunPod_Udp_WarnsAndKeepsTheListenPort()
        {
            var config = new NetConfig { Topology = Topology.Server, Port = 7777, Transport = TransportKind.Udp };
            ServerAdvertisement ad = ServerAdvertisement.For(config, Env("RUNPOD_TCP_PORT_7777", "40123", "RUNPOD_PUBLIC_IP", "69.30.85.12"));

            Assert.That(ad.Port, Is.EqualTo(7777));
            Assert.That(ad.Warnings, Has.Some.Contains("websocket"));
        }

        [Test]
        public void RunPod_MissingPublicIp_Warns()
        {
            var config = new NetConfig { Topology = Topology.Server, Port = 7777, Transport = TransportKind.WebSocket };
            ServerAdvertisement ad = ServerAdvertisement.For(config, Env("RUNPOD_TCP_PORT_7777", "40123"));

            Assert.That(ad.Port, Is.EqualTo(40123));
            Assert.That(ad.Address, Is.Null);
            Assert.That(ad.Warnings, Has.Some.Contains("RUNPOD_PUBLIC_IP"));
        }

        [Test]
        public void RunPod_BadMapping_IsIgnoredWithAWarning()
        {
            var config = new NetConfig { Topology = Topology.Server, Port = 7777, Transport = TransportKind.WebSocket };
            ServerAdvertisement ad = ServerAdvertisement.For(config, Env("RUNPOD_TCP_PORT_7777", "lots"));

            Assert.That(ad.Port, Is.EqualTo(7777));
            Assert.That(ad.Warnings, Has.Some.Contains("not a port"));
        }

        [Test]
        public void ExplicitPublicAddressAndPort_WinOverRunPod()
        {
            var config = new NetConfig
            {
                Topology = Topology.Server, Port = 7777, Transport = TransportKind.WebSocket, PublicAddress = " play.example.com ", PublicPort = 443,
            };
            ServerAdvertisement ad = ServerAdvertisement.For(config, Env("RUNPOD_TCP_PORT_7777", "40123", "RUNPOD_PUBLIC_IP", "69.30.85.12"));

            Assert.That(ad.Address, Is.EqualTo("play.example.com"));
            Assert.That(ad.Port, Is.EqualTo(443));
        }
    }

    [TestFixture]
    public class JoinCodeConfigTests
    {
        [Test]
        public void CommandLine_ReadsTheJoinCodeArguments()
        {
            var config = new NetConfig();
            NetCommandLineResult result = NetCommandLine.Apply(config, new[]
            {
                "-joincodes", "rendezvous", "-rendezvous=https://rv.example.com/base", "-rendezvoussecret", "abc",
                "-publicaddress", "play.example.com", "-publicport", "40123",
            });

            Assert.That(result.IsValid, Is.True, string.Join(" ", result.Errors));
            Assert.That(config.JoinCodes, Is.EqualTo(JoinCodeProvider.Rendezvous));
            Assert.That(config.RendezvousUrl, Is.EqualTo("https://rv.example.com/base"));
            Assert.That(config.RendezvousSecret, Is.EqualTo("abc"));
            Assert.That(config.PublicAddress, Is.EqualTo("play.example.com"));
            Assert.That(config.PublicPort, Is.EqualTo(40123));
        }

        [Test]
        public void CommandLine_ReportsBadJoinCodeArguments()
        {
            var config = new NetConfig();
            NetCommandLineResult result = NetCommandLine.Apply(config, new[] { "-joincodes", "carrier-pigeon", "-rendezvous", "rv.example.com", "-publicport", "0" });

            Assert.That(result.Errors, Has.Count.EqualTo(3));
            Assert.That(config.JoinCodes, Is.EqualTo(JoinCodeProvider.Auto));
            Assert.That(config.RendezvousUrl, Is.Null);
            Assert.That(config.PublicPort, Is.EqualTo(0));
        }

        [Test]
        public void Environment_FillsUrlSecretAndAddress()
        {
            var config = new NetConfig();
            var env = new Dictionary<string, string>
            {
                { NetEnvironment.RendezvousUrlVariable, " https://rv.example.com " },
                { NetEnvironment.RendezvousSecretVariable, "from-env" },
                { NetEnvironment.PublicAddressVariable, "203.0.113.7" },
            };

            NetEnvironment.Apply(config, name => env.TryGetValue(name, out string v) ? v : null);

            Assert.That(config.RendezvousUrl, Is.EqualTo("https://rv.example.com"));
            Assert.That(config.RendezvousSecret, Is.EqualTo("from-env"));
            Assert.That(config.PublicAddress, Is.EqualTo("203.0.113.7"));
        }

        [Test]
        public void Validate_ChecksTheRendezvousUrlAndProvider()
        {
            Assert.That(new NetConfig { RendezvousUrl = "https://rv.example.com" }.IsValid, Is.True);
            Assert.That(new NetConfig { RendezvousUrl = "rv.example.com" }.Validate(), Has.Some.Contains("RendezvousUrl"));
            Assert.That(new NetConfig { JoinCodes = (JoinCodeProvider)42 }.Validate(), Has.Some.Contains("JoinCodes"));
        }

        [Test]
        public void ToString_NeverShowsTheSecret()
        {
            Assert.That(new NetConfig { RendezvousSecret = "hunter2" }.ToString(), Does.Not.Contain("hunter2"));
        }
    }
}
