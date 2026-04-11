using System;
using System.Linq;
using Godot;
using SpacedOut.LevelGen;
using SpacedOut.Mission;
using SpacedOut.State;

namespace SpacedOut.Campaign;

/// <summary>
/// Orchestrates the campaign meta-loop:
///   Campaign Start → Sector Map → Node Selection → Level Generation →
///   Mission Play → Node Completion → (repeat) → Sector Boss → Next Sector → ...
///
/// Sits between <see cref="GameManager"/> (which owns the Godot scene tree)
/// and the data layer (<see cref="CampaignState"/>, <see cref="SectorMapGenerator"/>).
/// </summary>
public partial class CampaignManager : Node
{
    private CampaignState _campaign = new();
    private GameState _gameState = null!;
    private LevelGenerator? _levelGenerator;
    private NodeEncounterConfig? _activeEncounter;

    [Signal] public delegate void CampaignStartedEventHandler();
    [Signal] public delegate void SectorEnteredEventHandler(int sectorIndex, string displayName);
    [Signal] public delegate void NodeSelectedEventHandler(string nodeId, string nodeLabel);
    [Signal] public delegate void NodeCompletedEventHandler(string nodeId, string result);
    [Signal] public delegate void SectorCompletedEventHandler(int sectorIndex);
    [Signal] public delegate void CampaignEndedEventHandler(string result);
    [Signal] public delegate void MapUpdatedEventHandler();

    public CampaignState Campaign => _campaign;
    public NodeEncounterConfig? ActiveEncounter => _activeEncounter;
    public bool IsInMission { get; private set; }

    // ── Initialization ──────────────────────────────────────────────

    public void Initialize(GameState gameState, LevelGenerator? levelGenerator)
    {
        _gameState = gameState;
        _levelGenerator = levelGenerator;
    }

    // ── Campaign lifecycle ──────────────────────────────────────────

    public void StartNewCampaign(int? seed = null, int sectorCount = 3)
    {
        int runSeed = seed ??
            (int)(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() & 0x7FFFFFFF);

        _campaign = SectorMapGenerator.GenerateCampaign(runSeed, sectorCount);

        GD.Print($"[Campaign] Neue Kampagne gestartet – Seed={runSeed}, " +
                 $"{sectorCount} Sektoren");

        EmitSignal(SignalName.CampaignStarted);
        EnterCurrentSector();
    }

    private void EnterCurrentSector()
    {
        var sector = _campaign.CurrentSector;
        if (sector == null)
        {
            EndCampaign("victory");
            return;
        }

        GD.Print($"[Campaign] Sektor {sector.SectorIndex}: {sector.DisplayName} " +
                 $"(Biome={sector.BiomeId}, Difficulty={sector.Difficulty})");

        EmitSignal(SignalName.SectorEntered, sector.SectorIndex, sector.DisplayName);
        EmitSignal(SignalName.MapUpdated);
    }

    // ── Node selection ──────────────────────────────────────────────

    /// <summary>
    /// Called when the player picks a node on the sector map.
    /// Validates the selection, generates the level, and starts the mission.
    /// </summary>
    public bool SelectNode(string nodeId)
    {
        var sector = _campaign.CurrentSector;
        if (sector == null) return false;

        var node = sector.Nodes.FirstOrDefault(n => n.Id == nodeId);
        if (node == null || node.Status != NodeStatus.Available)
        {
            GD.PrintErr($"[Campaign] Knoten {nodeId} nicht verfügbar.");
            return false;
        }

        // Check fuel
        var encounter = NodeEncounterConfig.FromNode(node, sector.BiomeId);
        if (_campaign.Ship.Fuel < encounter.FuelCost)
        {
            GD.PrintErr("[Campaign] Nicht genug Treibstoff!");
            return false;
        }

        // Mark current node
        if (_campaign.CurrentNodeId != null)
        {
            var prevNode = sector.Nodes.FirstOrDefault(n => n.Id == _campaign.CurrentNodeId);
            if (prevNode != null && prevNode.Status == NodeStatus.Current)
                prevNode.Status = NodeStatus.Completed;
        }

        // Mark unreachable nodes in the same layer as skipped
        foreach (var n in sector.Nodes.Where(
            n => n.Layer == node.Layer && n.Id != nodeId &&
                 n.Status == NodeStatus.Available))
        {
            n.Status = NodeStatus.Skipped;
        }

        node.Status = NodeStatus.Current;
        _campaign.CurrentNodeId = nodeId;
        _campaign.VisitedNodeIds.Add(nodeId);
        _campaign.Ship.Fuel -= encounter.FuelCost;

        EmitSignal(SignalName.NodeSelected, nodeId, node.Label);

        // Generate the level and start the mission
        _activeEncounter = encounter;
        GenerateLevelForNode(node, encounter);

        return true;
    }

