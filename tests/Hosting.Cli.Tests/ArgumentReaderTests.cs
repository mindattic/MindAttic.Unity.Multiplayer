using Unity.Multiplayer.Hosting.Cli.Cli;

namespace Unity.Multiplayer.Hosting.Cli.Tests;

[TestFixture]
public class ArgumentReaderTests
{
    [Test]
    public void Get_reads_a_value_option()
    {
        var reader = new ArgumentReader(new[] { "deploy", "--name", "prod-box" }, 1);
        Assert.That(reader.Get("name"), Is.EqualTo("prod-box"));
    }

    [Test]
    public void Has_is_true_for_a_bare_flag()
    {
        var reader = new ArgumentReader(new[] { "deploy", "--dry-run" }, 1, new HashSet<string> { "dry-run" });
        Assert.That(reader.Has("dry-run"), Is.True);
        Assert.That(reader.Get("dry-run"), Is.Null);
    }

    [Test]
    public void Require_throws_when_missing()
    {
        var reader = new ArgumentReader(new[] { "deploy" }, 1);
        Assert.Throws<ArgumentException>(() => reader.Require("name"));
    }

    [Test]
    public void GetInt_parses_and_falls_back()
    {
        var reader = new ArgumentReader(new[] { "deploy", "--tail", "50" }, 1);
        Assert.That(reader.GetInt("tail", 10), Is.EqualTo(50));
        Assert.That(reader.GetInt("missing", 10), Is.EqualTo(10));
    }

    [Test]
    public void GetInt_throws_for_a_non_integer()
    {
        var reader = new ArgumentReader(new[] { "deploy", "--tail", "nope" }, 1);
        Assert.Throws<ArgumentException>(() => reader.GetInt("tail", 10));
    }

    [Test]
    public void A_trailing_option_with_no_value_becomes_a_flag_even_when_not_declared_boolean()
    {
        var reader = new ArgumentReader(new[] { "deploy", "--yes" }, 1);
        Assert.That(reader.Has("yes"), Is.True);
    }

    [Test]
    public void A_non_dashdash_token_is_rejected()
    {
        Assert.Throws<ArgumentException>(() => new ArgumentReader(new[] { "deploy", "oops" }, 1));
    }
}
