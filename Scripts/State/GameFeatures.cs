namespace SpacedOut.State;

/// <summary>
/// Global feature toggles. Flip a flag to re-enable a system without removing code paths.
/// </summary>
public static class GameFeatures
{
    /// <summary>
    /// When false (default), resource zones, zone fill, resource-zone signal contacts,
    /// map sync, and HUD drawing for zones are skipped.
    /// </summary>
    public static bool ResourceZonesEnabled { get; set; }
}
