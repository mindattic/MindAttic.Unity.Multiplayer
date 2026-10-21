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

    [Test]
    public void Aws_creds_round_trip_and_are_shared_by_both_aws_profiles()
    {
        _store.SetAwsCreds(new HostingCredentialStore.AwsCreds("AKIAEXAMPLE", "secret", "us-east-1"));
        var creds = _store.GetAwsCreds();
        Assert.That(creds, Is.Not.Null);
        Assert.That(creds!.AccessKeyId, Is.EqualTo("AKIAEXAMPLE"));
        Assert.That(creds.SecretAccessKey, Is.EqualTo("secret"));
        Assert.That(creds.Region, Is.EqualTo("us-east-1"));
    }

    [Test]
    public void Gce_creds_round_trip()
    {
        _store.SetGceCreds(new HostingCredentialStore.GceCreds("my-project", "ya29.token"));
        var creds = _store.GetGceCreds();
        Assert.That(creds!.ProjectId, Is.EqualTo("my-project"));
        Assert.That(creds.AccessToken, Is.EqualTo("ya29.token"));
    }

    [Test]
    public void Azure_creds_round_trip()
    {
        _store.SetAzureCreds(new HostingCredentialStore.AzureCreds("tenant-1", "client-1", "secret-1", "sub-1"));
        var creds = _store.GetAzureCreds();
        Assert.That(creds!.TenantId, Is.EqualTo("tenant-1"));
        Assert.That(creds.ClientId, Is.EqualTo("client-1"));
        Assert.That(creds.ClientSecret, Is.EqualTo("secret-1"));
        Assert.That(creds.SubscriptionId, Is.EqualTo("sub-1"));
    }

    [Test]
    public void GetAwsCreds_is_null_when_a_field_is_missing()
    {
        _store.SaveRaw("aws", "{\"type\":\"aws\",\"accessKeyId\":\"AKIA\",\"secretAccessKey\":\"\",\"region\":\"us-east-1\"}");
        Assert.That(_store.GetAwsCreds(), Is.Null);
    }

    [Test]
    public void Cloud_vm_options_round_trip_arbitrary_keys_per_profile_and_server()
    {
        _store.SetCloudVmOptions("AwsEc2", "ec2-box", new Dictionary<string, string>
        {
            ["amiId"] = "ami-123", ["keyName"] = "my-keypair", ["sshUser"] = "ubuntu", ["sshKeyPath"] = "/keys/id_ed25519",
        });
        var options = _store.GetCloudVmOptions("AwsEc2", "ec2-box");
        Assert.That(options, Is.Not.Null);
        Assert.That(options!["amiId"], Is.EqualTo("ami-123"));
        Assert.That(options["sshUser"], Is.EqualTo("ubuntu"));
    }

    [Test]
    public void Cloud_vm_options_are_independent_per_profile_for_the_same_server_name()
    {
        _store.SetCloudVmOptions("AwsEc2", "box", new Dictionary<string, string> { ["amiId"] = "ami-1" });
        _store.SetCloudVmOptions("Gce", "box", new Dictionary<string, string> { ["zone"] = "us-central1-a" });
        Assert.That(_store.GetCloudVmOptions("AwsEc2", "box")!.ContainsKey("zone"), Is.False);
        Assert.That(_store.GetCloudVmOptions("Gce", "box")!["zone"], Is.EqualTo("us-central1-a"));
    }

    [Test]
    public void GetCloudVmOptions_is_null_before_anything_is_stored()
    {
        Assert.That(_store.GetCloudVmOptions("AzureVm", "nobody"), Is.Null);
    }

    [Test]
    public void Fly_creds_round_trip()
    {
        Assert.That(_store.GetFlyCreds(), Is.Null);

        _store.SetFlyCreds(new HostingCredentialStore.FlyCreds("fo1_abc123", "personal"));
        var creds = _store.GetFlyCreds();
        Assert.That(creds, Is.Not.Null);
        Assert.That(creds!.ApiToken, Is.EqualTo("fo1_abc123"));
        Assert.That(creds.OrgSlug, Is.EqualTo("personal"));
    }

    [Test]
    public void GetFlyCreds_is_null_when_the_org_slug_is_missing()
    {
        _store.SaveRaw("fly", "{\"type\":\"fly\",\"apiKey\":\"fo1_abc123\",\"secret\":\"\"}");
        Assert.That(_store.GetFlyCreds(), Is.Null);
    }

    [Test]
    public void Edgegap_creds_round_trip()
    {
        _store.SetEdgegapCreds(new HostingCredentialStore.EdgegapCreds("edg-token-1", "my-game"));
        var creds = _store.GetEdgegapCreds();
        Assert.That(creds!.ApiToken, Is.EqualTo("edg-token-1"));
        Assert.That(creds.AppName, Is.EqualTo("my-game"));
    }

    [Test]
    public void GetEdgegapCreds_is_null_before_anything_is_stored()
    {
        Assert.That(_store.GetEdgegapCreds(), Is.Null);
    }

    [Test]
    public void GetEdgegapCreds_is_null_when_a_field_is_missing()
    {
        _store.SaveRaw("edgegap", "{\"type\":\"edgegap\",\"apiKey\":\"edg-token-1\",\"appName\":\"\"}");
        Assert.That(_store.GetEdgegapCreds(), Is.Null);
    }

    [Test]
    public void Gameye_creds_round_trip()
    {
        _store.SetGameyeCreds(new HostingCredentialStore.GameyeCreds("gy-key-1", "my-game", "eu-west"));
        var creds = _store.GetGameyeCreds();
        Assert.That(creds!.ApiKey, Is.EqualTo("gy-key-1"));
        Assert.That(creds.ImageName, Is.EqualTo("my-game"));
        Assert.That(creds.Location, Is.EqualTo("eu-west"));
    }

    [Test]
    public void GetGameyeCreds_is_null_before_anything_is_stored()
    {
        Assert.That(_store.GetGameyeCreds(), Is.Null);
    }

    [Test]
    public void GetGameyeCreds_is_null_when_a_field_is_missing()
    {
        _store.SaveRaw("gameye", "{\"type\":\"gameye\",\"apiKey\":\"gy-key-1\",\"imageName\":\"my-game\",\"location\":\"\"}");
        Assert.That(_store.GetGameyeCreds(), Is.Null);
    }
}
