// Unity.Multiplayer.Core: engine-free contracts (noEngineReferences).
// Session state machine, join-code format, NetConfig, ITickClock, the RPC attributes,
// the approval payload and the protocol-hash contract live here (RFC 0001).
// Nothing in this assembly may reference UnityEngine or Unity.Netcode (BIBLE UM-LAW-2).
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("Unity.Multiplayer")]
[assembly: InternalsVisibleTo("Unity.Multiplayer.Tests")]
[assembly: InternalsVisibleTo("Unity.Multiplayer.Core.Tests")]
