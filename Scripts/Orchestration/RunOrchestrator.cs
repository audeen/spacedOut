using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Godot;
using SpacedOut.LevelGen;
using SpacedOut.Meta;
using SpacedOut.Mission;
using SpacedOut.Network;
using SpacedOut.Run;
using SpacedOut.State;

namespace SpacedOut.Orchestration;

public class RunOrchestrator
{
    private readonly RunController _runController;
    private readonly GameState _state;
    private readonly GameServer _server;
    private readonly BroadcastService _broadcast;
    private MetaProgressService? _meta;

    private IRunDirector? _director;
    private RunDirectorContext? _directorCtx;
    private MissionOrchestrator? _missionOrch;

    public RunController Controller => _runController;

    /// <summary>Profile available to event filtering / unlock checks (null = no meta progression yet).</summary>
    public MetaProfile? Profile => _meta?.Profile;

    /// <summary>Active run director (null between runs). Exposed for wiring into <see cref="MissionOrchestrator"/>.</summary>
    public IRunDirector? Director => _director;

    /// <summary>Active director context (null between runs). Exposed for <see cref="MissionOrchestrator.SetDirector"/>.</summary>
    public RunDirectorContext? DirectorContext => _directorCtx;

    public RunOrchestrator(
        RunController runController,
        GameState state,
        GameServer server,
        BroadcastService broadcast)
    {
        _runController = runController;
        _state = state;
        _server = server;
        _broadcast = broadcast;
    }

    /// <summary>Wires the persistent meta-progression profile (M7).</summary>
    public void SetMetaProgress(MetaProgressService meta)
    {
        _meta = meta;
    }

    /// <summary>
    /// Wires the mission orchestrator so the director created in <see cref="StartRun"/> can be
    /// pushed into <see cref="MissionOrchestrator.SetDirector"/> for event/spawn budgeting.
    /// </summary>
    public void SetMissionOrchestrator(MissionOrchestrator missionOrch)
    {
        _missionOrch = missionOrch;
    }

    public void StartRun(int? campaignSeed = null, MissionController? missionController = null,
        string? perkId = null)
    {
        ResetStateForNewRun(missionController);

        int seed = campaignSeed ?? RunSeed.CreateNewCampaignSeed();
        // Scripted tutorial (tutorial_blindsprung) disabled — START uses GenericPool like procedural runs.
        _director = new EscalatingDirector();
        var def = new RunGenerator().GenerateRun(seed, skipTutorial: true, director: _director);
        _runController.StartRun(def, seed);

        // Reactive director context binds the *running* RunStateData and the controller's scan-range
        // helper so AdjustUpcomingNodes never touches nodes the player can already scan.
        _directorCtx = new RunDirectorContext(
            _runController.CurrentDefinition,
            _runController.CurrentRun,
            new Random(seed ^ 0x5F375A86),
            _runController.IsWithinScanRange);

        // Seed the threat pool so the first node already has budget for catalog events.
        _runController.CurrentRun.Pacing.ThreatCapacity = 8f;
        _runController.CurrentRun.Pacing.ThreatPool = 4f;

        // Hand the director (and its context) to MissionOrchestrator so PreSector/InSector
        // event picks and AgentSpawnConfig overrides go through the pacing pool.
        _missionOrch?.SetDirector(_director, () => _directorCtx);
        _runController.OnNodeEnteredHook = nodeId => _director?.OnNodeEntered(_directorCtx!, nodeId);
        _runController.OnNodeResolvedHook = (nodeId, res) =>
        {
            if (_director == null || _directorCtx == null) return;
            _director.OnNodeResolved(_directorCtx, nodeId, res);
            var changed = _director.AdjustUpcomingNodes(_directorCtx);
            if (changed.Count > 0)
            {
                _runController.ReevaluateNodeStates();
                SyncRunToState();
                BroadcastRunState();
            }
        };

        // M7: apply selected perk + passive unlocks to the freshly-initialized RunStateData.
        if (_meta != null)
        {
            UnlockApplier.ApplyToRunStart(_runController.CurrentRun, _meta.Profile, perkId);
            _meta.SetSelectedPerk(perkId);
        }

        _state.RunActive = true;
        _state.RunOutcome = RunOutcome.Ongoing;
        _state.ShowMainMenu = false;
        _state.ShowRunMapOnMainScreen = true;
        _state.Run.RunsStartedThisSession++;
        _state.LastRunStardustGain = 0;
        _state.ActivePerkId = perkId;
        var perkDef = perkId != null ? UnlockCatalog.GetById(perkId) : null;
        _state.ActivePerkName = perkDef?.Name ?? "";

        SyncRunToState();
        BroadcastRunState();
        GD.Print($"[RunOrchestrator] Run gestartet – CampaignSeed={_runController.CurrentRun.CampaignSeed}" +
            (perkId != null ? $" · Perk: {perkDef?.Name ?? perkId}" : ""));
    }

