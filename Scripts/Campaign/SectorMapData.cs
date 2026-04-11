using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace SpacedOut.Campaign;

// ── Node types ──────────────────────────────────────────────────────

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum NodeType
{
    Start,
    Navigation,
    ScanAnomaly,
    DebrisField,
    Encounter,
    DistressSignal,
    Station,
    EliteEncounter,
    Boss,
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum NodeStatus
{
    Locked,
    Available,
    Current,
    Completed,
    Skipped,
}

// ── Map node ────────────────────────────────────────────────────────

public class MapNode
{
    public string Id { get; set; } = "";
    public int Layer { get; set; }
    public int SlotIndex { get; set; }
    public NodeType Type { get; set; }
    public NodeStatus Status { get; set; } = NodeStatus.Locked;
    public int Seed { get; set; }
    public string? BiomeOverride { get; set; }

    public string Label { get; set; } = "";
    public string Description { get; set; } = "";
    public string Icon { get; set; } = "";

    public int DifficultyRating { get; set; } = 1;
    public bool IsRevealed { get; set; }

    public NodeReward? Reward { get; set; }

    public float MapX { get; set; }
    public float MapY { get; set; }
}

// ── Reward granted after completing a node ──────────────────────────

public class NodeReward
{
    public float HullRepair { get; set; }
    public int FuelGain { get; set; }
    public List<string> Upgrades { get; set; } = new();
    public int ScrapGain { get; set; }
}

// ── Edge connecting two nodes ───────────────────────────────────────

public class MapEdge
{
    public string FromNodeId { get; set; } = "";
    public string ToNodeId { get; set; } = "";
}

// ── One sector (act) of the campaign ────────────────────────────────

public class SectorDefinition
{
    public string Id { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string BiomeId { get; set; } = "asteroid_field";
    public int SectorIndex { get; set; }
    public int Difficulty { get; set; } = 1;
    public int Seed { get; set; }

    public List<MapNode> Nodes { get; set; } = new();
    public List<MapEdge> Edges { get; set; } = new();

    public int LayerCount { get; set; }
}

// ── Persistent ship state that survives between nodes ───────────────

public class PersistentShipState
{
    public float HullIntegrity { get; set; } = 100f;
    public int Fuel { get; set; } = 10;
    public int Scrap { get; set; }
    public List<string> Upgrades { get; set; } = new();
    public int CrewMorale { get; set; } = 80;

    public const int MaxFuel = 20;
    public const int MaxMorale = 100;
}

// ── Top-level campaign state ────────────────────────────────────────

public class CampaignState
{
    public int RunSeed { get; set; }
    public List<SectorDefinition> Sectors { get; set; } = new();
    public int CurrentSectorIndex { get; set; }
    public string? CurrentNodeId { get; set; }
    public PersistentShipState Ship { get; set; } = new();

    public bool IsActive { get; set; }
    public int NodesCompleted { get; set; }

    public List<string> VisitedNodeIds { get; set; } = new();

    public SectorDefinition? CurrentSector =>
        CurrentSectorIndex < Sectors.Count ? Sectors[CurrentSectorIndex] : null;
}
