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

    /// <summary>
    /// When false, sector generation skips the small tier (cluster small rocks +
    /// scatter) and does not place <see cref="AssetCategory.AsteroidSmall"/> in
    /// resource-zone fill. Set to true to restore full density.
    /// </summary>
    public static bool SmallAsteroidsEnabled { get; set; } = true;

    /// <summary>
    /// When false, no procedural sky / cubemap / planets – only a flat background colour.
    /// Set to true to restore the full space skybox.
    /// </summary>
    public static bool SkyboxEnabled { get; set; } = true;

    /// <summary>
    /// Reserved for post-MVP story arc. Currently has no effect —
    /// <see cref="Run.RunGenerator"/> is fully procedural and always uses a station-biased
    /// generic template for act-exit convergence nodes. Kept as a non-breaking placeholder;
    /// the dormant <c>story_act_*</c> templates live on in
    /// <see cref="Run.MissionTemplateCatalog"/> for future re-use.
    /// </summary>
    public static bool StoryMissionsEnabled { get; set; }

    /// <summary>
    /// Probability [0..1] that a hostile destroyed by gunner fire becomes a salvageable wreck POI
    /// (visible in Tactical) instead of being removed. Set to 0 to always use legacy destroy-only behavior.
    /// </summary>
    public static float DestroyedHostileLootChance { get; set; } = 0.35f;
}
