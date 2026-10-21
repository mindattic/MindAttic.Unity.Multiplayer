using System;
using System.Collections.Generic;

namespace Unity.Multiplayer.Core
{
    /// <summary>
    /// A kind of host a server package is built for. Every profile gets the same provider-neutral package
    /// (<see cref="ServerPackageSpec"/>); a profile only adjusts its defaults (a host that passes TCP only needs
    /// WebSocket) and adds its own files (a Dockerfile, a compose file, setup notes). A new host is a new profile:
    /// games never change.
    /// </summary>
    /// <remarks>
    /// Built in: <see cref="GenericVpsProfile"/> (the default), <see cref="RunPodProfile"/> and
    /// <see cref="GameLiftProfile"/>. G-Portal (its Private Server program for studios), Nitrado and Edgegap/Gameye
    /// (which allocate per match instead of keeping a server running -- see <see cref="IMatchAllocator"/>)
    /// are possible later additions, registered with <see cref="HostingProfiles.Register"/>.
    /// </remarks>
    public interface IHostingProfile
    {
        /// <summary>A short, file-safe identifier (<c>GenericVps</c>), used in menus, the manifest and folder names.</summary>
        string Id { get; }

        /// <summary>A name for people (<c>Generic VPS (Docker)</c>).</summary>
        string DisplayName { get; }

        /// <summary>One sentence: what host this suits.</summary>
        string Description { get; }

        /// <summary>
        /// Adjusts the package's settings for this host before anything is written, such as forcing WebSocket. Called
        /// once by <see cref="ServerPackageSpec.For"/>.
        /// </summary>
        void Configure(ServerPackageSpec spec);

        /// <summary>
        /// The profile's own files, added to the package after the common ones. None may share a path with them.
        /// </summary>
        IReadOnlyList<ServerPackageFile> CreateFiles(ServerPackageSpec spec);
    }

    /// <summary>The hosting profiles a build can choose from.</summary>
    public static class HostingProfiles
    {
        private static readonly object Gate = new object();
        private static readonly List<IHostingProfile> Registered = new List<IHostingProfile>
        {
            new GenericVpsProfile(),
            new RunPodProfile(),
            new GameLiftProfile(),
        };

        /// <summary>The default profile: <see cref="GenericVpsProfile"/>, a Docker image plus rendezvous on a UDP-capable VPS.</summary>
        public static IHostingProfile Default
        {
            get { return Find(GenericVpsProfile.ProfileId); }
        }

        /// <summary>Every registered profile, the built-in ones first.</summary>
        public static IReadOnlyList<IHostingProfile> All
        {
            get
            {
                lock (Gate)
                {
                    return Registered.ToArray();
                }
            }
        }

        /// <summary>Adds <paramref name="profile"/>, replacing a registered one with the same <see cref="IHostingProfile.Id"/>.</summary>
        /// <exception cref="ArgumentNullException"><paramref name="profile"/> is null.</exception>
        /// <exception cref="ArgumentException">Its id is not a safe file name.</exception>
        public static void Register(IHostingProfile profile)
        {
            if (profile == null)
            {
                throw new ArgumentNullException(nameof(profile));
            }

            if (!ServerPackageSpec.IsSafeFileName(profile.Id))
            {
                throw new ArgumentException("A hosting profile id must be letters, digits, '.', '_' and '-'; got '" + profile.Id + "'.", nameof(profile));
            }

            lock (Gate)
            {
                int index = IndexOf(profile.Id);
                if (index >= 0)
                {
                    Registered[index] = profile;
                }
                else
                {
                    Registered.Add(profile);
                }
            }
        }

        /// <summary>Removes the profile with id <paramref name="id"/>. The default profile stays. True when one was removed.</summary>
        public static bool Unregister(string id)
        {
            if (string.Equals(id, GenericVpsProfile.ProfileId, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            lock (Gate)
            {
                int index = IndexOf(id);
                if (index < 0)
                {
                    return false;
                }

                Registered.RemoveAt(index);
                return true;
            }
        }

        /// <summary>The profile with id <paramref name="id"/> (ignoring case), or null.</summary>
        public static IHostingProfile Find(string id)
        {
            lock (Gate)
            {
                int index = IndexOf(id);
                return index >= 0 ? Registered[index] : null;
            }
        }

        private static int IndexOf(string id)
        {
            for (int i = 0; i < Registered.Count; i++)
            {
                if (string.Equals(Registered[i].Id, id, StringComparison.OrdinalIgnoreCase))
                {
                    return i;
                }
            }

            return -1;
        }
    }
}