    /// <summary>Clears mission/ship/contact state so a new run always starts fresh,
    /// regardless of whether the previous one ended in Victory/Defeat or was aborted.</summary>
    private void ResetStateForNewRun(MissionController? missionController)
    {
        missionController?.ResetMission();
        _state.RunOutcome = RunOutcome.Ongoing;
        _state.RunActive = false;
        _state.Run.StrandedDefeat = false;
        _state.ActiveRunState = null;
        _state.ActiveRunDefinition = null;
    }

    public void SyncRunToState()
    {
        if (!_state.RunActive) return;
        _state.ActiveRunState = _runController.CurrentRun;
        _state.ActiveRunDefinition = _runController.CurrentDefinition;
    }

    public void SelectRunNode(string nodeId, MissionOrchestrator missionOrch)
    {
        if (!_state.RunActive) return;
        if (_state.MissionStarted)
        {
            GD.PrintErr("[RunOrchestrator] Mission läuft – neuer Knoten erst nach Abschluss.");
            return;
        }

        if (!_runController.CanAffordNodeEntry(nodeId))
        {
            GD.Print($"[RunOrchestrator] Knoten {nodeId} nicht betretbar (nicht reachable, branch-locked oder zu wenig Fuel).");
            return;
        }

        if (!_runController.CurrentDefinition.Nodes.TryGetValue(nodeId, out var nodeData))
            return;

        int fuelCost = NodeEncounterConfig.GetFuelCostFor(nodeData.Type);
        if (fuelCost > 0)
        {
            var run = _runController.CurrentRun;
            run.Resources.TryGetValue(RunResourceIds.Fuel, out int fuel);
            int next = Math.Max(0, fuel - fuelCost);
            run.Resources[RunResourceIds.Fuel] = next;
            GD.Print($"[RunOrchestrator] Sprung: -{fuelCost} Fuel (rest: {next}).");
        }

        _runController.EnterNode(nodeId);
        missionOrch.BeginLevelAndMissionForRunNode(nodeId, _runController);
        SyncRunToState();
        BroadcastRunState();
        _broadcast.BroadcastStateUpdates();
    }

    /// <summary>Spends ScienceData and marks the target run node as Scanned (M6 scan rework).</summary>
    public void ScanRunNode(string nodeId)
    {
        if (!_state.RunActive) return;
        if (string.IsNullOrEmpty(nodeId)) return;

        if (!_runController.ScanNode(nodeId))
        {
            GD.Print($"[RunOrchestrator] Scan abgelehnt für Knoten '{nodeId}' (bereits gescannt oder zu wenig ScienceData).");
            return;
        }

        SyncRunToState();
        BroadcastRunState();
        _broadcast.BroadcastStateUpdates();
    }

    public void OnMissionEnded(MissionResult result, MainScreen.HudOverlay? hud)
    {
        _state.ShowRunMapOnMainScreen = true;
        var activeNodeId = _runController.CurrentRun.CurrentNodeId;
        bool endedOnEndNode = false;

        if (!string.IsNullOrEmpty(activeNodeId))
        {
            if (_runController.CurrentDefinition.Nodes.TryGetValue(activeNodeId, out var nd))
                endedOnEndNode = nd.Type == RunNodeType.End;

            _runController.CurrentRun.CurrentHull = _state.Ship.HullIntegrity;

            _runController.ResolveNode(activeNodeId, MapMissionResultToNodeResolution(result));
            SyncRunToState();
        }

        // Classify run outcome. Destroyed trumps anything else; END+Success ends the run as victory.
        if (result == MissionResult.Destroyed)
        {
            _state.RunOutcome = RunOutcome.Defeat;
            _state.RunActive = false;
            _state.Run.StrandedDefeat = false;
            GD.Print("[RunOrchestrator] Run verloren \u2014 Schiff zerst\u00f6rt.");
        }
        else if (endedOnEndNode && result == MissionResult.Success)
        {
            _state.RunOutcome = RunOutcome.Victory;
            _state.RunActive = false;
            _state.Run.StrandedDefeat = false;
            GD.Print("[RunOrchestrator] Run gewonnen \u2014 END-Knoten erreicht.");
        }
        else if (_state.RunOutcome == RunOutcome.Ongoing
                 && result != MissionResult.Destroyed
                 && !(endedOnEndNode && result == MissionResult.Success)
                 && !_runController.HasAnyAffordableReachableNode())
        {
            _state.RunOutcome = RunOutcome.Defeat;
            _state.RunActive = false;
            _state.Run.StrandedDefeat = true;
            GD.Print("[RunOrchestrator] Run verloren \u2014 gestrandet (kein erreichbarer Knoten mehr leistbar).");
        }

        // M7: grant Sternenstaub once the run finalised this tick.
        if (_state.RunOutcome != RunOutcome.Ongoing && _meta != null)
        {
            int gained = StardustCalculator.Calculate(
                _runController.CurrentRun, _state.RunOutcome, _state.Run.StrandedDefeat);
            _state.LastRunStardustGain = gained;
            _meta.GrantStardust(gained);
            _meta.Profile.RunsCompleted++;
            _meta.Save();
            GD.Print($"[RunOrchestrator] Sternenstaub gutgeschrieben: +{gained} (Gesamt {_meta.Profile.Stardust}, Runs {_meta.Profile.RunsCompleted})");
        }

        BroadcastRunState();
        hud?.UpdateRunUi(_runController);
    }

