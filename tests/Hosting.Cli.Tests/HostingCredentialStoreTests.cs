using Unity.Multiplayer.Hosting.Cli.Credentials;

namespace Unity.Multiplayer.Hosting.Cli.Tests;

[TestFixture]
public class HostingCredentialStoreTests
{
    private string _dir = null!;
    private HostingCredentialStore _store = null!;

    [SetUp]
    public void SetUp()
    {
        _dir = Path.Combine(Path.GetTempPath(), "mp-host-vault-" + Guid.NewGuid().ToString("N"));
        _store = new HostingCredentialStore(_dir);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
    }

    [Test]
    public void GetGhcrCreds_is_null_before_anything_is_stored()
    {
        Assert.That(_store.GetGhcrCreds(), Is.Null);
    }

    [Test]
    public void Ghcr_creds_round_trip()
    {
        _store.SetGhcrCreds(new HostingCredentialStore.GhcrCreds("octocat", "ghp_abc123"));
        var creds = _store.GetGhcrCreds();
        Assert.That(creds, Is.Not.Null);
        Assert.That(creds!.Username, Is.EqualTo("octocat"));
        Assert.That(creds.Token, Is.EqualTo("ghp_abc123"));
    }

    [Test]
    public void RunPod_key_round_trips()
    {
        _store.SetRunPodCreds("rp-key-xyz");
        Assert.That(_store.GetRunPodCreds()!.ApiKey, Is.EqualTo("rp-key-xyz"));
    }

    [Test]
    public void Ssh_creds_round_trip_including_optional_passphrase()
    {
        _store.SetSshCreds("prod-box", new HostingCredentialStore.SshCreds("203.0.113.5", 2222, "deploy", "/keys/id_ed25519", "sekrit"));
        var creds = _store.GetSshCreds("prod-box");
        Assert.That(creds, Is.Not.Null);
        Assert.That(creds!.Host, Is.EqualTo("203.0.113.5"));
        Assert.That(creds.Port, Is.EqualTo(2222));
        Assert.That(creds.User, Is.EqualTo("deploy"));
        Assert.That(creds.PrivateKeyPath, Is.EqualTo("/keys/id_ed25519"));
        Assert.That(creds.Passphrase, Is.EqualTo("sekrit"));
    }

    [Test]
    public void Ssh_creds_default_port_and_user_when_the_entry_omits_them()
    {
        _store.SaveRaw("genericvps:bare", "{\"type\":\"ssh\",\"apiKey\":\"\",\"secret\":\"198.51.100.9\",\"sshKeyPath\":\"/k\"}");
        var creds = _store.GetSshCreds("bare");
        Assert.That(creds!.Host, Is.EqualTo("198.51.100.9"));
        Assert.That(creds.Port, Is.EqualTo(22));
        Assert.That(creds.User, Is.EqualTo("root"));
        Assert.That(creds.Passphrase, Is.Null);
    }

    [Test]
    public void Different_server_names_are_independent_entries()
    {
        _store.SetSshCreds("a", new HostingCredentialStore.SshCreds("10.0.0.1", 22, "root", "/k1", null));
        _store.SetSshCreds("b", new HostingCredentialStore.SshCreds("10.0.0.2", 22, "root", "/k2", null));
        Assert.That(_store.GetSshCreds("a")!.Host, Is.EqualTo("10.0.0.1"));
        Assert.That(_store.GetSshCreds("b")!.Host, Is.EqualTo("10.0.0.2"));
    }
}
