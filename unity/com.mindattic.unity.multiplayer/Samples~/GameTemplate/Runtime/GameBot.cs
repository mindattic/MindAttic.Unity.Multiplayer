// The game's bot: the only bot code a game writes. Launched with -bot A (mp-host game smoke, NetBotTest), the library
// joins the game by code with no menus and hands the connected session to RunAsync. This one walks its player to a
// crate (A takes crate:0, B crate:1, any other role crate:2), pushes it down and passes once the server has moved it.
// Replace the strategy with your game's; keep the shape: wait for what you need, play like a player would (the same
// [ToServer] calls), and Pass or Fail.
using System.Threading.Tasks;
using Unity.Multiplayer; // IBotDriver, NetBot, [NetBotDriver]
using UnityEngine;
using UnityEngine.Scripting;

namespace MyGame
{
    [NetBotDriver, Preserve]
    public sealed class GameBot : IBotDriver
    {
        public static string CrateFor(string role)
        {
            return Arena.CrateKey(role == "A" ? 0 : role == "B" ? 1 : 2);
        }

        public async Task RunAsync(NetBot bot)
        {
            Player me = await bot.LocalPlayer<Player>();
            string key = CrateFor(bot.Role);
            Crate crate = null;
            await bot.Until(() => bot.Session.Objects.TryGet(key, out crate), "the crate " + key + " to arrive");
            Vector2 start = crate.Position;

            // Walk to just above the crate...
            Vector2 above = start + new Vector2(0f, 1.2f);
            while ((me.Position - above).magnitude > 0.25f)
            {
                Vector2 gap = above - me.Position;
                me.Move(Vector2.ClampMagnitude(gap / 0.5f, 1f)); // slows down as it arrives
                await bot.Seconds(0.1f);                          // Move allows 20 calls a second
            }

            // ...then push it down until the server has moved it a whole unit.
            me.Move(Vector2.down);
            await bot.Until(() => crate != null && (crate.Position - start).magnitude > 1f, "the server to move " + key, 20f);
            me.Move(Vector2.zero);
            bot.Pass("pushed " + key + " from " + start + " to " + crate.Position + " as " + me.DisplayName);
        }
    }
}
