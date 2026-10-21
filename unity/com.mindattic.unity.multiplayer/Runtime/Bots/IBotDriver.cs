using System;
using System.Threading.Tasks;

namespace Unity.Multiplayer
{
    /// <summary>
    /// A game's bot strategy (UM-T57): what a headless bot client does once it has joined. The library launches the bot
    /// (<c>-bot &lt;role&gt;</c>), joins by code and then calls <see cref="RunAsync"/> with a <see cref="NetBot"/> that
    /// holds the session, the role and the scenario, and gives await-friendly waits. Pass the run with
    /// <see cref="NetBot.Pass"/> (the driver may keep playing afterwards), fail it with <see cref="NetBot.Fail"/> or by
    /// throwing. Returning without either passes it.
    /// </summary>
    /// <remarks>
    /// <para>Register the driver either as a component (a MonoBehaviour in the scene that implements this interface) or
    /// as a plain class marked <see cref="NetBotDriverAttribute"/> with a parameterless constructor. A game has one driver
    /// and reads <see cref="NetBot.Role"/> to tell its bots apart.</para>
    /// <para>Runs on the main thread (Unity's synchronization context), so it may touch scene objects between awaits.</para>
    /// </remarks>
    /// <example>
    /// <code>
    /// [NetBotDriver]
    /// public sealed class WalkerBot : IBotDriver
    /// {
    ///     public async Task RunAsync(NetBot bot)
    ///     {
    ///         Player me = await bot.LocalPlayer&lt;Player&gt;();
    ///         me.MoveTo(new Vector2(5, 0));
    ///         await bot.Until(() =&gt; me.Position.x &gt;= 4.9f, "the player to reach the wall");
    ///         bot.Pass("reached the wall");
    ///     }
    /// }
    /// </code>
    /// </example>
    public interface IBotDriver
    {
        /// <summary>Plays one bot's run on a connected session. Cancelled (<see cref="NetBot.Cancellation"/>) when the run fails or times out.</summary>
        Task RunAsync(NetBot bot);
    }

    /// <summary>
    /// Marks a class as the game's <see cref="IBotDriver"/>, so a <c>-bot</c> launch finds it with no scene change. The
    /// class needs a parameterless constructor (a MonoBehaviour is added to the bot's own GameObject instead). Nothing
    /// else references the class, so in an IL2CPP build with high code stripping also mark it <c>[Preserve]</c>.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, Inherited = false)]
    public sealed class NetBotDriverAttribute : Attribute
    {
    }
}
