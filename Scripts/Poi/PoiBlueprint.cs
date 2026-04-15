using System;
using SpacedOut.State;

namespace SpacedOut.Poi;

/// <summary>
/// Defines the interaction chain, proximity thresholds, rewards, and risks
/// for a single POI type. Instances are immutable templates stored in
/// <see cref="PoiBlueprintCatalog"/>.
/// </summary>
public class PoiBlueprint
{
    public string Id { get; init; } = "";
    public string DisplayName { get; init; } = "";

    /// <summary>Name shown after Tactical analysis (may differ from DisplayName).</summary>
    public string AnalyzedName { get; init; } = "";

    /// <summary>Flavor shown in the Tactical analysis log.</summary>
    public string AnalysisDescription { get; init; } = "";

    // ── Phase chain flags ────────────────────────────────────────────
    /// <summary>If true, a Gunner drill step is required between Analyzed and Extracting.</summary>
    public bool RequiresDrill { get; init; }
    /// <summary>If true, the Gunner must be in ToolMode.Mining (not Combat) to drill.</summary>
    public bool DrillRequiresMiningMode { get; init; } = true;
    /// <summary>If true, Barrage weapon mode causes immediate failure (collapse).</summary>
    public bool BarrageCausesFailure { get; init; }
    /// <summary>If true, an Engineer extraction step is the final action.</summary>
    public bool RequiresExtraction { get; init; }
    /// <summary>If true, the Engineer uses the tractor beam (ActivateTractor) instead of ExtractResource.</summary>
    public bool UsesTractorBeam { get; init; }

    // ── Proximity thresholds (map-space units) ──────────────────────
    public float AnalyzeRange { get; init; } = 150f;
    public float DrillRange { get; init; } = 80f;
    public float ExtractRange { get; init; } = 50f;

    // ── Timing ──────────────────────────────────────────────────────
    /// <summary>Seconds to complete the Tactical analysis phase.</summary>
    public float AnalyzeDuration { get; init; } = 12f;
    /// <summary>Seconds to complete the Gunner drill phase.</summary>
    public float DrillDuration { get; init; } = 15f;
    /// <summary>Seconds to complete the Engineer extraction phase.</summary>
    public float ExtractDuration { get; init; } = 10f;
    /// <summary>Seconds after opening before the POI becomes unstable (0 = no timer).</summary>
    public float InstabilityTimer { get; init; }

    // ── Heat effects ────────────────────────────────────────────────
    /// <summary>System that takes heat during drilling.</summary>
    public SystemId DrillHeatTarget { get; init; } = SystemId.Weapons;
    /// <summary>Heat per second while drilling.</summary>
    public float DrillHeatRate { get; init; } = 2f;
    /// <summary>System that takes heat during extraction.</summary>
    public SystemId ExtractHeatTarget { get; init; } = SystemId.Drive;
    /// <summary>Heat per second while extracting.</summary>
    public float ExtractHeatRate { get; init; } = 1.5f;

    // ── Rewards ─────────────────────────────────────────────────────
    public PoiRewardEntry[] Rewards { get; init; } = Array.Empty<PoiRewardEntry>();
    /// <summary>Rewards granted when the POI fails due to heat overshoot (crystal shatter etc.).</summary>
    public PoiRewardEntry[] ReducedRewards { get; init; } = Array.Empty<PoiRewardEntry>();
    /// <summary>Hull change on failure (negative = damage).</summary>
    public float FailureHullDelta { get; init; }

    // ── Trap / variant mechanic ─────────────────────────────────────
    /// <summary>Probability [0..1] that this POI spawns as a trapped variant.</summary>
    public float TrapChance { get; init; }
    /// <summary>Hull damage when a trap triggers undetected.</summary>
    public float TrapDamage { get; init; }

    // ── Anomaly-specific: distance-based reward scaling ─────────────
    /// <summary>If true, reward magnitude scales with proximity at completion.</summary>
    public bool RewardScalesWithDistance { get; init; }
    /// <summary>Full reward if ship is within this range.</summary>
    public float FullRewardRange { get; init; } = 60f;
    /// <summary>Half reward within this range.</summary>
    public float HalfRewardRange { get; init; } = 150f;

    // ── Side-effects during analysis ────────────────────────────────
    /// <summary>Sensor range penalty [0..1] applied during/after analysis.</summary>
    public float SensorRangePenalty { get; init; }
    /// <summary>Duration in seconds for sensor penalty.</summary>
    public float SensorPenaltyDuration { get; init; }
    /// <summary>Heat spike applied to all systems on close approach.</summary>
    public float CloseApproachHeatSpike { get; init; }

    // ── Reward profiles (drifting pod variants) ─────────────────────
    public PoiRewardProfile[] RewardProfiles { get; init; } = Array.Empty<PoiRewardProfile>();
}

/// <summary>
/// Named reward variant selected at spawn time (e.g. rescue pod vs cargo vs trap).
/// </summary>
public class PoiRewardProfile
{
    public string ProfileId { get; init; } = "";
    public string Label { get; init; } = "";
    public float Weight { get; init; } = 1f;
    public PoiRewardEntry[] Rewards { get; init; } = Array.Empty<PoiRewardEntry>();
    public float HullDelta { get; init; }
    public bool IsTrap { get; init; }
}
