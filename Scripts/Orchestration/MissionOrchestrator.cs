using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using SpacedOut.Agents;
using SpacedOut.LevelGen;
using SpacedOut.Mission;
using SpacedOut.Run;
using SpacedOut.Sector;
using SpacedOut.Shared;
using SpacedOut.State;

namespace SpacedOut.Orchestration;

public class MissionOrchestrator
{
    /// <summary>Matches <see cref="MissionGenerator"/> landmark contact id.</summary>
    private const string PrimaryTargetContactId = "primary_target";

    private readonly MissionController _missionController;
    private readonly GameState _state;
    private readonly BroadcastService _broadcast;
    private LevelGenerator? _levelGenerator;

    private readonly SectorGenerator _sectorGenerator = new();
    private SectorData? _currentSector;
    private Sector3DMarkers? _markers;

    /// <summary>Fast lookup from contact id to sector entity, rebuilt per sector.</summary>
    private Dictionary<string, SectorEntity> _entityLookup = new();

    public float LevelRadius => _currentSector?.LevelRadius ?? 400f;
    public SectorData? CurrentSector => _currentSector;

    public MissionOrchestrator(
        MissionController missionController,
        GameState state,
        BroadcastService broadcast)
    {
        _missionController = missionController;
        _state = state;
        _broadcast = broadcast;
    }

    public void SetLevelGenerator(LevelGenerator? gen)
    {
        _levelGenerator = gen;
    }

    public void SetMarkers(Sector3DMarkers? markers)
    {
        _markers = markers;
    }

    public void StartMission()
    {
        if (_state.MissionStarted) return;
        var enc = _currentSector != null
            ? NodeEncounterConfig.DefaultForBiome(_currentSector.BiomeId)
            : NodeEncounterConfig.DefaultForBiome("asteroid_field");
        _missionController.ApplyEncounterConfig(enc);
        _state.Mission.MissionTitle = enc.MissionTitle;
        _state.Mission.BriefingText = enc.BriefingText;
        _state.Mission.MissionId = $"debug_{_currentSector?.BiomeId ?? "unknown"}";
        _missionController.StartMission();
        _broadcast.BroadcastMissionStarted();
        GD.Print("[MissionOrchestrator] Mission gestartet!");
    }

    public void ResetMission()
    {
        _missionController.ResetMission();
        if (_currentSector != null)
        {
            MissionGenerator.PopulateMission(_state, _currentSector);
            RebuildEntityLookup();
        }
        else if (_levelGenerator != null)
        {
            MissionGenerator.PopulateMission(_state, _levelGenerator);
        }
        GD.Print("[MissionOrchestrator] Mission zurückgesetzt");
    }

    public void RegenerateBiome(string biomeId)
    {
        if (_levelGenerator == null) return;
        GenerateSectorAndBuild(_levelGenerator.CurrentSeed, biomeId);
    }

    public void RegenerateLevel()
    {
        if (_levelGenerator == null) return;
        int seed = (int)(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() & 0x7FFFFFFF);
        string biomeId = _currentSector?.BiomeId ?? "asteroid_field";
        GenerateSectorAndBuild(seed, biomeId);
    }

    public void BeginLevelAndMissionForRunNode(string nodeId, RunController runController)
    {
        if (_state.MissionStarted) return;
        if (!runController.CurrentDefinition.Nodes.TryGetValue(nodeId, out var data))
            return;

        _state.ShowRunMapOnMainScreen = false;

        string biome = BiomeForRunNode(data);
        int risk = Math.Max(1, data.RiskRating);
        int levelSeed = RunSeed.DeriveLevelSeed(runController.CurrentRun.CampaignSeed, nodeId);

        // 1. Config + Script FIRST
        var enc = NodeEncounterConfig.FromRunNodeType(data.Type, biome, risk);
        MissionScript? script = null;

        if (!string.IsNullOrEmpty(data.AssignedMissionId))
        {
            script = MissionScriptCatalog.GetOrNull(data.AssignedMissionId);
            if (script != null)
            {
                if (script.LevelRadiusMultiplier.HasValue)
                    enc.LevelRadiusMultiplier = script.LevelRadiusMultiplier.Value;
                if (!string.IsNullOrEmpty(script.BiomeId))
                    biome = script.BiomeId;
            }

            if (MissionTemplateCatalog.TryGet(data.AssignedMissionId, out var tpl) && tpl != null)
            {
                enc.MissionTitle = tpl.Title;
                enc.BriefingText = MissionTemplateCatalog.BuildBriefing(tpl);
            }
        }

        // 2. Sector with effective radius + agent overrides + guaranteed mission markers
        GenerateSectorAndBuild(levelSeed, biome, data.Type, enc.LevelRadiusMultiplier,
            script?.AgentOverrides, script?.MissionMarkers);

        // 3. Apply config + script, then start
        _missionController.ApplyEncounterConfig(enc);
        _missionController.ApplyMissionScript(script);
        _state.Mission.MissionTitle = enc.MissionTitle;
        _state.Mission.BriefingText = enc.BriefingText;
        _state.Mission.MissionId = string.IsNullOrEmpty(data.AssignedMissionId)
            ? $"{runController.CurrentDefinition.Id}_{nodeId}"
            : data.AssignedMissionId;

        _missionController.StartMission();
        _broadcast.BroadcastMissionStarted();
    }

