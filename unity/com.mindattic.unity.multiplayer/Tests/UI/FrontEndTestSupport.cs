using System;
using System.Collections;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using MindAttic.Components.Unity;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Unity.Multiplayer.UI.Tests
{
    /// <summary>A player identity kept in memory, so the tests never touch PlayerPrefs or a real profile.</summary>
    internal sealed class TestIdentity : IPlayerIdentitySource
    {
        public TestIdentity(string name, Color color)
        {
            DisplayName = name;
            Color = color;
        }

        public string DisplayName { get; private set; }

        public Color Color { get; private set; }

        public string StoredIn
        {
            get { return "the test"; }
        }

        public int Saves { get; private set; }

        public void Save(string displayName, Color color)
        {
            DisplayName = displayName;
            Color = color;
            Saves++;
        }
    }

    internal static class FrontEndTestSupport
    {
        /// <summary>
        /// A UDP port nothing is bound to right now (the OS picks it), so these suites never collide with each other or with
        /// another Unity run on the same machine.
        /// </summary>
        public static ushort FreeUdpPort()
        {
            using (var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp))
            {
                socket.Bind(new IPEndPoint(IPAddress.Any, 0));
                return (ushort)((IPEndPoint)socket.LocalEndPoint).Port;
            }
        }

        /// <summary>The visible button labelled <paramref name="label"/> under <paramref name="root"/>, clicked as a player would.</summary>
        public static void Click(Component root, string label)
        {
            Assert.That(root, Is.Not.Null, "nothing to click '" + label + "' on");
            MaButton button = root.GetComponentsInChildren<MaButton>(false).FirstOrDefault(b => b.Label == label);
            Assert.That(button, Is.Not.Null, "no '" + label + "' button; there are: "
                + string.Join(", ", root.GetComponentsInChildren<MaButton>(false).Select(b => b.Label)));
            Assert.That(button.Interactable, Is.True, "'" + label + "' is disabled");
            button.Click();
        }

        /// <summary>True when <paramref name="root"/> shows a button labelled <paramref name="label"/>.</summary>
        public static bool HasButton(Component root, string label)
        {
            return root != null && root.GetComponentsInChildren<MaButton>(false).Any(b => b.Label == label);
        }

        /// <summary>The labels of the visible labelled buttons under <paramref name="root"/>, in order (icon buttons left out).</summary>
        public static string[] Buttons(Component root)
        {
            return root.GetComponentsInChildren<MaButton>(false).Where(b => !(b is MaIconButton)).Select(b => b.Label).ToArray();
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
            MaModal.CloseAll();
            MaToast.DismissAll();
            MaCanvas.DestroyOverlay();
            foreach (EventSystem es in UnityEngine.Object.FindObjectsByType<EventSystem>(FindObjectsInactive.Include))
            {
                UnityEngine.Object.Destroy(es.gameObject);
            }
        }
    }
}