    public void ResolveFromNetwork(string nodeId, string resolutionStr, MainScreen.HudOverlay? hud)
    {
        if (_state.MissionStarted) return;
        if (!Enum.TryParse<NodeResolution>(resolutionStr, true, out var res)) return;
        _runController.ResolveNode(nodeId, res);
        SyncRunToState();
        BroadcastRunState();
        _broadcast.BroadcastStateUpdates();
        hud?.UpdateRunUi(_runController);
    }

    public void ResolveFromHud(int resolution, MainScreen.HudOverlay? hud)
    {
        if (_state.MissionStarted) return;
        var id = _runController.CurrentRun.CurrentNodeId;
        if (string.IsNullOrEmpty(id)) return;
        _runController.ResolveNode(id, (NodeResolution)resolution);
        SyncRunToState();
        BroadcastRunState();
        _broadcast.BroadcastStateUpdates();
        hud?.UpdateRunUi(_runController);
    }

    public void BroadcastRunState()
    {
        SyncRunToState();
        _server.BroadcastToAll(CreateRunStateMessage());
    }

    public void SendRunStateToClient(string clientId)
    {
        _server.SendToClient(clientId, CreateRunStateMessage());
    }

    private string CreateRunStateMessage()
    {
        SyncRunToState();
        return JsonSerializer.Serialize(new
        {
            type = "run_state_update",
            data = BuildRunPayload(_runController),
        });
    }

    private object BuildRunPayload(RunController run)
    {
        var st = run.CurrentRun;
        var def = run.CurrentDefinition;
        return new
        {
            run_id = st.RunId,
            campaign_seed = st.CampaignSeed,
            definition_id = def.Id,
            start_node_id = def.StartNodeId,
            current_node_id = st.CurrentNodeId,
            outcome = _state.RunOutcome.ToString(),
            current_depth = st.CurrentDepth,
            resources = st.Resources,
            flags = st.Flags,
            visited = st.VisitedNodeIds,
            last_resolved = st.LastResolvedNodeId,
            scan_cost = RunController.ScanCostScience,
            perk_id = _state.ActivePerkId ?? "",
            perk_name = _state.ActivePerkName ?? "",
            nodes = def.Nodes.Values.Select(n =>
            {
                var rt = st.NodeStates[n.Id];
                var k = rt.Knowledge;
                bool sighted = k != NodeKnowledgeState.Silhouette;
                bool scanned = k == NodeKnowledgeState.Scanned;
                return new
                {
                    n.Id,
                    Title = sighted ? n.Title : "",
                    type = sighted ? n.Type.ToString() : "?",
                    n.Depth,
                    RiskRating = sighted ? n.RiskRating : 0,
                    n.NextNodeIds,
                    layout_x = n.LayoutX,
                    layout_y = n.LayoutY,
                    state = rt.State.ToString(),
                    resolution = rt.Resolution.ToString(),
                    knowledge = k.ToString(),
                    scannable = run.IsWithinScanRange(n.Id),
                    fuel_cost = NodeEncounterConfig.GetFuelCostFor(n.Type),
                    briefing_preview = scanned
                        ? MissionOrchestrator.GetBriefingPreviewForRunNode(n)
                        : "",
                };
            }).ToList(),
        };
    }

    private static NodeResolution MapMissionResultToNodeResolution(MissionResult result) =>
        result switch
        {
            MissionResult.Success => NodeResolution.Success,
            MissionResult.Partial => NodeResolution.PartialSuccess,
            _ => NodeResolution.Failure,
        };
}