    /// <summary>
    /// Update per-frame: sync runtime contact state back to sector entities,
    /// update 3D marker positions and visibility, sync pin brackets.
    /// </summary>
    public void UpdateMarkers()
    {
        if (_currentSector == null || _markers == null) return;

        SyncContactsToSector();
        _markers.UpdateVisibility();
        SyncPinBrackets();
        UpdatePinnedBracketPositions();
    }

    /// <summary>
    /// Writes runtime <see cref="Contact"/> state back onto the corresponding
    /// <see cref="SectorEntity"/> so that all views (3D markers, star map, tactical)
    /// read a single consistent truth.
    /// </summary>
    private void SyncContactsToSector()
    {
        if (_currentSector == null) return;
        float lr = _currentSector.LevelRadius;

        foreach (var contact in _state.Contacts)
        {
            if (!_entityLookup.TryGetValue(contact.Id, out var entity))
            {
                entity = new SectorEntity
                {
                    Id = contact.Id,
                    Type = contact.Type == ContactType.Hostile
                        ? SectorEntityType.HostileShip
                        : SectorEntityType.NeutralShip,
                    AssetId = "encounter_marker",
                    MapPresence = MapPresence.Point,
                    IsMovable = true,
                    Radius = 3f,
                    Scale = 1f,
                    Tags = new[] { "contact", "mobile", "runtime" },
                    ContactType = contact.Type,
                    RadarShowDetectedInFullRange = contact.RadarShowDetectedInFullRange,
                };
                _currentSector.Entities.Add(entity);
                _entityLookup[contact.Id] = entity;
                _markers?.AddDynamicMarker(entity);
            }

            entity.WorldPosition = CoordinateMapper.MapToWorld(
                contact.PositionX, contact.PositionY, contact.PositionZ, lr);
            entity.Velocity = new Vector3(
                contact.VelocityX, contact.VelocityZ, contact.VelocityY);
            entity.Discovery = contact.Discovery;
            entity.ScanProgress = contact.ScanProgress;
            entity.DisplayName = contact.DisplayName;
            entity.ThreatLevel = contact.ThreatLevel;
            entity.RadarShowDetectedInFullRange = contact.RadarShowDetectedInFullRange;
        }
    }

    private void SyncPinBrackets()
    {
        if (_markers == null || _currentSector == null) return;

        var activePinIds = new HashSet<string>(_state.PinnedEntities.Select(p => p.EntityId));
        _markers.RemovePinBracketsExcept(activePinIds);

        foreach (var pin in _state.PinnedEntities)
        {
            var entity = ResolveEntityForContactId(_currentSector, pin.EntityId);
            if (entity != null)
                _markers.AddPinBracket(pin.EntityId, entity.WorldPosition, entity.Radius, pin.Label);
        }
    }

    private void UpdatePinnedBracketPositions()
    {
        if (_markers == null || _currentSector == null) return;

        foreach (var pin in _state.PinnedEntities)
        {
            var entity = ResolveEntityForContactId(_currentSector, pin.EntityId);
            if (entity != null)
                _markers.SetPinBracketWorldPosition(pin.EntityId, entity.WorldPosition);
        }
    }

    /// <summary>
    /// Maps a tactical contact id to sector data. Landmark contacts use <see cref="PrimaryTargetContactId"/>
    /// while <see cref="SectorEntity.Id"/> stays a generated instance id.
    /// </summary>
    private static SectorEntity? ResolveEntityForContactId(SectorData sector, string contactEntityId)
    {
        if (contactEntityId == PrimaryTargetContactId)
            return sector.Entities.Find(e => e.IsLandmark);
        return sector.Entities.Find(e => e.Id == contactEntityId);
    }

    private void GenerateSectorAndBuild(int seed, string biomeId, RunNodeType? nodeType = null,
        float radiusMultiplier = 1f, List<AgentSpawnProfile>? agentOverrides = null,
        IReadOnlyList<MissionMarkerPlacement>? missionMarkers = null)
    {
        _currentSector = _sectorGenerator.Generate(seed, biomeId, nodeType, radiusMultiplier, agentOverrides,
            missionMarkers);

        if (_levelGenerator != null)
            _levelGenerator.BuildFromSectorData(_currentSector);

        _markers?.Initialize(_currentSector);

        MissionGenerator.PopulateMission(_state, _currentSector);
        RebuildEntityLookup();
    }

    private void RebuildEntityLookup()
    {
        _entityLookup.Clear();
        if (_currentSector == null) return;

        foreach (var entity in _currentSector.Entities)
        {
            if (entity.MapPresence != MapPresence.Point) continue;
            _entityLookup.TryAdd(entity.Id, entity);
        }

        // MissionGenerator renames landmark entities to "primary_target"
        var landmark = _currentSector.Entities.Find(e => e.IsLandmark);
        if (landmark != null)
            _entityLookup.TryAdd(PrimaryTargetContactId, landmark);
    }

    private static string BiomeForRunNode(RunNodeData data) => data.Type switch
    {
        RunNodeType.Station or RunNodeType.End => "station_periphery",
        RunNodeType.Hostile => "wreck_zone",
        _ => "asteroid_field",
    };
}
