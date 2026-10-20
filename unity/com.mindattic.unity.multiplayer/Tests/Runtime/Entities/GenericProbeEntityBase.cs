namespace MindAttic.Unity.Multiplayer.Tests
{
    /// <summary>
    /// A generic NetEntity base. Abstract and memberless, so the generator lets it compile (MAMP005 fires only for
    /// generic entities that declare RPCs or synced fields, or are concrete); the editor prefab validator refuses a
    /// prefab whose entity derives from it (EditMode NetPrefabValidatorTests). It lives in this assembly because Unity will
    /// not attach a component from an Editor-only assembly.
    /// </summary>
    public abstract class GenericProbeEntityBase<T> : NetEntity
    {
    }
}
