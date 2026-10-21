// Unity.Multiplayer.UI: the optional drop-in front end (UM-T48). Compiled only when com.mindattic.components.unity
// is installed (MINDATTIC_COMPONENTS); uses Unity.Shared profiles when com.mindattic.unity.shared is too
// (MINDATTIC_SHARED). It talks to NetBootstrap / NetSession only and never imports Unity.Netcode (BIBLE UM-LAW-2).
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("Unity.Multiplayer.UI.Tests")]

// The public front-end test kit (UM-T62): FrontEndTesting exposes the menus override and Bind to a game's own tests.
[assembly: InternalsVisibleTo("Unity.Multiplayer.UI.Testing")]
