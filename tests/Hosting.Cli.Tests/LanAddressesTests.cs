using System.Net;
using Unity.Multiplayer.Hosting.Cli.Local;

namespace Unity.Multiplayer.Hosting.Cli.Tests;

[TestFixture]
public class LanAddressesTests
{
    [Test]
    public void GetLocalIPv4Addresses_never_throws_and_never_returns_a_loopback_address()
    {
        IReadOnlyList<string> addresses = LanAddresses.GetLocalIPv4Addresses();
        Assert.That(addresses, Is.Not.Null);
        foreach (string address in addresses)
        {
            Assert.That(IPAddress.TryParse(address, out var parsed), Is.True, $"'{address}' is not a valid IPv4 address.");
            Assert.That(IPAddress.IsLoopback(parsed!), Is.False);
        }
    }
}
