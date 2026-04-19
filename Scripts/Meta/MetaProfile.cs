using System.Collections.Generic;

namespace SpacedOut.Meta;

/// <summary>
/// Persistent meta-progression profile saved to <c>user://profile.json</c>.
/// Carries Sternenstaub, purchased unlock ids, the last selected loadout perk,
/// and lifetime run counter. Survives game restarts.
/// </summary>
public class MetaProfile
{
    /// <summary>Soft currency awarded at run-end (see <see cref="StardustCalculator"/>).</summary>
    public int Stardust { get; set; }

    /// <summary>Set of unlock ids the player has purchased (perks + content packs).</summary>
    public HashSet<string> UnlockedIds { get; set; } = new();

    /// <summary>Lifetime counter — incremented exactly once per finished run (Victory or Defeat).</summary>
    public int RunsCompleted { get; set; }

    /// <summary>Last loadout perk picked in the main menu; null = no perk active.</summary>
    public string? SelectedPerkId { get; set; }

    /// <summary>Forward compat slot for future migrations.</summary>
    public int SchemaVersion { get; set; } = 1;
}
