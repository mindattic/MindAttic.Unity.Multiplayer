// Unity.Multiplayer: the NGO adapter. This is the only assembly (with its Editor and
// Tests companions) allowed to import Unity.Netcode (BIBLE UM-LAW-2).
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("Unity.Multiplayer.Editor")]
[assembly: InternalsVisibleTo("Unity.Multiplayer.Tests")]
[assembly: InternalsVisibleTo("Unity.Multiplayer.Samples.Minimal.Tests")] // tests/MinimalSample, run against the imported sample
