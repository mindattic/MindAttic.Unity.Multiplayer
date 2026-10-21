namespace Unity.Multiplayer.Core
{
    /// <summary>
    /// The log lines a bot run ends with (UM-T57), the contract the compose smoke watches for: <c>[NetBot:A] OK: ...</c>
    /// once the game's driver passes, <c>[NetBot:A] FAIL: ...</c> on any failure. One place, so the runner, the wait
    /// script and the tests agree.
    /// </summary>
    public static class BotLog
    {
        /// <summary>Marks a bot's passing line, after its tag.</summary>
        public const string OkMarker = "OK:";

        /// <summary>Marks a bot's failing line, after its tag.</summary>
        public const string FailMarker = "FAIL:";

        /// <summary>The tag every line of the bot playing <paramref name="role"/> starts with: <c>[NetBot:A]</c>.</summary>
        public static string Tag(string role)
        {
            return "[NetBot:" + (string.IsNullOrWhiteSpace(role) ? "?" : role.Trim()) + "]";
        }

        /// <summary><c>[NetBot:A] message</c>.</summary>
        public static string Line(string role, string message)
        {
            return Tag(role) + " " + message;
        }

        /// <summary><c>[NetBot:A] OK: message</c>.</summary>
        public static string Ok(string role, string message)
        {
            return Line(role, OkMarker + " " + message);
        }

        /// <summary><c>[NetBot:A] FAIL: message</c>.</summary>
        public static string Fail(string role, string message)
        {
            return Line(role, FailMarker + " " + message);
        }
    }
}
