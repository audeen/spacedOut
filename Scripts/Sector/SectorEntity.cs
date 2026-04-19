using Godot;
using SpacedOut.State;

namespace SpacedOut.Sector;

public class SectorEntity
{
    public string Id { get; set; } = "";
    public SectorEntityType Type { get; set; }
    public string AssetId { get; set; } = "";
    public Vector3 WorldPosition { get; set; }
    public Vector3 Rotation { get; set; }
    public float Scale { get; set; } = 1f;
    public float Radius { get; set; }
    public string[] Tags { get; set; } = System.Array.Empty<string>();

    public MapPresence MapPresence { get; set; } = MapPresence.None;
    public bool IsMissionRelevant { get; set; }
    /// <summary>Biome-level flavor landmark (large orientation object, procedural fill).</summary>
    public bool IsLandmark { get; set; }
    /// <summary>Mission-scoped primary objective; MissionGenerator remaps its contact id to <c>primary_target</c>.</summary>
    public bool IsPrimaryObjective { get; set; }

    // Runtime state (mutated during gameplay)
    /// <summary>Synced from runtime <see cref="SpacedOut.State.Contact.IsDestroyed"/> for map/3D visibility.</summary>
    public bool IsDestroyed { get; set; }
    public DiscoveryState Discovery { get; set; } = DiscoveryState.Hidden;
    public float ScanProgress { get; set; }
    public string DisplayName { get; set; } = "";
    public ContactType ContactType { get; set; } = ContactType.Unknown;
    public int ThreatLevel { get; set; }

    // Pre-revealed entities skip the discovery pipeline
    public bool PreRevealed { get; set; }
    /// <summary>
    /// If true, tactical radar shows this entity as Detected in the full sensor circle; if false, only in the inner third.
    /// </summary>
    public bool RadarShowDetectedInFullRange { get; set; }
    /// <summary>When true, Detected is not downgraded when the ship leaves sensor range (synced with <see cref="SpacedOut.State.Contact.PersistDetectedBeyondSensorRange"/>).</summary>
    public bool PersistDetectedBeyondSensorRange { get; set; }

    // Dynamic objects
    public Vector3 Velocity { get; set; }
    public bool IsMovable { get; set; }

    /// <summary>Agent archetype id (e.g. "pirate_raider") used by MissionGenerator to build AgentState.</summary>
    public string AgentTypeId { get; set; } = "";
    /// <summary>Initial behavior mode for this agent.</summary>
    public AgentBehaviorMode InitialBehaviorMode { get; set; }
    /// <summary>World-space anchor for patrol/guard behavior.</summary>
    public Vector3 AnchorPosition { get; set; }
    /// <summary>World-space destination for transit behavior.</summary>
    public Vector3 DestinationPosition { get; set; }

    // ── POI data (set at spawn, carried to Contact via MissionGenerator) ──
    /// <summary>Blueprint id from PoiBlueprintCatalog (empty = not a POI).</summary>
    public string PoiType { get; set; } = "";
    /// <summary>Reward profile chosen at generation time (for multi-variant POIs).</summary>
    public string PoiRewardProfile { get; set; } = "";
    /// <summary>Whether this POI instance has a hidden trap.</summary>
    public bool PoiHasTrap { get; set; }
}