    // ── Level generation bridge ─────────────────────────────────────

    private void GenerateLevelForNode(MapNode node, NodeEncounterConfig config)
    {
        if (_levelGenerator == null)
        {
            GD.PrintErr("[Campaign] Kein LevelGenerator verfügbar!");
            return;
        }

        _levelGenerator.GenerateLevel(node.Seed, config.BiomeId);

        // Override mission info with encounter-specific data
        MissionGenerator.PopulateMission(_gameState, _levelGenerator);
        _gameState.Mission.MissionTitle = config.MissionTitle;
        _gameState.Mission.BriefingText = config.BriefingText;

        // Apply persistent ship state to the mission
        _gameState.Ship.HullIntegrity = _campaign.Ship.HullIntegrity;

        IsInMission = true;

        GD.Print($"[Campaign] Level generiert für '{node.Label}' " +
                 $"(Seed={node.Seed}, Biome={config.BiomeId})");
    }

    // ── Mission completion ──────────────────────────────────────────

    /// <summary>
    /// Called by GameManager when MissionController signals mission end.
    /// Processes rewards, updates persistent state, and advances the map.
    /// </summary>
    public void OnMissionCompleted(string result)
    {
        IsInMission = false;

        var sector = _campaign.CurrentSector;
        var node = sector?.Nodes.FirstOrDefault(n => n.Id == _campaign.CurrentNodeId);
        if (sector == null || node == null) return;

        // Transfer ship state back to persistent state
        _campaign.Ship.HullIntegrity = _gameState.Ship.HullIntegrity;
        _campaign.NodesCompleted++;

        // Apply rewards
        if (result is "success" or "partial" && node.Reward != null)
        {
            ApplyReward(node.Reward);
        }

        // Handle station healing
        if (_activeEncounter?.IsStation == true)
        {
            _campaign.Ship.HullIntegrity = Math.Min(
                100f, _campaign.Ship.HullIntegrity + 25f);
            _campaign.Ship.Fuel = Math.Min(
                PersistentShipState.MaxFuel, _campaign.Ship.Fuel + 3);
            _campaign.Ship.CrewMorale = Math.Min(
                PersistentShipState.MaxMorale, _campaign.Ship.CrewMorale + 10);
        }

        // Check for game over
        if (_campaign.Ship.HullIntegrity <= 0)
        {
            EndCampaign("destroyed");
            return;
        }

        if (_campaign.Ship.Fuel <= 0 && !_activeEncounter?.IsStation == true)
        {
            var hasStation = SectorMapGenerator.GetAvailableNodes(sector)
                .Any(n => n.Type == NodeType.Station);
            if (!hasStation)
            {
                EndCampaign("stranded");
                return;
            }
        }

        // Mark node completed
        node.Status = NodeStatus.Completed;

        EmitSignal(SignalName.NodeCompleted, node.Id, result);

        // Check if this was the boss node
        if (node.Type == NodeType.Boss)
        {
            CompleteSector();
            return;
        }

        // Unlock next nodes
        SectorMapGenerator.UnlockAdjacentNodes(sector, node.Id);
        EmitSignal(SignalName.MapUpdated);

        GD.Print($"[Campaign] Knoten '{node.Label}' abgeschlossen ({result}). " +
                 $"Hull={_campaign.Ship.HullIntegrity:F0}%, " +
                 $"Fuel={_campaign.Ship.Fuel}, " +
                 $"Scrap={_campaign.Ship.Scrap}");
    }

