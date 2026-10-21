using Unity.Multiplayer.Hosting.Cli.Cli;
using Unity.Multiplayer.Hosting.Cli.Commands;
using Unity.Multiplayer.Hosting.Cli.Credentials;
using Unity.Multiplayer.Hosting.Cli.Game;
using Unity.Multiplayer.Hosting.Cli.Packaging;
using Unity.Multiplayer.Hosting.Cli.Processes;
using Unity.Multiplayer.Hosting.Cli.Tests.Fakes;

namespace Unity.Multiplayer.Hosting.Cli.Tests;

/// <summary>
/// UM-T58: <c>mp-host game</c> deploys any Unity.Multiplayer game with no per-game script. These tests build a
/// throwaway Unity project on disk (ProjectVersion.txt, ProjectSettings.asset, EditorBuildSettings.asset, a scene)
/// and an isolated vault, then assert on the editor discovery, the argument/default resolution, the scene choice
/// and the dry-run narration, without a Unity install, Docker or a network.
/// </summary>
[TestFixture]
public class GameCommandTests
{
    private const string Version = "6000.4.3f1";

    private string _root = null!;
    private string _project = null!;
    private string _editor = null!;
    private HostingCredentialStore _vault = null!;
    private GameDeploymentStore _deployments = null!;
    private FakeProcessRunner _processes = null!;

    [SetUp]
    public void SetUp()
    {
        _root = Path.Combine(Path.GetTempPath(), "mp-host-game-" + Guid.NewGuid().ToString("N"));
        _project = Path.Combine(_root, "My Game");
        WriteProject(_project, "Unity.Cursory", ("Assets/Game/Main.unity", true), ("Assets/Game/Level2.unity", true));
        _editor = Path.Combine(_root, "Hub", Version, "Editor", "Unity.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(_editor)!);
        File.WriteAllText(_editor, "");
        _vault = new HostingCredentialStore(Path.Combine(_root, "vault"));
        _deployments = new GameDeploymentStore(Path.Combine(_root, "deployments"));
        _processes = new FakeProcessRunner();
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }

    // ---- editor discovery ----------------------------------------------------------------------------------------

    [Test]
    public void ParseProjectVersion_reads_m_EditorVersion()
    {
        Assert.That(UnityEditorLocator.ParseProjectVersion("m_EditorVersion: 6000.4.3f1\nm_EditorVersionWithRevision: 6000.4.3f1 (39d1a88d4dd1)\n"), Is.EqualTo("6000.4.3f1"));
        Assert.That(UnityEditorLocator.ParseProjectVersion("nothing here"), Is.Null);
        Assert.That(UnityEditorLocator.ParseProjectVersion(null), Is.Null);
    }

    [Test]
    public void Locate_finds_the_project_version_under_the_default_hub_folder()
    {
        var env = Editors(platform: "windows", vars: new() { ["ProgramFiles"] = Path.Combine(_root, "PF") });
        string expected = Path.Combine(_root, "PF", "Unity", "Hub", "Editor", Version, "Editor", "Unity.exe");
        Touch(expected);

        UnityEditorLocation found = UnityEditorLocator.Locate(_project, null, env);

        Assert.That(found.ExecutablePath, Is.EqualTo(expected));
        Assert.That(found.Version, Is.EqualTo(Version));
        Assert.That(found.Source, Is.EqualTo("Unity Hub's default folder"));
    }

    [Test]
    public void Locate_uses_hubs_editor_list_and_secondary_install_path()
    {
        string hub = Path.Combine(_root, "HubConfig");
        Directory.CreateDirectory(hub);
        string listed = Path.Combine(_root, "Custom", "Unity.exe");
        Touch(listed);
        File.WriteAllText(Path.Combine(hub, "editors-v2.json"),
            $$"""{"schema_version":"2","data":[{"version":"2022.3.1f1","location":["C:/x"]},{"version":"{{Version}}","location":[{{System.Text.Json.JsonSerializer.Serialize(listed)}}],"manual":true}]}""");

        Assert.That(UnityEditorLocator.Locate(_project, null, Editors(hubConfig: hub)).ExecutablePath, Is.EqualTo(listed));

        File.Delete(Path.Combine(hub, "editors-v2.json"));
        string secondaryRoot = Path.Combine(_root, "D-Unity");
        File.WriteAllText(Path.Combine(hub, "secondaryInstallPath.json"), System.Text.Json.JsonSerializer.Serialize(secondaryRoot));
        string secondary = Path.Combine(secondaryRoot, Version, "Editor", "Unity.exe");
        Touch(secondary);

        UnityEditorLocation found = UnityEditorLocator.Locate(_project, null, Editors(hubConfig: hub));
        Assert.That(found.ExecutablePath, Is.EqualTo(secondary));
        Assert.That(found.Source, Is.EqualTo("Unity Hub's install location"));
    }

    [Test]
    public void Locate_prefers_the_explicit_path_then_UNITY_EDITOR_then_UNITY_HUB_EDITORS()
    {
        string fromEnv = Path.Combine(_root, "env", "Unity.exe");
        Touch(fromEnv);
        var env = Editors(vars: new() { ["UNITY_EDITOR"] = fromEnv, ["UNITY_HUB_EDITORS"] = Path.Combine(_root, "Hub") });

        Assert.That(UnityEditorLocator.Locate(_project, _editor, env).Source, Is.EqualTo("--unity"));
        Assert.That(UnityEditorLocator.Locate(_project, null, env).ExecutablePath, Is.EqualTo(fromEnv));

        UnityEditorLocation hub = UnityEditorLocator.Locate(_project, null, Editors(vars: new() { ["UNITY_HUB_EDITORS"] = Path.Combine(_root, "Hub") }));
        Assert.That(hub.ExecutablePath, Is.EqualTo(_editor));
        Assert.That(hub.Source, Is.EqualTo("UNITY_HUB_EDITORS"));
    }

    [Test]
    public void Locate_names_the_version_every_folder_searched_and_the_override_when_missing()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => UnityEditorLocator.Locate(_project, null, Editors(platform: "linux")))!;

