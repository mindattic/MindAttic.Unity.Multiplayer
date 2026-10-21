// Reads the arrow keys or WASD with whichever input handling the project uses (Project Settings > Player > Active
// Input Handling). Nothing here is about networking.
#if ENABLE_INPUT_SYSTEM && GAME_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif
using UnityEngine;

namespace MyGame
{
    internal static class GameInput
    {
        /// <summary>The direction the keys point, each axis -1, 0 or 1.</summary>
        public static Vector2 Direction()
        {
#if ENABLE_INPUT_SYSTEM && GAME_INPUT_SYSTEM
            // The Input System package (GAME_INPUT_SYSTEM is defined by the asmdef when it is installed).
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null)
            {
                return Vector2.zero;
            }

            float x = Axis(keyboard.rightArrowKey.isPressed || keyboard.dKey.isPressed, keyboard.leftArrowKey.isPressed || keyboard.aKey.isPressed);
            float y = Axis(keyboard.upArrowKey.isPressed || keyboard.wKey.isPressed, keyboard.downArrowKey.isPressed || keyboard.sKey.isPressed);
            return new Vector2(x, y);
#elif ENABLE_LEGACY_INPUT_MANAGER
            return new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical"));
#else
            return Vector2.zero;
#endif
        }

        private static float Axis(bool positive, bool negative)
        {
            return (positive ? 1f : 0f) - (negative ? 1f : 0f);
        }
    }
}