    private void ApplyReward(NodeReward reward)
    {
        _campaign.Ship.HullIntegrity = Math.Min(
            100f, _campaign.Ship.HullIntegrity + reward.HullRepair);
        _campaign.Ship.Fuel = Math.Min(
            PersistentShipState.MaxFuel, _campaign.Ship.Fuel + reward.FuelGain);
        _campaign.Ship.Scrap += reward.ScrapGain;

        foreach (var upgrade in reward.Upgrades)
        {
            if (!_campaign.Ship.Upgrades.Contains(upgrade))
                _campaign.Ship.Upgrades.Add(upgrade);
        }
    }

    // ── Sector completion ───────────────────────────────────────────

    private void CompleteSector()
    {
        var sector = _campaign.CurrentSector;
        if (sector == null) return;

        GD.Print($"[Campaign] Sektor {sector.SectorIndex} ({sector.DisplayName}) abgeschlossen!");
        EmitSignal(SignalName.SectorCompleted, sector.SectorIndex);

        // Advance to next sector
        _campaign.CurrentSectorIndex++;

        if (_campaign.CurrentSectorIndex >= _campaign.Sectors.Count)
        {
            EndCampaign("victory");
            return;
        }

        // Bonus fuel between sectors
        _campaign.Ship.Fuel = Math.Min(
            PersistentShipState.MaxFuel, _campaign.Ship.Fuel + 5);

        // Enter the start node of the next sector
        var nextSector = _campaign.CurrentSector!;
        var startNode = nextSector.Nodes.First(n => n.Type == NodeType.Start);
        startNode.Status = NodeStatus.Current;
        startNode.IsRevealed = true;
        _campaign.CurrentNodeId = startNode.Id;
        SectorMapGenerator.UnlockAdjacentNodes(nextSector, startNode.Id);

        EnterCurrentSector();
    }

    // ── Campaign end ────────────────────────────────────────────────

    private void EndCampaign(string result)
    {
        _campaign.IsActive = false;

        string message = result switch
        {
            "victory" => "Alle Sektoren durchquert – Mission erfolgreich!",
            "destroyed" => "Schiff zerstört – Kampagne beendet.",
            "stranded" => "Kein Treibstoff – Kampagne beendet.",
            _ => "Kampagne beendet.",
        };

        GD.Print($"[Campaign] Kampagne beendet: {message}");
        EmitSignal(SignalName.CampaignEnded, result);
    }

    // ── Query helpers for UI ────────────────────────────────────────

    public System.Collections.Generic.List<MapNode> GetAvailableNodes()
    {
        var sector = _campaign.CurrentSector;
        return sector != null
            ? SectorMapGenerator.GetAvailableNodes(sector)
            : new();
    }

    /// <summary>
    /// Returns the full map data for the current sector,
    /// suitable for serialization to the Navigator/Captain web client.
    /// </summary>
    public object GetSectorMapForClient()
    {
        var sector = _campaign.CurrentSector;
        if (sector == null) return new { };

        return new
        {
            sector_name = sector.DisplayName,
            biome = sector.BiomeId,
            difficulty = sector.Difficulty,
            current_node = _campaign.CurrentNodeId,
            fuel = _campaign.Ship.Fuel,
            hull = _campaign.Ship.HullIntegrity,
            scrap = _campaign.Ship.Scrap,
            morale = _campaign.Ship.CrewMorale,
            nodes = sector.Nodes
                .Where(n => n.IsRevealed)
                .Select(n => new
                {
                    id = n.Id,
                    layer = n.Layer,
                    type = n.Type.ToString(),
                    status = n.Status.ToString(),
                    label = n.Label,
                    description = n.Description,
                    icon = n.Icon,
                    difficulty = n.DifficultyRating,
                    x = n.MapX,
                    y = n.MapY,
                    fuel_cost = n.Type == NodeType.Station ||
                                n.Type == NodeType.Start ? 0 : 1,
                }),
            edges = sector.Edges
                .Where(e =>
                    sector.Nodes.Any(n => n.Id == e.FromNodeId && n.IsRevealed) &&
                    sector.Nodes.Any(n => n.Id == e.ToNodeId && n.IsRevealed))
                .Select(e => new
                {
                    from = e.FromNodeId,
                    to = e.ToNodeId,
                }),
            sectors_total = _campaign.Sectors.Count,
            sector_index = sector.SectorIndex,
        };
    }
}
