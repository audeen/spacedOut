using System.Collections.Generic;
using SpacedOut.Agents;
using SpacedOut.State;

namespace SpacedOut.Mission;

public enum TriggerRef { Landmark, NearestBeacon, MapCenter, Encounter, Exit }

public enum SpawnOrigin { EdgeRandom, NearLandmark }

public enum MarkerPlacementRule
{
    /// <summary>World-space lerp between spawn and landmark: T=0 spawn, T=1 landmark.</summary>
    AlongSpawnToLandmark,
    /// <summary>Placed at the sector centroid (uses <see cref="Sector.SectorData.LandmarkPosition"/> as anchor).</summary>
    SectorCenter,
}

public class MissionMarkerPlacement
{
    public string AssetId { get; init; } = "";
    /// <summary>Stable contact id (matches GameState Contact.Id after MissionGenerator).</summary>
    public string ContactId { get; init; } = "";
    public MarkerPlacementRule Rule { get; init; } = MarkerPlacementRule.AlongSpawnToLandmark;
    /// <summary>Interpolation factor for <see cref="MarkerPlacementRule.AlongSpawnToLandmark"/>.</summary>
    public float TAlongPath { get; init; } = 0.4f;
}

public class MissionScript
{
    public string Id { get; init; } = "";
    public string BiomeId { get; init; } = "asteroid_field";
    public float? LevelRadiusMultiplier { get; init; }

    public InitialConditions? Initial { get; init; }
    /// <summary>Mission-scoped primary objective POI (e.g. navigation relay). Replaces biome landmark as the <c>primary_target</c>.</summary>
    public PrimaryObjective? PrimaryObjective { get; init; }
    /// <summary>When true, the biome-level landmark placement is skipped (the mission provides its own primary target).</summary>
    public bool DisableBiomeLandmark { get; init; }
    /// <summary>Guaranteed markers (not from random biome MarkerAssets pool).</summary>
    public List<MissionMarkerPlacement> MissionMarkers { get; init; } = new();
    public List<AgentSpawnProfile>? AgentOverrides { get; init; }
    public List<DeferredAgentSpawn> DeferredAgentSpawns { get; init; } = new();
    public List<ProximityTrigger> ProximityTriggers { get; init; } = new();
    public List<TimeTrigger> TimeTriggers { get; init; } = new();
    public Dictionary<string, ContactClassification> Classifications { get; init; } = new();
    public Dictionary<string, ScriptedEvent> Events { get; init; } = new();
    public Dictionary<string, ScriptedDecision> Decisions { get; init; } = new();

    /// <summary>When true, <see cref="MissionController.CheckEvents"/> does not run the global time-based schedule (script proximity/time triggers only).</summary>
    public bool UseOnlyScriptTriggers { get; init; }

    /// <summary>When true, active mission event <see cref="GameEvent.TimeRemaining"/> values are not decremented (display-only countdown).</summary>
    public bool PauseActiveEventTimers { get; init; }

    /// <summary>When false, expiring <c>recovery_window</c> does not fail the mission. Default true preserves legacy behavior.</summary>
    public bool FailMissionWhenRecoveryWindowExpires { get; init; } = true;

    /// <summary>
    /// Authoring-Escape: wenn <c>true</c>, registriert <see cref="SpacedOut.Orchestration.MissionOrchestrator"/>
    /// keine Director-Heartbeat-/Hostile-KO-Hooks für diesen Sektor. Story-/Boss-Sektoren mit fest
    /// choreografiertem Spawn-Pacing setzen das, damit die dynamischen Wellen des
    /// <see cref="SpacedOut.Run.IRunDirector"/> nicht in das autorisierte Encounter-Design hineinfunken.
    /// </summary>
    public bool DisableDirectorWaves { get; init; }
}

public class DeferredAgentSpawn
{
    public string AgentType { get; init; } = "";
    /// <summary>EventId that triggers this spawn.</summary>
    public string TriggerId { get; init; } = "";
    public SpawnOrigin Origin { get; init; }
    /// <summary>Where the agent should patrol/guard after arriving.</summary>
    public TriggerRef? AnchorRef { get; init; }
    public AgentBehaviorMode InitialMode { get; init; }
    /// <summary>Spawn is skipped when the active node's risk rating is lower than this value (0 = always spawn).</summary>
    public int MinRisk { get; init; }
}

/// <summary>
/// Runtime POI spawn requested via <see cref="DecisionEffects.SpawnPois"/>. Resolved by
/// <see cref="SpacedOut.Orchestration.MissionOrchestrator"/> into a real <see cref="SectorEntity"/>
/// (3D mesh via <c>LevelGenerator.AppendStaticEntity</c>) and a <see cref="Contact"/> with
/// <see cref="Contact.PoiType"/> so the standard scan/drill/extract chain in
/// <see cref="MissionController.UpdatePoiInteractions"/> picks it up automatically.
/// </summary>
public class DeferredPoiSpawn
{
    /// <summary>AssetLibrary id of a <c>PoiMarker</c> asset (e.g. <c>poi_rich_vein</c>, <c>poi_drifting_pod</c>).</summary>
    public string AssetId { get; init; } = "";

