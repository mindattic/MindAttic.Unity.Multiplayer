namespace Unity.Multiplayer.Hosting.Cli.Cli;

/// <summary>
/// Reads a flat <c>--name value</c> / <c>--flag</c> argument list (no short options, no
/// sub-grouping -- every mp-host command is one verb followed by its own flags). Deliberately not
/// a general-purpose parser: it only has to serve five commands, and a hand-rolled reader is
/// trivial to unit test without depending on a prerelease CLI library.
/// </summary>
public sealed class ArgumentReader
{
    private readonly Dictionary<string, string> _values = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _flags = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Parses <paramref name="args"/>, starting at <paramref name="skip"/> (past the verb).</summary>
    /// <exception cref="ArgumentException">A <c>--name</c> token is immediately followed by another <c>--</c> token or nothing (treated as a flag only when <paramref name="booleanFlags"/> names it).</exception>
    public ArgumentReader(IReadOnlyList<string> args, int skip, IReadOnlySet<string>? booleanFlags = null)
    {
        booleanFlags ??= new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int i = skip; i < args.Count; i++)
        {
            string token = args[i];
            if (!token.StartsWith("--", StringComparison.Ordinal))
            {
                throw new ArgumentException($"Unexpected argument '{token}'; every option starts with '--'.");
            }

            string name = token[2..];
            bool hasNext = i + 1 < args.Count && !args[i + 1].StartsWith("--", StringComparison.Ordinal);
            if (booleanFlags.Contains(name) || !hasNext)
            {
                _flags.Add(name);
            }
            else
            {
                _values[name] = args[++i];
            }
        }
    }

    /// <summary>True when <c>--name</c> was given (with or without a value).</summary>
    public bool Has(string name) => _values.ContainsKey(name) || _flags.Contains(name);

    /// <summary>The value given to <c>--name</c>, or null when it was not given (or given as a bare flag).</summary>
    public string? Get(string name) => _values.TryGetValue(name, out var value) ? value : null;

    /// <summary>The value given to <c>--name</c>, or <paramref name="fallback"/> when it was not given.</summary>
    public string GetOrDefault(string name, string fallback) => Get(name) ?? fallback;

    /// <summary>The value given to <c>--name</c>; throws when it is required and missing.</summary>
    /// <exception cref="ArgumentException"><c>--name</c> was not given.</exception>
    public string Require(string name) => Get(name) ?? throw new ArgumentException($"--{name} is required.");

    /// <summary>The integer value given to <c>--name</c>, or <paramref name="fallback"/> when it was not given.</summary>
    /// <exception cref="ArgumentException">The value is not an integer.</exception>
    public int GetInt(string name, int fallback)
    {
        string? raw = Get(name);
        if (raw is null) return fallback;
        return int.TryParse(raw, out int value) ? value : throw new ArgumentException($"--{name} must be an integer; got '{raw}'.");
    }
}
