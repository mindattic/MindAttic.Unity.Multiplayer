using System;
using System.Collections;
using NUnit.Framework;
using Unity.Multiplayer.UI.Testing;
using UnityEngine;

namespace Unity.Multiplayer.UI.Tests
{
    /// <summary>The package's own front-end tests use the public kit (UM-T62), so it is exercised the way a game uses it.</summary>
    internal static class FrontEndTestSupport
    {
        /// <summary>The visible button labelled <paramref name="label"/> under <paramref name="root"/>, clicked as a player would.</summary>
        public static void Click(Component root, string label)
        {
            FrontEndTesting.Click(root, label);
        }

        /// <summary>True when <paramref name="root"/> shows a button labelled <paramref name="label"/>.</summary>
        public static bool HasButton(Component root, string label)
        {
            return FrontEndTesting.HasButton(root, label);
        }

        /// <summary>The labels of the visible labelled buttons under <paramref name="root"/>, in order (icon buttons left out).</summary>
        public static string[] Buttons(Component root)
        {
            return FrontEndTesting.Buttons(root);
        }

        /// <summary>Waits up to <paramref name="seconds"/> (real time) for <paramref name="condition"/>; fails with <paramref name="what"/>.</summary>
        public static IEnumerator Wait(Func<bool> condition, float seconds, Func<string> what)
        {
            float until = Time.realtimeSinceStartup + seconds;
            while (!condition())
            {
                if (Time.realtimeSinceStartup > until)
                {
                    Assert.Fail("Timed out after " + seconds + " s: " + what());
                }

                yield return null;
            }
        }

        /// <summary>Closes every dialog and toast and removes the shared overlay and event systems the components made.</summary>
        public static void CleanUpUi()
        {
            FrontEndTesting.CleanUpUi();
        }
    }
}
