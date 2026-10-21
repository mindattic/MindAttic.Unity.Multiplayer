using System;
using System.Collections.Generic;
using System.Text;

namespace Unity.Multiplayer.Core
{
    /// <summary>
    /// The AWS GameLift hosting profile: the server package runs directly on a GameLift-managed fleet
    /// instance, no Docker -- GameLift's classic EC2 fleets launch the build's executable straight from
    /// its RuntimeConfiguration launch path, not a container. Adds no Dockerfile, only onboarding notes.
    /// </summary>
    /// <remarks>
    /// <para>Three pieces make a GameLift deploy work, each covered by its own ticket's half:</para>
    /// <list type="number">
    /// <item><description>In Runtime, <c>GameLiftServerHost</c> (<c>Runtime/Hosting/GameLift</c>, compiled
    /// in only under the <c>MINDATTIC_GAMELIFT</c> version define once the GameLift Server SDK package
    /// is installed) drives <c>InitSDK</c>/<c>ProcessReady</c>/<c>ActivateGameSession</c>/
    /// <c>AcceptPlayerSession</c>/<c>RemovePlayerSession</c>/<c>ProcessEnding</c>.</description></item>
    /// <item><description>mp-host's <c>GameLiftProvisioner</c> (<c>Hosting.Cli</c>) zips this package,
    /// uploads it as a GameLift build and creates the fleet; mp-host does not push a Docker image for
    /// this profile (<c>IProvisioner.RequiresContainerImage</c> is false).</description></item>
    /// <item><description><see cref="GameLiftMatchAllocator"/> creates one game session (and a player
    /// session per seat) per match on that fleet once it is up; the fleet itself is provisioned once and
    /// stays running between matches, like every other <see cref="IHostingProfile"/>.</description></item>
    /// </list>
    /// <para>See docs/articles/hosting/gamelift.md for the full setup and cost notes.</para>
    /// </remarks>
    public sealed class GameLiftProfile : IHostingProfile
    {
        /// <summary>This profile's <see cref="Id"/>.</summary>
        public const string ProfileId = "GameLift";

        /// <summary>The notes file's name.</summary>
        public const string NotesFileName = "HOSTING.txt";

        /// <inheritdoc />
        public string Id
        {
            get { return ProfileId; }
        }

        /// <inheritdoc />
        public string DisplayName
        {
            get { return "AWS GameLift (managed fleets, GameLift Server SDK)"; }
        }

        /// <inheritdoc />
        public string Description
        {
            get { return "A GameLift-managed EC2 fleet: no Docker -- the GameLift Server SDK drives the server's lifecycle and GameLiftMatchAllocator creates one game session per match."; }
        }

        /// <inheritdoc />
        public void Configure(ServerPackageSpec spec)
        {
            if (spec == null)
            {
                throw new ArgumentNullException(nameof(spec));
            }

            // GameLift allocates one game session per match itself (GameLiftMatchAllocator); a fleet
            // instance is never found by Relay the way a listen server would be.
            if (spec.Config.JoinCodes == JoinCodeProvider.Relay)
            {
                spec.Config.JoinCodes = JoinCodeProvider.Auto;
            }
        }

        /// <inheritdoc />
        public IReadOnlyList<ServerPackageFile> CreateFiles(ServerPackageSpec spec)
        {
            if (spec == null)
            {
                throw new ArgumentNullException(nameof(spec));
            }

            return new[] { ServerPackageFile.Text(NotesFileName, ComposeNotes(spec)) };
        }

        /// <summary>The <c>HOSTING.txt</c> text.</summary>
        public static string ComposeNotes(ServerPackageSpec spec)
        {
            if (spec == null)
            {
                throw new ArgumentNullException(nameof(spec));
            }

            var text = new StringBuilder();
            text.Append("Hosting profile: GameLift\n");
            text.Append("==========================\n\n");
            text.Append("No Dockerfile here on purpose: GameLift's classic EC2 fleets run this package's executable\n");
            text.Append("directly (its RuntimeConfiguration launch path), not a container. mp-host zips this folder,\n");
            text.Append("uploads it as a GameLift build and creates the fleet for you -- see\n");
            text.Append("docs/articles/hosting/gamelift.md.\n\n");
            text.Append("1. Install the GameLift Server SDK package in this project (it defines MINDATTIC_GAMELIFT) so\n");
            text.Append("   GameLiftServerHost actually calls InitSDK/ProcessReady; without it the build still runs,\n");
            text.Append("   but as a plain dedicated server GameLift cannot manage or route sessions to.\n");
            text.Append("2. mp-host deploy --profile GameLift --name <fleet> (--package <this folder> | --unity ...)\n");
            text.Append("   --rendezvous-url ... --rendezvous-secret ... --public-address ... builds the GameLift\n");
            text.Append("   build + fleet; it does not push a Docker image for this profile.\n");
            text.Append("3. A matchmaking service creates one game session per match with GameLiftMatchAllocator\n");
            text.Append("   (Unity.Multiplayer.Core) against the fleet id mp-host printed -- not by deploying again.\n");
            text.Append("   The fleet stays up between matches, same as every other hosting profile.\n");
            text.Append("4. AWS credentials and every launch option (EC2 instance type, fleet type, ...) come from\n");
            text.Append("   MindAttic.Vault (mp-host credentials set-aws / set-cloud-options --profile GameLift), never\n");
            text.Append("   from this package or the repo.\n");
            return text.ToString();
        }
    }
}
