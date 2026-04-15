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
    public LandmarkOverride? Landmark { get; init; }
    /// <summary>Guaranteed markers (not from random biome MarkerAssets pool).</summary>
    public List<MissionMarkerPlacement> MissionMarkers { get; init; } = new();
    public List<AgentSpawnProfile>? AgentOverrides { get; init; }
    public List<DeferredAgentSpawn> DeferredAgentSpawns { get; init; } = new();
    public List<ProximityTrigger> ProximityTriggers { get; init; } = new();
    public List<TimeTrigger> TimeTriggers { get; init; } = new();
    public Dictionary<string, ContactClassification> Classifications { get; init; } = new();
    public Dictionary<string, ScriptedEvent> Events { get; init; } = new();
    public Dictionary<string, ScriptedDecision> Decisions { get; init; } = new();
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

public class LandmarkOverride
{
    public string DefaultName { get; init; } = "";
    public string ClassifiedName { get; init; } = "";
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
