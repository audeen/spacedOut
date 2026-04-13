using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Godot;
using SpacedOut.LevelGen;
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

    public RunController Controller => _runController;

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

    public void StartRun(int? campaignSeed = null)
    {
        int seed = campaignSeed ?? RunSeed.CreateNewCampaignSeed();
        var def = new RunGenerator().GenerateRun(seed);
        _runController.StartRun(def, seed);
        _state.RunActive = true;
        _state.ShowRunMapOnMainScreen = true;
        SyncRunToState();
        BroadcastRunState();
        GD.Print($"[RunOrchestrator] Run gestartet – CampaignSeed={_runController.CurrentRun.CampaignSeed}");
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

        if (_runController.CanEnterNode(nodeId))
        {
            _runController.EnterNode(nodeId);
            missionOrch.BeginLevelAndMissionForRunNode(nodeId, _runController);
            SyncRunToState();
            BroadcastRunState();
            _broadcast.BroadcastStateUpdates();
        }
    }

    public void OnMissionEnded(MissionResult result, MainScreen.HudOverlay? hud)
    {
        _state.ShowRunMapOnMainScreen = true;
        var activeNodeId = _runController.CurrentRun.CurrentNodeId;
        if (!string.IsNullOrEmpty(activeNodeId))
        {
            _runController.ResolveNode(activeNodeId, MapMissionResultToNodeResolution(result));
            SyncRunToState();
            BroadcastRunState();
        }

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

    private static object BuildRunPayload(RunController run)
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
            current_depth = st.CurrentDepth,
            resources = st.Resources,
            flags = st.Flags,
            visited = st.VisitedNodeIds,
            last_resolved = st.LastResolvedNodeId,
            nodes = def.Nodes.Values.Select(n => new
            {
                n.Id,
                n.Title,
                type = n.Type.ToString(),
                n.Depth,
                n.RiskRating,
                n.NextNodeIds,
                layout_x = n.LayoutX,
                layout_y = n.LayoutY,
                state = st.NodeStates[n.Id].State.ToString(),
                resolution = st.NodeStates[n.Id].Resolution.ToString(),
                knowledge = st.NodeStates[n.Id].Knowledge.ToString(),
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
