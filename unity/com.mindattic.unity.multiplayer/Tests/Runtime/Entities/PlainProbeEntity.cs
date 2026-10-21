namespace Unity.Multiplayer.Tests
{
    /// <summary>
    /// A plain, valid NetEntity for the EditMode setup, validator and build tests. It lives in this assembly because
    /// Unity will not attach a component from an Editor-only assembly.
    /// </summary>
    public class PlainProbeEntity : NetEntity
    {
    }
}