        Assert.That(ex.Message, Does.Contain(Version));
        Assert.That(ex.Message, Does.Contain(Path.Combine(_root, "home", "Unity", "Hub", "Editor", Version, "Editor", "Unity")));
        Assert.That(ex.Message, Does.Contain("--unity"));
        Assert.Throws<FileNotFoundException>(() => UnityEditorLocator.Locate(_project, Path.Combine(_root, "nope.exe"), Editors()));
    }

    [Test]
    public void ExecutableIn_knows_each_platforms_layout()
    {
        Assert.That(UnityEditorLocator.ExecutableIn("/hub/6000", "macos"), Is.EqualTo(Path.Combine("/hub/6000", "Unity.app", "Contents", "MacOS", "Unity")));
        Assert.That(UnityEditorLocator.ExecutableIn("/hub/6000", "linux"), Is.EqualTo(Path.Combine("/hub/6000", "Editor", "Unity")));
    }

    // ---- project facts and scene selection ---------------------------------------------------------------------

    [Test]
    public void ParseBuildScenes_keeps_enabled_scenes_in_order()
    {
        const string asset = """
            EditorBuildSettings:
              m_Scenes:
              - enabled: 0
                path: Assets/Off.unity
                guid: 1
              - enabled: 1
                path: Assets/Main.unity
                guid: 2
              - enabled: 1
                path: Assets/Two.unity
                guid: 3
            """;

        Assert.That(UnityProject.ParseBuildScenes(asset), Is.EqualTo(new[] { "Assets/Main.unity", "Assets/Two.unity" }));
    }

    [Test]
    public void Slug_comes_from_the_product_name_else_the_folder()
    {
        Assert.That(UnityProject.Load(_project).Slug, Is.EqualTo("unity-cursory"));
        Assert.That(UnityProject.ToSlug("  My Great_Game!! "), Is.EqualTo("my-great-game"));
        Assert.That(UnityProject.ToSlug("..."), Is.Null);

        string unnamed = Path.Combine(_root, "Space Race");
        WriteProject(unnamed, null, ("Assets/A.unity", true));
        Assert.That(UnityProject.Load(unnamed).Slug, Is.EqualTo("space-race"));
    }

    [Test]
    public void ResolveServerScene_prefers_the_flag_then_mp_host_json_then_the_first_build_scene()
    {
        Assert.That(UnityProject.Load(_project).ResolveServerScene(null), Is.EqualTo(("Assets/Game/Main.unity", "the first enabled scene in Build Settings")));
        Assert.That(UnityProject.Load(_project).ResolveServerScene(@"Assets\Game\Level2.unity").Scene, Is.EqualTo("Assets/Game/Level2.unity"));

        File.WriteAllText(Path.Combine(_project, UnityProject.SettingsFileName), """{ "scene": "Assets/Game/Level2.unity" }""");
        Assert.That(UnityProject.Load(_project).ResolveServerScene(null), Is.EqualTo(("Assets/Game/Level2.unity", "mp-host.json")));
    }

    [Test]
    public void ResolveServerScene_refuses_a_missing_scene_or_no_scene_at_all()
    {
        Assert.Throws<InvalidOperationException>(() => UnityProject.Load(_project).ResolveServerScene("Assets/Nope.unity"));

        string empty = Path.Combine(_root, "Empty");
        WriteProject(empty, "Empty");
        var ex = Assert.Throws<InvalidOperationException>(() => UnityProject.Load(empty).ResolveServerScene(null))!;
        Assert.That(ex.Message, Does.Contain("Build Settings").And.Contain("--scene"));
    }

    [Test]
    public void Load_refuses_a_folder_that_is_not_a_unity_project()
    {
        Directory.CreateDirectory(Path.Combine(_root, "plain"));
        Assert.Throws<InvalidOperationException>(() => UnityProject.Load(Path.Combine(_root, "plain")));
    }

    [Test]
    public void FindMultiplayerRepository_follows_a_file_reference_to_a_checkout_with_the_rendezvous_dockerfile()
    {
        string repository = Path.Combine(_root, "Unity.Multiplayer");
        Touch(Path.Combine(repository, "server", "Rendezvous", "Dockerfile"));
        Directory.CreateDirectory(Path.Combine(repository, "unity", UnityProject.PackageId));
        File.WriteAllText(Path.Combine(_project, "Packages", "manifest.json"),
            $$"""{ "dependencies": { "{{UnityProject.PackageId}}": "file:../../Unity.Multiplayer/unity/{{UnityProject.PackageId}}" } }""");

        Assert.That(UnityProject.FindMultiplayerRepository(_project), Is.EqualTo(Path.GetFullPath(repository)));

        File.WriteAllText(Path.Combine(_project, "Packages", "manifest.json"),
            $$"""{ "dependencies": { "{{UnityProject.PackageId}}": "https://github.com/mindattic/Unity.Multiplayer.git?path=unity/{{UnityProject.PackageId}}" } }""");
        Assert.That(UnityProject.FindMultiplayerRepository(_project), Is.Null);
    }

    // ---- argument parsing and defaults ------------------------------------------------------------------------

    [Test]
    public void Plan_local_defaults_build_the_first_build_scene_and_run_lan_with_rendezvous_and_no_upnp()
    {
        GamePlan plan = Plan("game", "local", "--project", _project);

        Assert.That(plan.Profile, Is.EqualTo("Local"));
        Assert.That(plan.Name, Is.EqualTo("unity-cursory-local"));
        Assert.That(plan.Delegated, Is.Not.Null);
        List<string> up = plan.Delegated!;
        Assert.That(up.Take(4), Is.EqualTo(new[] { "local", "up", "--name", "unity-cursory-local" }));
        Assert.That(After(up, "--unity"), Is.EqualTo(_editor));
        Assert.That(After(up, "--unity-project"), Is.EqualTo(Path.GetFullPath(_project)));
        Assert.That(After(up, "--scene"), Is.EqualTo("Assets/Game/Main.unity"));
        Assert.That(After(up, "--package-output"), Is.EqualTo(Path.Combine(Path.GetFullPath(_project), "Builds", "ServerPackage", "GenericVps")));
        Assert.That(After(up, "--rendezvous-url"), Is.EqualTo(GamePlan.LocalRendezvousUrl));
        Assert.That(up, Does.Contain("--no-upnp"));
        Assert.That(up, Does.Not.Contain("--dry-run"));
    }

    [Test]
    public void Plan_defaults_the_project_to_the_current_directory()
    {
        GamePlan plan = GamePlan.Create(new[] { "game", "local" }, Services(currentDirectory: _project), new RecordingDeployLog());
        Assert.That(plan.Project.Directory, Is.EqualTo(Path.GetFullPath(_project)));
    }

    [Test]
    public void Plan_local_builds_the_rendezvous_image_from_a_file_referenced_checkout()
    {
        string repository = Path.Combine(_root, "Unity.Multiplayer");
        Touch(Path.Combine(repository, "server", "Rendezvous", "Dockerfile"));
        File.WriteAllText(Path.Combine(_project, "Packages", "manifest.json"),
            $$"""{ "dependencies": { "{{UnityProject.PackageId}}": "file:../../Unity.Multiplayer/unity/{{UnityProject.PackageId}}" } }""");

        GamePlan plan = Plan("game", "local", "--project", _project);

        Assert.That(plan.PreSteps.Single().Arguments, Does.Contain(GamePlan.LocalRendezvousImage));
        Assert.That(After(plan.Delegated!, "--rendezvous-image"), Is.EqualTo(GamePlan.LocalRendezvousImage));
    }

    [Test]
    public void Plan_takes_defaults_from_mp_host_json_and_lets_flags_win()
    {
        File.WriteAllText(Path.Combine(_project, UnityProject.SettingsFileName),
            """{ "name": "cursory", "image": "mindattic/unity-cursory", "profile": "GenericVps", "scene": "Assets/Game/Level2.unity", "yes": true }""");

        GamePlan deploy = Plan("game", "deploy", "--project", _project, "--public-address", "198.51.100.7", "--tag", "v2");
        Assert.That(deploy.Name, Is.EqualTo("cursory"));
        Assert.That(After(deploy.Delegated!, "--image"), Is.EqualTo("mindattic/unity-cursory"));
        Assert.That(After(deploy.Delegated!, "--tag"), Is.EqualTo("v2"));
        Assert.That(After(deploy.Delegated!, "--scene"), Is.EqualTo("Assets/Game/Level2.unity"));
        Assert.That(After(deploy.Delegated!, "--rendezvous-url"), Is.EqualTo("http://198.51.100.7:8080"));

        GamePlan local = Plan("game", "local", "--project", _project, "--scene", "Assets/Game/Main.unity");
        Assert.That(local.Name, Is.EqualTo("cursory-local"));
        Assert.That(After(local.Delegated!, "--scene"), Is.EqualTo("Assets/Game/Main.unity"));

        _deployments.Set(new GameDeployment("GenericVps", "cursory", "box-1", null, _project, DateTimeOffset.UtcNow));
        Assert.That(Plan("game", "destroy", "--project", _project).Delegated, Does.Not.Contain("--yes"), "destroy is only ever confirmed on the command line");
    }

    [Test]
    public void Plan_genericvps_defaults_the_public_address_to_the_stored_ssh_host_and_the_image_to_the_ghcr_user()
    {
        _vault.SetSshCreds("unity-cursory", new HostingCredentialStore.SshCreds("203.0.113.40", 22, "deploy", "/key", null));
        _vault.SetGhcrCreds(new HostingCredentialStore.GhcrCreds("RyanD", "token"));

        List<string> deploy = Plan("game", "deploy", "--project", _project).Delegated!;

        Assert.That(After(deploy, "--profile"), Is.EqualTo("GenericVps"));
        Assert.That(After(deploy, "--public-address"), Is.EqualTo("203.0.113.40"));
        Assert.That(After(deploy, "--rendezvous-url"), Is.EqualTo("http://203.0.113.40:8080"));
        Assert.That(After(deploy, "--image"), Is.EqualTo("ryand/unity-cursory"));
    }

    [Test]
    public void Plan_hosted_without_an_address_refuses_for_real_but_uses_a_placeholder_in_a_dry_run()
    {
        Assert.Throws<InvalidOperationException>(() => Plan("game", "deploy", "--project", _project));
        Assert.Throws<InvalidOperationException>(() => Plan("game", "deploy", "--project", _project, "--profile", "RunPod"), "RunPod needs a rendezvous url");

        var log = new RecordingDeployLog();
        GamePlan plan = GamePlan.Create(new[] { "game", "deploy", "--project", _project, "--dry-run" }, Services(), log);
        Assert.That(After(plan.Delegated!, "--public-address"), Is.EqualTo(GamePlan.PlaceholderAddress));
        Assert.That(plan.Delegated, Does.Contain("--dry-run"));
        Assert.That(log.Lines, Has.Some.StartsWith("warn: Dry run: no --public-address"));
    }

    [Test]
    public void Plan_maps_profiles_to_their_package_and_treats_deploy_to_Local_as_local_up()
    {
        GamePlan runPod = Plan("game", "deploy", "--project", _project, "--profile", "RunPod", "--rendezvous-url", "http://r:8080");
        Assert.That(After(runPod.Delegated!, "--package-output"), Does.EndWith(Path.Combine("ServerPackage", "RunPod")));
        Assert.That(After(runPod.Delegated!, "--public-address"), Is.EqualTo("unity-cursory"));

        GamePlan hetzner = Plan("game", "deploy", "--project", _project, "--profile", "Hetzner", "--public-address", "1.2.3.4");
        Assert.That(After(hetzner.Delegated!, "--package-output"), Does.EndWith(Path.Combine("ServerPackage", "GenericVps")));

        GamePlan local = Plan("game", "deploy", "--project", _project, "--profile", "local");
        Assert.That(local.IsLocal, Is.True);
        Assert.That(local.Delegated!.Take(2), Is.EqualTo(new[] { "local", "up" }));
    }

    [Test]
    public void Plan_refuses_unknown_verbs_and_a_hosted_profile_on_local_verbs()
    {
        Assert.Throws<ArgumentException>(() => Plan("game", "launch", "--project", _project));
        Assert.Throws<ArgumentException>(() => Plan("game", "down", "--project", _project, "--profile", "RunPod"));
        Assert.Throws<ArgumentException>(() => Plan("game", "local", "--project", _project, "--profile", "GenericVps"));
    }

    [Test]
    public void Plan_skip_build_reuses_the_built_package_and_refuses_when_there_is_none()
    {
        Assert.Throws<InvalidOperationException>(() => Plan("game", "local", "--project", _project, "--skip-build"));

        string package = Path.Combine(_project, "Builds", "ServerPackage", "GenericVps");
        Touch(Path.Combine(package, ServerPackageManifest.FileName));
        GamePlan plan = Plan("game", "local", "--project", _project, "--skip-build");

        Assert.That(plan.ReusesPackage, Is.True);
        Assert.That(After(plan.Delegated!, "--package"), Is.EqualTo(Path.Combine(Path.GetFullPath(_project), "Builds", "ServerPackage", "GenericVps")));
        Assert.That(plan.Delegated, Does.Not.Contain("--unity"));
    }

    [Test]
    public void Plan_update_status_logs_destroy_use_the_remembered_server_id()
    {
        Assert.Throws<InvalidOperationException>(() => Plan("game", "status", "--project", _project));

        _deployments.Set(new GameDeployment("GenericVps", "unity-cursory", "srv-42", "203.0.113.9", _project, DateTimeOffset.UtcNow));

        Assert.That(After(Plan("game", "status", "--project", _project).Delegated!, "--server-id"), Is.EqualTo("srv-42"));
        Assert.That(After(Plan("game", "logs", "--project", _project, "--tail", "50").Delegated!, "--tail"), Is.EqualTo("50"));
        Assert.That(Plan("game", "destroy", "--project", _project, "--yes").Delegated, Does.Contain("--yes"));
        Assert.That(After(Plan("game", "update", "--project", _project, "--public-address", "203.0.113.9").Delegated!, "--server-id"), Is.EqualTo("srv-42"));
        Assert.That(After(Plan("game", "status", "--project", _project, "--server-id", "other").Delegated!, "--server-id"), Is.EqualTo("other"));
    }

    [Test]
    public void Plan_status_logs_destroy_update_default_to_the_profile_the_project_last_deployed_with()
    {
        // UM-T61: after 'game local', a bare status/logs inspects the local server instead of a GenericVps that doesn't exist.
        _deployments.SetLastProfile(_project, "Local");
        var log = new RecordingDeployLog();
        GamePlan status = GamePlan.Create(new[] { "game", "status", "--project", _project }, Services(), log);
        Assert.That(status.Profile, Is.EqualTo("Local"));
        Assert.That(status.Name, Is.EqualTo("unity-cursory-local"));
        Assert.That(status.Delegated, Is.Null, "local status runs docker compose itself");
        Assert.That(log.Lines, Has.Some.Contains("Profile Local: the one this project last deployed with"));
        Assert.That(Plan("game", "logs", "--project", _project).Profile, Is.EqualTo("Local"));
        Assert.That(Plan("game", "destroy", "--project", _project, "--yes").Delegated!.Take(2), Is.EqualTo(new[] { "local", "down" }));

        // A hosted profile is remembered the same way, and its server id comes from that profile's record.
        _deployments.SetLastProfile(_project, "RunPod");
        _deployments.Set(new GameDeployment("RunPod", "unity-cursory", "pod-7", null, _project, DateTimeOffset.UtcNow));
        GamePlan logs = Plan("game", "logs", "--project", _project);
        Assert.That(After(logs.Delegated!, "--profile"), Is.EqualTo("RunPod"));
        Assert.That(After(logs.Delegated!, "--server-id"), Is.EqualTo("pod-7"));
        Assert.That(After(Plan("game", "update", "--project", _project, "--rendezvous-url", "http://r:8080").Delegated!, "--profile"), Is.EqualTo("RunPod"));

        // --profile always wins, and deploy never guesses from history.
        _deployments.Set(new GameDeployment("GenericVps", "unity-cursory", "srv-1", "203.0.113.9", _project, DateTimeOffset.UtcNow));
        Assert.That(Plan("game", "status", "--project", _project, "--profile", "GenericVps").Profile, Is.EqualTo("GenericVps"));
        Assert.That(Plan("game", "deploy", "--project", _project, "--public-address", "203.0.113.9").Profile, Is.EqualTo(GamePlan.DefaultProfile));
    }

    [Test]
    public void Plan_without_a_remembered_profile_still_defaults_to_mp_host_json_then_GenericVps()
    {
        _deployments.Set(new GameDeployment("GenericVps", "unity-cursory", "srv-42", "203.0.113.9", _project, DateTimeOffset.UtcNow));
        Assert.That(Plan("game", "status", "--project", _project).Profile, Is.EqualTo("GenericVps"));

        File.WriteAllText(Path.Combine(_project, "mp-host.json"), """{ "profile": "Local" }""");
        Assert.That(Plan("game", "status", "--project", _project).Profile, Is.EqualTo("Local"));
    }

    [Test]
    public void DeploymentStore_remembers_the_last_profile_per_project_folder()
    {
        string other = Path.Combine(_root, "Other Checkout", "My Game");
        Directory.CreateDirectory(other);
        Assert.That(_deployments.LastProfile(_project), Is.Null);

        _deployments.SetLastProfile(_project, "Local");
        _deployments.SetLastProfile(other, "Hetzner");
        Assert.That(_deployments.LastProfile(_project), Is.EqualTo("Local"));
        Assert.That(_deployments.LastProfile(_project + Path.DirectorySeparatorChar), Is.EqualTo("Local"), "a trailing separator is the same folder");
        Assert.That(_deployments.LastProfile(other), Is.EqualTo("Hetzner"), "two checkouts of one game keep their own");

        _deployments.SetLastProfile(_project, "RunPod");
        Assert.That(_deployments.LastProfile(_project), Is.EqualTo("RunPod"), "the latest deploy replaces it");

        string file = Directory.GetFiles(Path.Combine(_deployments.Directory, "projects")).Single(f => File.ReadAllText(f).Contains("RunPod"));
        Assert.That(Path.GetFileName(file), Does.StartWith("my-game--"));
        File.WriteAllText(file, "{ not json");
        Assert.That(_deployments.LastProfile(_project), Is.Null, "an unreadable record is ignored");
    }

    [Test]
    public void Plan_passes_every_repeated_env_and_masks_secrets_when_described()
    {
        GamePlan plan = Plan("game", "local", "--project", _project, "--env", "A=1", "--env", "B=2", "--rendezvous-secret", "s3cret", "--server-password", "pw");

        Assert.That(string.Join(' ', plan.Delegated!), Does.Contain("--env A=1 --env B=2"));
        string described = GamePlan.Describe(plan.Delegated!);
        Assert.That(described, Does.Not.Contain("s3cret").And.Not.Contain(" pw"));
        Assert.That(described, Does.Contain("--rendezvous-secret ***"));
    }

    [Test]
    public void ArgumentReader_keeps_every_value_of_a_repeated_option()
    {
        var reader = new ArgumentReader(new[] { "x", "--env", "A=1", "--env", "B=2", "--name", "n" }, 1);
        Assert.That(reader.Repeated("env"), Is.EqualTo(new[] { "A=1", "B=2" }));
        Assert.That(reader.Get("env"), Is.EqualTo("B=2"));
        Assert.That(reader.Repeated("missing"), Is.Empty);
    }

    // ---- dry-run narration end to end ---------------------------------------------------------------------------

    [Test]
    public async Task Local_dry_run_narrates_the_unity_build_and_compose_without_starting_anything()
    {
        var log = new RecordingDeployLog();

        int exit = await GameCommand.RunAsync(new[] { "game", "local", "--project", _project, "--dry-run" }, log, Services(), CancellationToken.None);

        Assert.That(exit, Is.EqualTo(0), string.Join('\n', log.Lines));
        Assert.That(_processes.Calls, Is.Empty, "a dry run starts no process");
        string unity = log.DryRunCalls.Single(c => c.Contains(UnityBatchCommand.BatchMethod));
        Assert.That(unity, Does.StartWith(_editor.Contains(' ') ? $"\"{_editor}\"" : _editor));
        Assert.That(unity, Does.Contain("-serverScene Assets/Game/Main.unity"));
        Assert.That(unity, Does.Contain("-serverProfile GenericVps"));
        Assert.That(unity, Does.Contain("-logFile"));
        Assert.That(log.DryRunCalls, Has.Some.Contains("docker compose"));
        Assert.That(log.Lines, Has.Some.StartsWith("info: ==> mp-host local up --name unity-cursory-local"));
        Assert.That(_vault.GetRendezvousSecret(), Is.Null, "a dry run never writes the vault");
    }

    [Test]
    public async Task Deploy_dry_run_with_an_empty_vault_narrates_the_whole_pipeline()
    {
        var log = new RecordingDeployLog();

        int exit = await GameCommand.RunAsync(new[] { "game", "deploy", "--project", _project, "--dry-run" }, log, Services(), CancellationToken.None);

        Assert.That(log.Lines, Has.None.StartsWith("error: "), string.Join('\n', log.Lines));
        Assert.That(exit, Is.EqualTo(0).Or.EqualTo(1), "health is simulated; the run must just finish");
        Assert.That(_processes.Calls, Is.Empty);
        Assert.That(log.DryRunCalls, Has.Some.Contains("-serverScene Assets/Game/Main.unity"));
        Assert.That(log.DryRunCalls, Has.Some.Contains("docker push"));
        Assert.That(Directory.Exists(_deployments.Directory), Is.False, "a dry run remembers no deployment");
    }

    [Test]
    public async Task Dry_run_without_a_unity_install_warns_and_narrates_with_a_placeholder()
    {
        var log = new RecordingDeployLog();
        var services = new GameServices(_vault)
        {
            Processes = _processes,
            Editors = Editors(platform: "linux"),
            Deployments = _deployments,
            PreferLinuxDocker = false,
        };

        int exit = await GameCommand.RunAsync(new[] { "game", "local", "--project", _project, "--dry-run" }, log, services, CancellationToken.None);

        Assert.That(exit, Is.EqualTo(0), string.Join('\n', log.Lines));
        Assert.That(log.Lines, Has.Some.StartsWith("warn: Dry run: Unity " + Version));
        Assert.That(log.DryRunCalls, Has.Some.StartsWith($"\"<Unity {Version} editor>\""));
    }

    [Test]
    public async Task Down_dry_run_hands_off_to_local_down()
    {
        var log = new RecordingDeployLog();
        int exit = await GameCommand.RunAsync(new[] { "game", "down", "--project", _project, "--dry-run" }, log, Services(), CancellationToken.None);

        // Nothing was started, so 'local down' finds no stored state and says how to tear it down anyway.
        Assert.That(log.Lines, Has.Some.StartsWith("info: ==> mp-host local down --name unity-cursory-local --dry-run"));
        Assert.That(exit, Is.EqualTo(1));
    }

    [Test]
    public async Task Game_help_prints_the_usage()
    {
        var log = new RecordingDeployLog();
        Assert.That(await GameCommand.RunAsync(new[] { "game", "help" }, log, Services(), CancellationToken.None), Is.EqualTo(0));
        Assert.That(log.Lines.Single(), Does.Contain("game local"));
    }

    // ---- docker engine -----------------------------------------------------------------------------------------

    [Test]
    public async Task FindLinuxEngine_switches_only_from_a_windows_engine_to_an_answering_linux_one()
    {
        var log = new RecordingDeployLog();
        var windowsThenLinux = new ScriptedProcessRunner(args => args.Contains("-H") ? "linux" : "windows");
        Assert.That(await DockerEngine.FindLinuxEngineAsync(windowsThenLinux, _ => null, log, CancellationToken.None), Is.EqualTo(DockerEngine.LinuxEnginePipe));

        var linux = new ScriptedProcessRunner(_ => "linux");
        Assert.That(await DockerEngine.FindLinuxEngineAsync(linux, _ => null, log, CancellationToken.None), Is.Null);
        Assert.That(linux.Calls, Is.EqualTo(1));

        var preset = new ScriptedProcessRunner(_ => "windows");
        Assert.That(await DockerEngine.FindLinuxEngineAsync(preset, n => n == "DOCKER_HOST" ? "tcp://x" : null, log, CancellationToken.None), Is.Null);
        Assert.That(preset.Calls, Is.Zero);
    }

    // ---- package profile / batch command -----------------------------------------------------------------------

    [Test]
    public void PackageProfiles_map_every_provider_onto_a_unity_package_profile()
    {
        Assert.That(PackageProfiles.For("runpod"), Is.EqualTo("RunPod"));
        Assert.That(PackageProfiles.For("GameLift"), Is.EqualTo("GameLift"));
        Assert.That(PackageProfiles.For("Hetzner"), Is.EqualTo("GenericVps"));
        Assert.That(PackageProfiles.For("Local"), Is.EqualTo("GenericVps"));
    }

    [Test]
    public void UnityBatchCommand_adds_the_scene_log_and_development_flags_only_when_asked()
    {
        var bare = UnityBatchCommand.Arguments(new ServerPackageBuildRequest("GenericVps", null, "u", "/p"), "/out");
        Assert.That(bare, Does.Not.Contain("-serverScene").And.Not.Contain("-logFile").And.Not.Contain("-serverDevelopment"));

        var full = UnityBatchCommand.Arguments(new ServerPackageBuildRequest("Fly", null, "u", "/p", "Assets/S.unity", "/out", "/b.log", true), "/out");
        Assert.That(string.Join(' ', full), Does.Contain("-serverProfile GenericVps -serverOutput /out -serverScene Assets/S.unity -logFile /b.log -serverDevelopment"));
    }

    // ---- helpers ------------------------------------------------------------------------------------------------

    private GamePlan Plan(params string[] args) => GamePlan.Create(args, Services(), new RecordingDeployLog());

    private GameServices Services(string? currentDirectory = null) => new(_vault)
    {
        Processes = _processes,
        Editors = Editors(vars: new() { ["UNITY_HUB_EDITORS"] = Path.Combine(_root, "Hub") }),
        Deployments = _deployments,
        CurrentDirectory = currentDirectory ?? _root,
        PreferLinuxDocker = false,
    };

    private EditorSearchEnvironment Editors(string platform = "windows", Dictionary<string, string>? vars = null, string? hubConfig = null) => new()
    {
        Platform = platform,
        Home = Path.Combine(_root, "home"),
        HubConfigDirectory = hubConfig ?? Path.Combine(_root, "no-hub-config"),
        GetEnvironmentVariable = name => vars is not null && vars.TryGetValue(name, out string? v) ? v
            : name == "ProgramFiles" ? Path.Combine(_root, "no-program-files") : null,
    };

    private static string? After(IReadOnlyList<string> args, string option)
    {
        int i = args.ToList().IndexOf(option);
        return i >= 0 && i + 1 < args.Count ? args[i + 1] : null;
    }

    internal static void Touch(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "");
    }

    internal static void WriteProject(string directory, string? productName, params (string Path, bool Enabled)[] scenes)
    {
        Directory.CreateDirectory(Path.Combine(directory, "ProjectSettings"));
        Directory.CreateDirectory(Path.Combine(directory, "Packages"));
        File.WriteAllText(Path.Combine(directory, "ProjectSettings", "ProjectVersion.txt"),
            $"m_EditorVersion: {Version}\nm_EditorVersionWithRevision: {Version} (39d1a88d4dd1)\n");
        File.WriteAllText(Path.Combine(directory, "ProjectSettings", "ProjectSettings.asset"),
            $"PlayerSettings:\n  companyName: MindAttic\n  productName: {productName}\n");
        var build = new System.Text.StringBuilder("EditorBuildSettings:\n  m_Scenes:\n");
        foreach ((string path, bool enabled) in scenes)
        {
            build.Append($"  - enabled: {(enabled ? 1 : 0)}\n    path: {path}\n    guid: 0\n");
            Touch(Path.Combine(directory, path));
        }

        File.WriteAllText(Path.Combine(directory, "ProjectSettings", "EditorBuildSettings.asset"), build.ToString());
        File.WriteAllText(Path.Combine(directory, "Packages", "manifest.json"), """{ "dependencies": {} }""");
    }

    /// <summary>Answers each docker call from its arguments.</summary>
    private sealed class ScriptedProcessRunner : IProcessRunner
    {
        private readonly Func<IReadOnlyList<string>, string> _answer;

        public ScriptedProcessRunner(Func<IReadOnlyList<string>, string> answer) => _answer = answer;

        public int Calls { get; private set; }

        public Task<ProcessOutcome> RunAsync(string fileName, IReadOnlyList<string> arguments, string? workingDirectory, string? standardInput,
            CancellationToken ct, IReadOnlyDictionary<string, string>? environmentVariables = null)
        {
            Calls++;
            return Task.FromResult(new ProcessOutcome(0, _answer(arguments) + "\n", ""));
        }
    }
}
