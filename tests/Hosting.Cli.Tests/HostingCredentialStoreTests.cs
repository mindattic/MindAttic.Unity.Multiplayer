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

    [TestCase("Hetzner")]
    [TestCase("Vultr")]
    [TestCase("DigitalOcean")]
    [TestCase("Linode")]
    public void CloudVps_creds_are_null_before_anything_is_stored_and_round_trip_once_set(string provider)
    {
        var (get, set) = CloudVpsAccessors(provider);
        Assert.That(get(), Is.Null);

        set(new HostingCredentialStore.CloudVpsCreds("api-token-xyz", "deploy", "/keys/id_ed25519", "ssh-ed25519 AAAA... deploy@laptop"));
        var creds = get();
        Assert.That(creds, Is.Not.Null);
        Assert.That(creds!.ApiKey, Is.EqualTo("api-token-xyz"));
        Assert.That(creds.SshUser, Is.EqualTo("deploy"));
        Assert.That(creds.SshPrivateKeyPath, Is.EqualTo("/keys/id_ed25519"));
        Assert.That(creds.SshPublicKey, Is.EqualTo("ssh-ed25519 AAAA... deploy@laptop"));
    }

    private (Func<HostingCredentialStore.CloudVpsCreds?> Get, Action<HostingCredentialStore.CloudVpsCreds> Set) CloudVpsAccessors(string provider) => provider switch
    {
        "Hetzner" => (() => _store.GetHetznerCreds(), c => _store.SetHetznerCreds(c)),
        "Vultr" => (() => _store.GetVultrCreds(), c => _store.SetVultrCreds(c)),
        "DigitalOcean" => (() => _store.GetDigitalOceanCreds(), c => _store.SetDigitalOceanCreds(c)),
        "Linode" => (() => _store.GetLinodeCreds(), c => _store.SetLinodeCreds(c)),
        _ => throw new ArgumentOutOfRangeException(nameof(provider)),
    };

    [Test]
    public void Ovh_creds_round_trip_every_field()
    {
        Assert.That(_store.GetOvhCreds(), Is.Null);

        _store.SetOvhCreds(new HostingCredentialStore.OvhCreds(
            "ovh-eu", "project-123", "app-key", "app-secret", "consumer-key", "root", "/keys/id_ed25519", "ssh-ed25519 AAAA..."));

        var creds = _store.GetOvhCreds();
        Assert.That(creds, Is.Not.Null);
        Assert.That(creds!.Endpoint, Is.EqualTo("ovh-eu"));
        Assert.That(creds.ServiceName, Is.EqualTo("project-123"));
        Assert.That(creds.ApplicationKey, Is.EqualTo("app-key"));
        Assert.That(creds.ApplicationSecret, Is.EqualTo("app-secret"));
        Assert.That(creds.ConsumerKey, Is.EqualTo("consumer-key"));
        Assert.That(creds.SshUser, Is.EqualTo("root"));
        Assert.That(creds.SshPrivateKeyPath, Is.EqualTo("/keys/id_ed25519"));
        Assert.That(creds.SshPublicKey, Is.EqualTo("ssh-ed25519 AAAA..."));
    }

    [Test]
    public void Cloud_provider_creds_are_independent_of_each_other_and_of_RunPod()
    {
        _store.SetHetznerCreds(new HostingCredentialStore.CloudVpsCreds("hetzner-token", "root", "/k1", "pk1"));
        _store.SetVultrCreds(new HostingCredentialStore.CloudVpsCreds("vultr-token", "root", "/k2", "pk2"));
        _store.SetRunPodCreds("runpod-token");

        Assert.That(_store.GetHetznerCreds()!.ApiKey, Is.EqualTo("hetzner-token"));
        Assert.That(_store.GetVultrCreds()!.ApiKey, Is.EqualTo("vultr-token"));
        Assert.That(_store.GetRunPodCreds()!.ApiKey, Is.EqualTo("runpod-token"));
        Assert.That(_store.GetDigitalOceanCreds(), Is.Null);
    }
}
