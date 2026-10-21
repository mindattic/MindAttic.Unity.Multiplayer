namespace Unity.Multiplayer.Tests
{
    /// <summary>A non-generic entity with a generic base: what the editor prefab validator's generic-NetEntity check catches (EditMode NetPrefabValidatorTests).</summary>
    public sealed class ClosedGenericProbeEntity : GenericProbeEntityBase<int>
    {
    }
}