    /// <summary>Where to anchor the spawn (default = ship position).</summary>
    public TriggerRef? AnchorRef { get; init; }

    /// <summary>Distance (map units) from the anchor toward the ship; ensures the POI does not pop directly on top of the ship.</summary>
    public float DistanceFromAnchor { get; init; } = 100f;

    /// <summary>Initial discovery state of the spawned contact (default <see cref="DiscoveryState.Detected"/> so it shows up on the map immediately).</summary>
    public DiscoveryState Discovery { get; init; } = DiscoveryState.Detected;

    /// <summary>When true, the contact is pre-revealed (visible on main screen + released to Nav).</summary>
    public bool PreRevealed { get; init; }

    /// <summary>When true, a Detected blip uses the full sensor ring (not only the inner third).</summary>
    public bool RadarShowDetectedInFullRange { get; init; }

    /// <summary>When true, Detected is not downgraded to Hidden when the ship leaves sensor range; blip can stay visible as a mission mark.</summary>
    public bool PersistDetectedBeyondSensorRange { get; init; }
}

public class InitialConditions
{
    public float HullIntegrity { get; init; } = 100f;
    public int ProbeCharges { get; init; } = 3;
    public Dictionary<SystemId, SystemOverride> Systems { get; init; } = new();
}

public class SystemOverride
{
    public float Heat { get; init; }
    public SystemStatus? Status { get; init; }
}

/// <summary>
/// Mission-scoped primary objective (e.g. navigation relay, data vault, distress pod).
/// Placed as its own POI entity in the sector; receives the stable contact id <c>primary_target</c>
/// via <see cref="MissionGenerator"/>. Reusable across missions and procedural runs.
/// </summary>
public class PrimaryObjective
{
    /// <summary>AssetDefinition id that supplies the 3D visual/shape (e.g. <c>station_relay</c>).</summary>
    public string AssetId { get; init; } = "station_relay";
    /// <summary>PoiBlueprint id that defines interaction chain, rewards, timings (e.g. <c>navigation_relay</c>).</summary>
    public string PoiBlueprintId { get; init; } = "navigation_relay";
    /// <summary>Name shown before Tactical classification completes.</summary>
    public string DefaultName { get; init; } = "Unbekanntes Signal";
    /// <summary>Name shown after Tactical classification (optional; empty = keep default).</summary>
    public string ClassifiedName { get; init; } = "";
    /// <summary>How to position the objective in the sector.</summary>
    public MarkerPlacementRule Placement { get; init; } = MarkerPlacementRule.SectorCenter;
    /// <summary>Interpolation factor for lerp-based placement rules.</summary>
    public float TAlongPath { get; init; } = 0.5f;
    /// <summary>When true, the objective is not pre-revealed; Tactical must detect it first.</summary>
    public bool HiddenUntilDiscovered { get; init; }
    /// <summary>When true, the exit marker starts hidden and is revealed after the objective is fully scanned.</summary>
    public bool HideExitUntilScanned { get; init; }
}

public class ProximityTrigger
{
    public TriggerRef Ref { get; init; }
    public float Radius { get; init; }
    public string EventId { get; init; } = "";
    public string? DecisionId { get; init; }
    public string? LogEntry { get; init; }
    public bool Once { get; init; } = true;
}

public class TimeTrigger
{
    public float Time { get; init; }
    public string EventId { get; init; } = "";
    public string? DecisionId { get; init; }
    public string? LogEntry { get; init; }
    public bool Once { get; init; } = true;
}

public class ContactClassification
{
    public string? Name { get; init; }
    public ContactType? Type { get; init; }
    public string? Log { get; init; }
}

public class ScriptedEvent
{
    public string Title { get; init; } = "";
    public string Description { get; init; } = "";
    public float Duration { get; init; } = 120f;
    public string? DecisionId { get; init; }
    public string? LogEntry { get; init; }
    public List<SystemEffect>? SystemEffects { get; init; }
    /// <summary>When false, hidden from Godot main screen active-events panel (CaptainNav still sees it).</summary>
    public bool ShowOnMainScreen { get; init; } = true;
}

public class SystemEffect
{
    public SystemId System { get; init; }
    public float HeatDelta { get; init; }
    public SystemStatus? SetStatus { get; init; }
}

public class ScriptedDecision
{
    public string Title { get; init; } = "";
    public string Description { get; init; } = "";
    public List<DecisionOption> Options { get; init; } = new();
}
