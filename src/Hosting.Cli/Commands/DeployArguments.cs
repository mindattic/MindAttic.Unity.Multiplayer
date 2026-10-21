using Unity.Multiplayer.Hosting.Cli.Cli;
using Unity.Multiplayer.Hosting.Cli.Packaging;

namespace Unity.Multiplayer.Hosting.Cli.Commands;

/// <summary>
/// Pure argument-to-model mapping shared by <c>deploy</c> and <c>update</c> (everything but which
/// provisioner call runs at the end). Kept free of any I/O so NUnit can assert on the parsed shape
/// directly instead of round-tripping through stdout.
/// </summary>
public static class DeployArguments
{
    private static readonly HashSet<string> BooleanFlags = new(StringComparer.OrdinalIgnoreCase) { "dry-run" };

    /// <summary>Parses <paramref name="args"/> (after the verb) into a reader with this command's flags recognised.</summary>
    public static ArgumentReader Parse(IReadOnlyList<string> args) => new(args, 1, BooleanFlags);

    public static ServerPackageBuildRequest BuildPackageRequest(ArgumentReader args, string profileId) =>
        new(profileId, args.Get("package"), args.Get("unity"), args.Get("unity-project"));

    /// <summary>The <c>ghcr.io/owner/repo:tag</c> reference the pipeline builds, pushes, provisions and updates onto.</summary>
    public static string BuildImageReference(ArgumentReader args, string registry)
    {
        string repository = args.Require("image");
        string tag = args.GetOrDefault("tag", "latest");
        return $"{registry}/{repository.TrimStart('/')}:{tag}";
    }

    /// <summary>
    /// <c>MINDATTIC_RENDEZVOUS_URL</c>, <c>MINDATTIC_RENDEZVOUS_SECRET</c> (<paramref name="rendezvousSecret"/>,
    /// resolved by <see cref="Composition.HostingComposer.ResolveRendezvousSecret"/> from
    /// <c>--rendezvous-secret</c> or the vault), <c>MINDATTIC_PUBLIC_ADDRESS</c> and an
    /// optional <c>MINDATTIC_SERVER_PASSWORD</c>, then every repeated <c>--env KEY=VALUE</c> on top
    /// (so an operator can override or add to the defaults without a code change).
    /// </summary>
    /// <exception cref="ArgumentException">An <c>--env</c> value is not <c>KEY=VALUE</c>.</exception>
    public static Dictionary<string, string> BuildEnvironment(ArgumentReader args, IReadOnlyList<string> rawArgs, string rendezvousSecret)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rendezvousSecret);
        var env = new Dictionary<string, string>
        {
            ["MINDATTIC_RENDEZVOUS_URL"] = args.Require("rendezvous-url"),
            ["MINDATTIC_RENDEZVOUS_SECRET"] = rendezvousSecret,
            ["MINDATTIC_PUBLIC_ADDRESS"] = args.Require("public-address"),
        };
        string? password = args.Get("server-password");
        if (!string.IsNullOrEmpty(password))
        {
            env["MINDATTIC_SERVER_PASSWORD"] = password;
        }

        for (int i = 0; i < rawArgs.Count; i++)
        {
            if (rawArgs[i] != "--env" || i + 1 >= rawArgs.Count) continue;
            string pair = rawArgs[++i];
            int eq = pair.IndexOf('=');
            if (eq <= 0)
            {
                throw new ArgumentException($"--env '{pair}' must be KEY=VALUE.");
            }

            env[pair[..eq]] = pair[(eq + 1)..];
        }

        return env;
    }

    public static TimeSpan HealthTimeout(ArgumentReader args) => TimeSpan.FromSeconds(args.GetInt("health-timeout", 120));

    public static TimeSpan JoinCodeTimeout(ArgumentReader args) => TimeSpan.FromSeconds(args.GetInt("joincode-timeout", 60));
}
