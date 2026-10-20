using System;
using MindAttic.Unity.Multiplayer.Core;
using UnityEngine;
using UnityEngine.Rendering;

namespace MindAttic.Unity.Multiplayer
{
    /// <summary>
    /// Settings for a headless (dedicated server) process: one started with <c>-batchmode -nographics</c>, or a
    /// Linux Dedicated Server build. <see cref="NetBootstrap"/> applies them; a game with its own bootstrap can
    /// call them directly.
    /// </summary>
    public static class HeadlessBootstrap
    {
        /// <summary>
        /// True when this process has no screen to draw to: batch mode, <c>-nographics</c> (no graphics device),
        /// or a Dedicated Server build.
        /// </summary>
        public static bool IsHeadless
        {
            get
            {
#if UNITY_SERVER
                return true;
#else
                return Application.isBatchMode || SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null;
#endif
            }
        }

        /// <summary>
        /// Paces the process to the simulation: vsync off and <c>Application.targetFrameRate</c> = the tick rate,
        /// so a headless server runs one frame per network tick instead of spinning a CPU core; and keeps running
        /// without focus.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="tickRate"/> is outside 1..<see cref="NetConfig.MaxTickRate"/>.</exception>
        public static void Apply(int tickRate)
        {
            if (tickRate < 1 || tickRate > NetConfig.MaxTickRate)
            {
                throw new ArgumentOutOfRangeException(nameof(tickRate), tickRate, "Tick rate must be between 1 and " + NetConfig.MaxTickRate + ".");
            }

            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = tickRate;
            Application.runInBackground = true;
        }

        /// <summary>
        /// Folds this process's environment (<c>MINDATTIC_RENDEZVOUS_URL</c>, <c>MINDATTIC_RENDEZVOUS_SECRET</c>,
        /// <c>MINDATTIC_PUBLIC_ADDRESS</c>, see <see cref="NetEnvironment"/>) and then its command line (<c>-mode -address
        /// -port -transport -tickrate -maxplayers -joincodes -rendezvous -publicaddress -publicport</c>, see
        /// <see cref="NetCommandLine"/>) into <paramref name="config"/>, and logs every unreadable argument.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="config"/> is null.</exception>
        public static NetCommandLineResult ReadCommandLine(NetConfig config)
        {
            NetEnvironment.Apply(config);
            NetCommandLineResult result = NetCommandLine.Apply(config, Environment.GetCommandLineArgs());
            for (int i = 0; i < result.Errors.Count; i++)
            {
                Debug.LogWarning("[MindAttic.Multiplayer] Command line: " + result.Errors[i]);
            }

            return result;
        }
    }
}
