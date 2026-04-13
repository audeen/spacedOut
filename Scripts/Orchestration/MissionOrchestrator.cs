using System;
using System.Linq;
using Godot;
using SpacedOut.LevelGen;
using SpacedOut.Mission;
using SpacedOut.Run;
using SpacedOut.Sector;
using SpacedOut.State;

namespace SpacedOut.Orchestration;

public class MissionOrchestrator
{
    private readonly MissionController _missionController;
    private readonly GameState _state;
    private readonly BroadcastService _broadcast;
    private LevelGenerator? _levelGenerator;

    private readonly SectorGenerator _sectorGenerator = new();
    private SectorData? _currentSector;
    private Sector3DMarkers? _markers;

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
        _missionController.ApplyEncounterConfig(_currentSector != null
            ? NodeEncounterConfig.DefaultForBiome(_currentSector.BiomeId)
            : NodeEncounterConfig.DefaultForBiome("asteroid_field"));
        _missionController.StartMission();
        _broadcast.BroadcastMissionStarted();
        GD.Print("[MissionOrchestrator] Mission gestartet!");
    }

    public void ResetMission()
    {
        _missionController.ResetMission();
        if (_currentSector != null)
            MissionGenerator.PopulateMission(_state, _currentSector);
        else if (_levelGenerator != null)
            MissionGenerator.PopulateMission(_state, _levelGenerator);
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

        GenerateSectorAndBuild(levelSeed, biome);

        var enc = NodeEncounterConfig.FromRunNodeType(data.Type, biome, risk);
        if (!string.IsNullOrEmpty(data.AssignedMissionId) &&
            MissionTemplateCatalog.TryGet(data.AssignedMissionId, out var tpl) && tpl != null)
        {
            enc.MissionTitle = tpl.Title;
            enc.BriefingText = MissionTemplateCatalog.BuildBriefing(tpl);
        }

        _missionController.ApplyEncounterConfig(enc);
        _state.Mission.MissionTitle = enc.MissionTitle;
        _state.Mission.BriefingText = enc.BriefingText;
        _state.Mission.MissionId = string.IsNullOrEmpty(data.AssignedMissionId)
            ? $"{runController.CurrentDefinition.Id}_{nodeId}"
            : data.AssignedMissionId;

        _missionController.StartMission();
        _broadcast.BroadcastMissionStarted();
    }

    /// <summary>
    /// Update per-frame marker visibility (discovery state changes, pin brackets).
    /// </summary>
    public void UpdateMarkers()
    {
        if (_currentSector == null || _markers == null) return;

        _markers.UpdateVisibility();
        _markers.UpdatePinPositions(_currentSector);
        SyncPinBrackets();
    }

    private void SyncPinBrackets()
    {
        if (_markers == null || _currentSector == null) return;

        var activePinIds = new System.Collections.Generic.HashSet<string>(
            _state.PinnedEntities.Select(p => p.EntityId));

        foreach (var pin in _state.PinnedEntities)
        {
            var entity = _currentSector.Entities.Find(e => e.Id == pin.EntityId);
            if (entity != null)
                _markers.AddPinBracket(pin.EntityId, entity.WorldPosition, pin.Label);
        }
    }

    private void GenerateSectorAndBuild(int seed, string biomeId)
    {
        _currentSector = _sectorGenerator.Generate(seed, biomeId);

        if (_levelGenerator != null)
            _levelGenerator.BuildFromSectorData(_currentSector);

        _markers?.Initialize(_currentSector);

        MissionGenerator.PopulateMission(_state, _currentSector);
    }

    private static string BiomeForRunNode(RunNodeData data) => data.Type switch
    {
        RunNodeType.Station or RunNodeType.End => "station_periphery",
        RunNodeType.Hostile => "wreck_zone",
        _ => "asteroid_field",
    };
}
