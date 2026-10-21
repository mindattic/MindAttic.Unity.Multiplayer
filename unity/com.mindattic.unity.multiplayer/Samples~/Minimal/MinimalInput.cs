// Reads the arrow keys or WASD. Nothing here is about networking: it exists so the sample runs whichever input
// handling the project uses (Project Settings > Player > Active Input Handling).
#if ENABLE_INPUT_SYSTEM && MINDATTIC_SAMPLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif
using UnityEngine;

namespace MindAttic.Unity.Multiplayer.Samples.Minimal
{
    internal static class MinimalInput
    {
        // The direction the keys point, each axis -1, 0 or 1.
        public static Vector2 Direction()
        {
#if ENABLE_INPUT_SYSTEM && MINDATTIC_SAMPLE_INPUT_SYSTEM
            // The Input System package (MINDATTIC_SAMPLE_INPUT_SYSTEM is defined by the asmdef when it is installed).
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null)
            {
                return Vector2.zero;
            }

            float x = Axis(keyboard.rightArrowKey.isPressed || keyboard.dKey.isPressed, keyboard.leftArrowKey.isPressed || keyboard.aKey.isPressed);
            float y = Axis(keyboard.upArrowKey.isPressed || keyboard.wKey.isPressed, keyboard.downArrowKey.isPressed || keyboard.sKey.isPressed);
            return new Vector2(x, y);
#elif ENABLE_LEGACY_INPUT_MANAGER
            // The classic Input Manager.
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
