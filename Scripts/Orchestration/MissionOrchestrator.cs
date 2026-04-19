using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using SpacedOut.Agents;
using SpacedOut.Commands.Handlers;
using SpacedOut.LevelGen;
using SpacedOut.Meta;
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
    private MetaProgressService? _meta;

    /// <summary>Fast lookup from contact id to sector entity, rebuilt per sector.</summary>
    private Dictionary<string, SectorEntity> _entityLookup = new();

    /// <summary>
    /// Last args passed to <see cref="GenerateSectorAndBuild"/> — kept so debug regen
    /// (<see cref="RegenerateLevel"/>, <see cref="RegenerateBiome"/>) still places mission POIs
    /// (e.g. tutorial <c>station_relay</c> from <see cref="MissionScript.PrimaryObjective"/>).
    /// </summary>
    private RunNodeType? _lastSectorNodeType;
    private List<AgentSpawnProfile>? _lastSectorAgentOverrides;
    private IReadOnlyList<MissionMarkerPlacement>? _lastSectorMissionMarkers;
    private MissionScript? _lastSectorMissionScript;

    /// <summary>
    /// Optional director (set by <see cref="RunOrchestrator"/>). Used to bias event picks and
    /// adjust generic NPC spawn profiles against <see cref="PacingState.ThreatPool"/>.
    /// </summary>
    private IRunDirector? _director;
    private Func<RunDirectorContext?>? _directorCtxProvider;

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

    /// <summary>M7: gives the orchestrator access to the persistent profile so that
    /// pack-gated <see cref="NodeEvent"/>s are filtered by <see cref="MetaProfile.UnlockedIds"/>.</summary>
    public void SetMetaProgress(MetaProgressService meta)
    {
        _meta = meta;
    }

    /// <summary>
    /// Wires the run director + context provider. <see cref="RunOrchestrator"/> calls this once per
    /// run so event picks and generic spawn profiles can be re-rolled against the threat pool.
    /// </summary>
    public void SetDirector(IRunDirector director, Func<RunDirectorContext?> ctxProvider)
    {
        _director = director;
        _directorCtxProvider = ctxProvider;
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
        GenerateSectorAndBuild(_levelGenerator.CurrentSeed, biomeId,
            _lastSectorNodeType,
            GetDebugRadiusMultiplier(biomeId),
            _lastSectorAgentOverrides,
            _lastSectorMissionMarkers,
            _lastSectorMissionScript);
    }

    public void RegenerateLevel()
    {
        if (_levelGenerator == null) return;
        int seed = (int)(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() & 0x7FFFFFFF);
        string biomeId = _currentSector?.BiomeId ?? "asteroid_field";
        GenerateSectorAndBuild(seed, biomeId,
            _lastSectorNodeType,
            GetDebugRadiusMultiplier(biomeId),
            _lastSectorAgentOverrides,
            _lastSectorMissionMarkers,
            _lastSectorMissionScript);
    }

    /// <summary>
    /// Debug regeneration preserves the effective multiplier only when regenerating
    /// the <b>same</b> biome (e.g. "Neues Level" with new seed). If the target biome
    /// differs from <see cref="SectorData.BiomeId"/>, using
    /// <c>LevelRadius / target.Basis</c> would mix radii from unrelated biomes
    /// (e.g. Station → Asteroid yields a tiny multiplier). In that case we use
    /// <see cref="GetDefaultRadiusMultiplierForBiome"/>.
    /// </summary>
    private float GetDebugRadiusMultiplier(string biomeId)
    {
        if (_currentSector != null
            && string.Equals(_currentSector.BiomeId, biomeId, StringComparison.Ordinal)
            && BiomeDefinition.TryGet(biomeId, out var biome)
            && biome.LevelRadius > 0.1f)
        {
            float current = _currentSector.LevelRadius / biome.LevelRadius;
            if (current > 0.1f) return current;
        }

        return GetDefaultRadiusMultiplierForBiome(biomeId);
    }

    /// <summary>Matches typical gameplay defaults from <see cref="NodeEncounterConfig"/>.</summary>
    private static float GetDefaultRadiusMultiplierForBiome(string biomeId) =>
        biomeId == "station_periphery" ? 0.6f : 5f;

    /// <summary>Debug: spawn a POI marker near the ship with full 3D mesh (see <see cref="LevelGenerator.AppendStaticEntity"/>).</summary>
    public void DebugSpawnPoiMarker(string assetId)
    {
        if (_currentSector == null || _levelGenerator == null)
        {
            GD.PrintErr("[Debug] POI-Spawn: Kein Level geladen.");
            return;
        }

        var def = AssetLibrary.GetById(assetId);
        if (def == null || def.Category != AssetCategory.PoiMarker)
        {
            GD.PrintErr($"[Debug] POI-Spawn: Kein PoiMarker-Asset: {assetId}");
            return;
        }

        var ship = _state.Ship;
        var entity = DebugPoiMarkerFactory.CreateNearShip(
            def, ship.PositionX, ship.PositionY, ship.PositionZ, _currentSector.LevelRadius);
        entity.Discovery = DiscoveryState.Scanned;
        entity.ScanProgress = 100;
        entity.PreRevealed = true;

        _currentSector.Entities.Add(entity);
        _levelGenerator.AppendStaticEntity(entity);
        _entityLookup[entity.Id] = entity;

        var contact = MissionGenerator.CreateContactFromEntity(entity, _currentSector);
        _state.Contacts.Add(contact);

        _broadcast.BroadcastStateUpdates();
        GD.Print($"[Debug] POI gespawnt: {entity.DisplayName} ({assetId})");
    }

    /// <summary>
    /// Resolve a <see cref="DeferredPoiSpawn"/> request (typically from <see cref="DecisionEffects.SpawnPois"/>)
    /// into a real <see cref="SectorEntity"/> with 3D mesh and matching <see cref="Contact"/>.
    /// Silently no-ops when no level/sector is loaded (e.g. pre-sector resolution path).
    /// </summary>
    public bool SpawnRuntimePoi(DeferredPoiSpawn spawn)
    {
        if (spawn == null || string.IsNullOrEmpty(spawn.AssetId)) return false;
        if (_currentSector == null || _levelGenerator == null)
        {
            GD.Print($"[MissionOrchestrator] SpawnRuntimePoi('{spawn.AssetId}') skipped — no active sector.");
            return false;
        }

        var def = AssetLibrary.GetById(spawn.AssetId);
        if (def == null || def.Category != AssetCategory.PoiMarker)
        {
            GD.PrintErr($"[MissionOrchestrator] SpawnRuntimePoi: asset '{spawn.AssetId}' not found or not a PoiMarker.");
            return false;
        }

        var ship = _state.Ship;
        float anchorX, anchorY, anchorZ;
        if (spawn.AnchorRef.HasValue)
        {
            var anchor = _missionController.ResolveRefMap(spawn.AnchorRef.Value);
            if (anchor.HasValue)
            {
                anchorX = anchor.Value.X;
                anchorY = anchor.Value.Y;
                anchorZ = anchor.Value.Z;
            }
            else
            {
                anchorX = ship.PositionX; anchorY = ship.PositionY; anchorZ = ship.PositionZ;
            }
        }
        else
        {
            anchorX = ship.PositionX; anchorY = ship.PositionY; anchorZ = ship.PositionZ;
        }

        float dx = ship.PositionX - anchorX;
        float dy = ship.PositionY - anchorY;
        float dz = ship.PositionZ - anchorZ;
        float len = MathF.Sqrt(dx * dx + dy * dy + dz * dz);
        float spawnX, spawnY, spawnZ;
        float dist = MathF.Max(0f, spawn.DistanceFromAnchor);
        if (len < 0.001f)
        {
            float angle = GD.Randf() * Mathf.Tau;
            spawnX = anchorX + MathF.Cos(angle) * dist;
            spawnY = anchorY;
            spawnZ = anchorZ + MathF.Sin(angle) * dist;
        }
        else
        {
            spawnX = anchorX + (dx / len) * dist;
            spawnY = anchorY + (dy / len) * dist;
            spawnZ = anchorZ + (dz / len) * dist;
        }

        spawnX = Mathf.Clamp(spawnX, 5f, 995f);
        spawnY = Mathf.Clamp(spawnY, 5f, 995f);
        spawnZ = Mathf.Clamp(spawnZ, 5f, 995f);

        var entity = DebugPoiMarkerFactory.CreateAtMapPosition(
            def, spawnX, spawnY, spawnZ, _currentSector.LevelRadius);

        entity.Discovery = spawn.Discovery;
        entity.PreRevealed = spawn.PreRevealed;
        entity.RadarShowDetectedInFullRange = spawn.RadarShowDetectedInFullRange;
        entity.PersistDetectedBeyondSensorRange = spawn.PersistDetectedBeyondSensorRange;
        if (spawn.Discovery == DiscoveryState.Scanned)
            entity.ScanProgress = 100;

        _currentSector.Entities.Add(entity);
        _levelGenerator.AppendStaticEntity(entity);
        _entityLookup[entity.Id] = entity;

        var contact = MissionGenerator.CreateContactFromEntity(entity, _currentSector);
        _state.Contacts.Add(contact);

        _broadcast.BroadcastStateUpdates();
        GD.Print($"[MissionOrchestrator] Runtime POI spawned: {entity.DisplayName} ({spawn.AssetId}) at map ({spawnX:F0},{spawnY:F0},{spawnZ:F0}).");
        return true;
    }

    /// <summary>Briefing text that will be used when this node’s mission starts (run-map preview before confirm).</summary>
    public static string GetBriefingPreviewForRunNode(RunNodeData data)
    {
        BuildEncounterForRunNode(data, out var enc, out _, out _);
        return enc.BriefingText;
    }

    private static void BuildEncounterForRunNode(RunNodeData data,
        out NodeEncounterConfig enc, out string biomeForSector, out MissionScript? script)
    {
        biomeForSector = BiomeForRunNode(data);
        int risk = Math.Max(1, data.RiskRating);
        enc = NodeEncounterConfig.FromRunNodeType(data.Type, biomeForSector, risk);
        script = null;

        if (string.IsNullOrEmpty(data.AssignedMissionId))
            return;

        script = MissionScriptCatalog.GetOrNull(data.AssignedMissionId);
        if (script != null)
        {
            if (script.LevelRadiusMultiplier.HasValue)
                enc.LevelRadiusMultiplier = script.LevelRadiusMultiplier.Value;
            if (!string.IsNullOrEmpty(script.BiomeId))
                biomeForSector = script.BiomeId;
        }

        if (MissionTemplateCatalog.TryGet(data.AssignedMissionId, out var tpl) && tpl != null)
        {
            enc.MissionTitle = tpl.Title;
            enc.BriefingText = MissionTemplateCatalog.BuildBriefing(tpl);
        }
    }

    public void BeginLevelAndMissionForRunNode(string nodeId, RunController runController)
    {
        if (_state.MissionStarted) return;
        if (!runController.CurrentDefinition.Nodes.TryGetValue(nodeId, out var data))
            return;

        // ── M4: Pre-Sector event gate ───────────────────────────────────────
        // When a catalog event fires as PreSector, we defer sector generation
        // entirely. Captain/Nav gets a decision overlay; other roles see a
        // "Funkspruch" banner until it's resolved via <see cref="ResolvePreSectorDecision"/>.
        if (TryStartPreSectorEvent(data, runController, nodeId))
            return;

        BeginLevelAndMissionForRunNodeCore(nodeId, data, runController);
    }

    private void BeginLevelAndMissionForRunNodeCore(string nodeId, RunNodeData data, RunController runController)
    {
        _state.ShowRunMapOnMainScreen = false;

        int levelSeed = RunSeed.DeriveLevelSeed(runController.CurrentRun.CampaignSeed, nodeId);

        // 1. Config + Script FIRST
        BuildEncounterForRunNode(data, out var enc, out var biome, out var script);

        // Director-Hook: only when no script-supplied AgentOverrides exist (authoring takes priority).
        var agentOverrides = script?.AgentOverrides;
        if (agentOverrides == null)
        {
            var baseProfiles = AgentSpawnConfig.GetProfiles(biome, data.Type);
            var ctx = _directorCtxProvider?.Invoke();
            if (_director != null && ctx != null)
                agentOverrides = _director.AdjustSpawnProfiles(ctx, data, biome, baseProfiles);
        }

        // 2. Sector with effective radius + agent overrides + guaranteed mission markers
        GenerateSectorAndBuild(levelSeed, biome, data.Type, enc.LevelRadiusMultiplier,
            agentOverrides, script?.MissionMarkers, script);

        // 3. Apply config + script, then start
        _missionController.ApplyEncounterConfig(enc);
        _missionController.ApplyMissionScript(script);
        _state.Mission.MissionTitle = enc.MissionTitle;
        _state.Mission.BriefingText = enc.BriefingText;
        _state.Mission.MissionId = string.IsNullOrEmpty(data.AssignedMissionId)
            ? $"{runController.CurrentDefinition.Id}_{nodeId}"
            : data.AssignedMissionId;
        _state.Mission.NodeRiskRating = data.RiskRating;

        // M5: Station sectors get a seeded dock inventory. Other sectors keep Dock == null.
        _state.Mission.Dock = enc.IsStation ? StationInventory.Build(nodeId) : null;
        _state.Mission.Docked = false;
        _state.Mission.DockedContactId = null;

        _missionController.StartMission();

        // ── Director: per-Sektor In-Sector-Wellen-State zuruecksetzen + Hooks binden ─────
        WireDirectorInSectorHooks(data, script);

        // ── M4: In-Sector event injection ───────────────────────────────────
        TryInjectInSectorEvent(data, runController, biome);

        _broadcast.BroadcastMissionStarted();
    }

    /// <summary>
    /// Resets the per-sector wave counters on <see cref="PacingState"/> and binds the director's
    /// in-sector heartbeat / hostile-destroyed hooks to the active <see cref="MissionController"/>.
    /// Honors <see cref="MissionScript.DisableDirectorWaves"/> as the authoring escape-hatch.
    /// </summary>
    private void WireDirectorInSectorHooks(RunNodeData data, MissionScript? script)
    {
        // Default: clear stale hooks so a previous sector's binding never leaks across.
        _missionController.OnHeartbeatHook = null;
        _missionController.OnHostileDestroyedHook = null;

        if (_director == null) return;
        var ctx = _directorCtxProvider?.Invoke();
        if (ctx == null) return;

        // Per-sector reset (new sector = fresh wave budget, fresh heartbeat clock).
        var pacing = ctx.Pacing;
        pacing.InSectorWaveCount = 0;
        pacing.LastWaveAtElapsed = -1f;
        pacing.LastWaveReason = "";
        pacing.NextHeartbeatAtElapsed = SpacedOut.Run.EscalatingDirector.HeartbeatIntervalSec;

        if (script?.DisableDirectorWaves == true)
        {
            GD.Print($"[MissionOrchestrator] Director waves disabled by script for node {data.Id}.");
            return;
        }

        var director = _director;
        var capturedCtx = ctx;
        var capturedNode = data;
        _missionController.OnHeartbeatHook = dt => director.TickInSector(capturedCtx, _missionController, capturedNode, dt);
        _missionController.OnHostileDestroyedHook = id => director.OnHostileDestroyed(capturedCtx, _missionController, capturedNode, id);
    }

    /// <summary>
    /// Hybrid pick: catalog filters candidates + firing roll, director (if set) picks against the
    /// pacing/threat-pool. Falls back to the catalog's RNG pick when no director is wired.
    /// </summary>
    private NodeEvent? PickEventViaDirector(
        RunNodeData data,
        string biome,
        int campaignSeed,
        HashSet<string> alreadyFired,
        NodeEventTrigger trigger)
    {
        if (!NodeEventCatalog.NodeCarriesEvents(data.Type))
            return null;

        // Honor authored firing probability — Director only chooses among pre-rolled candidates.
        if (!NodeEventCatalog.RollFiring(data, campaignSeed))
            return null;

        var candidates = NodeEventCatalog.GetCandidates(data, biome, alreadyFired,
            _meta?.Profile.UnlockedIds, trigger);
        if (candidates.Count == 0)
            return null;

        var ctx = _directorCtxProvider?.Invoke();
        if (_director != null && ctx != null)
            return _director.PickEvent(ctx, data, trigger, candidates);

        // Fallback: deterministic catalog RNG (preserves legacy behavior).
        int seed = unchecked(campaignSeed ^ data.Id.GetHashCode());
        var rng = new Random(seed);
        return candidates[rng.Next(candidates.Count)];
    }

    /// <summary>
    /// Picks and stages a pre-sector <see cref="NodeEvent"/>, if eligible. Returns true when
    /// sector generation was deferred — the caller must not continue the mission start path.
    /// </summary>
    private bool TryStartPreSectorEvent(RunNodeData data, RunController runController, string nodeId)
    {
        var runState = _state.ActiveRunState;
        if (runState == null) return false;

        string biome = BiomeForRunNode(data);
        var evt = PickEventViaDirector(data, biome, runController.CurrentRun.CampaignSeed,
            runState.FiredEventIds, NodeEventTrigger.PreSector);
        if (evt == null || evt.Trigger != NodeEventTrigger.PreSector) return false;

        runState.FiredEventIds.Add(evt.Id);

        _state.Mission.PreSectorEventActive = true;
        _state.Mission.PendingPreSectorEventId = evt.Id;
        _state.Mission.PendingPreSectorEventTitle = evt.Title;
        _state.Mission.PendingPreSectorNodeId = nodeId;
        _state.ShowRunMapOnMainScreen = true;

        _state.AddPendingDecision(new MissionDecision
        {
            Id = CaptainNavCommandHandler.PreSectorDecisionId(evt.Id),
            Title = evt.Title,
            Description = evt.Description,
            Options = evt.Options,
        });

        GD.Print($"[MissionOrchestrator] Pre-Sector Event: {evt.Id} ({evt.Title}) at node {nodeId}");
        return true;
    }

    /// <summary>
    /// Resolves a pre-sector event. Must be invoked from <see cref="Commands.CommandProcessor"/>'s
    /// <c>PreSectorDecisionResolved</c> signal. Either skips the node (resolving it as Success)
    /// or continues the original mission start path.
    /// </summary>
    public void ResolvePreSectorDecision(string nodeId, string eventId, bool skipSector, RunOrchestrator runOrch)
    {
        if (string.IsNullOrEmpty(nodeId)) return;
        if (!runOrch.Controller.CurrentDefinition.Nodes.TryGetValue(nodeId, out var data)) return;

        if (skipSector)
        {
            runOrch.Controller.ResolveNode(nodeId, NodeResolution.Success);
            runOrch.SyncRunToState();
            runOrch.BroadcastRunState();
            _broadcast.BroadcastStateUpdates();
            GD.Print($"[MissionOrchestrator] Pre-Sector Skip: node {nodeId} ({eventId}) → Success without combat");
            return;
        }

        BeginLevelAndMissionForRunNodeCore(nodeId, data, runOrch.Controller);
    }

    /// <summary>
    /// Picks an In-Sector <see cref="NodeEvent"/> and injects a synthetic trigger + decision
    /// into the active <see cref="MissionController"/> via its runtime-registration API.
    /// Safe to call right after <see cref="MissionController.StartMission"/>.
    /// </summary>
    private void TryInjectInSectorEvent(RunNodeData data, RunController runController, string biome)
    {
        var runState = _state.ActiveRunState;
        if (runState == null) return;

        var evt = PickEventViaDirector(data, biome, runController.CurrentRun.CampaignSeed,
            runState.FiredEventIds, NodeEventTrigger.InSector);
        if (evt == null || evt.Trigger != NodeEventTrigger.InSector) return;

        runState.FiredEventIds.Add(evt.Id);

        string decisionId = $"event:{evt.Id}";
        string synthEventId = $"event_banner:{evt.Id}";

        _missionController.RegisterRuntimeDecision(decisionId, new ScriptedDecision
        {
            Title = evt.Title,
            Description = evt.Description,
            Options = evt.Options,
        });

        _missionController.RegisterRuntimeEvent(synthEventId, new ScriptedEvent
        {
            Title = evt.Title,
            Description = evt.Description,
            Duration = 30f,
            DecisionId = decisionId,
            LogEntry = $"Funkspruch: {evt.Title}",
            ShowOnMainScreen = true,
        });

        switch (evt.InSectorTrigger.Kind)
        {
            case NodeEventInSectorKind.AtTime:
                _missionController.AddRuntimeTimeTrigger(new TimeTrigger
                {
                    EventId = synthEventId,
                    Time = evt.InSectorTrigger.TimeSeconds,
                    Once = true,
                });
                break;
            case NodeEventInSectorKind.ProximityToMapCenter:
                _missionController.AddRuntimeProximityTrigger(new ProximityTrigger
                {
                    EventId = synthEventId,
                    Ref = TriggerRef.MapCenter,
                    Radius = evt.InSectorTrigger.ProximityRadius,
                    Once = true,
                });
                break;
            default:
                _missionController.AddRuntimeProximityTrigger(new ProximityTrigger
                {
                    EventId = synthEventId,
                    Ref = evt.InSectorTrigger.ProximityRef,
                    Radius = evt.InSectorTrigger.ProximityRadius,
                    Once = true,
                });
                break;
        }

        GD.Print($"[MissionOrchestrator] In-Sector Event injected: {evt.Id} ({evt.InSectorTrigger.Kind})");
    }

    /// <summary>
    /// Update per-frame: sync runtime contact state back to sector entities,
    /// update 3D marker positions and visibility, sync pin brackets.
    /// </summary>
    public void UpdateMarkers()
    {
        if (_currentSector == null || _markers == null) return;

        PrunePinsForDestroyedContacts();
        SyncContactsToSector();
        _markers.UpdateVisibility();
        SyncPinBrackets();
        UpdatePinnedBracketPositions();
    }

    private void PrunePinsForDestroyedContacts()
    {
        _state.PinnedEntities.RemoveAll(p =>
            _state.Contacts.Find(c => c.Id == p.EntityId) is { IsDestroyed: true });
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
                    PersistDetectedBeyondSensorRange = contact.PersistDetectedBeyondSensorRange,
                    // Propagate the agent archetype so Sector3DMarkers can load the
                    // ship mesh from AgentDefinition.ScenePaths. Without this,
                    // deferred spawns (pirates, corsairs) stay as plain HUD blips.
                    AgentTypeId = contact.Agent?.AgentType ?? "",
                };
                _currentSector.Entities.Add(entity);
                _entityLookup[contact.Id] = entity;
                _markers?.AddDynamicMarker(entity);
            }

            entity.IsDestroyed = contact.IsDestroyed;
            entity.WorldPosition = CoordinateMapper.MapToWorld(
                contact.PositionX, contact.PositionY, contact.PositionZ, lr);
            entity.Velocity = contact.IsDestroyed
                ? Vector3.Zero
                : new Vector3(contact.VelocityX, contact.VelocityZ, contact.VelocityY);
            entity.Discovery = contact.Discovery;
            // Tutorial exit: keep off radar/3D until JumpCoordinatesUnlocked (contact may briefly go Probed from bugs).
            if (contact.Id == "sector_exit" && !_state.Mission.JumpCoordinatesUnlocked
                && contact.Discovery != DiscoveryState.Scanned)
                entity.Discovery = DiscoveryState.Hidden;
            entity.ScanProgress = contact.ScanProgress;
            entity.DisplayName = contact.DisplayName;
            entity.ThreatLevel = contact.ThreatLevel;
            entity.RadarShowDetectedInFullRange = contact.RadarShowDetectedInFullRange;
            entity.PersistDetectedBeyondSensorRange = contact.PersistDetectedBeyondSensorRange;
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
    /// Maps a tactical contact id to sector data. The primary objective uses <see cref="PrimaryTargetContactId"/>
    /// while <see cref="SectorEntity.Id"/> stays a generated instance id.
    /// </summary>
    private static SectorEntity? ResolveEntityForContactId(SectorData sector, string contactEntityId)
    {
        if (contactEntityId == PrimaryTargetContactId)
            return sector.Entities.Find(e => e.IsPrimaryObjective);
        return sector.Entities.Find(e => e.Id == contactEntityId);
    }

    private void GenerateSectorAndBuild(int seed, string biomeId, RunNodeType? nodeType = null,
        float radiusMultiplier = 1f, List<AgentSpawnProfile>? agentOverrides = null,
        IReadOnlyList<MissionMarkerPlacement>? missionMarkers = null, MissionScript? missionScript = null)
    {
        _lastSectorNodeType = nodeType;
        _lastSectorAgentOverrides = agentOverrides;
        _lastSectorMissionMarkers = missionMarkers;
        _lastSectorMissionScript = missionScript;

        _currentSector = _sectorGenerator.Generate(seed, biomeId, nodeType, radiusMultiplier, agentOverrides,
            missionMarkers, missionScript?.PrimaryObjective, missionScript?.DisableBiomeLandmark ?? false);

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

        // MissionGenerator renames primary-objective entities to "primary_target"
        var primary = _currentSector.Entities.Find(e => e.IsPrimaryObjective);
        if (primary != null)
            _entityLookup.TryAdd(PrimaryTargetContactId, primary);
    }

    private static string BiomeForRunNode(RunNodeData data) => data.Type switch
    {
        RunNodeType.Station or RunNodeType.End => "station_periphery",
        RunNodeType.Hostile => "wreck_zone",
        _ => "asteroid_field",
    };
}
