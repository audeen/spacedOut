using System;
using System.Collections.Generic;
using System.Text.Json;
using Godot;
using SpacedOut.Commands.Handlers;
using SpacedOut.Mission;
using SpacedOut.Network;
using SpacedOut.Orchestration;
using SpacedOut.Sector;
using SpacedOut.State;

namespace SpacedOut.Commands;

public partial class CommandProcessor : Node, ICommandContext
{
    private GameState _state = null!;
    private GameServer _server = null!;
    private SectorData? _sectorData;
    private MissionController? _missionController;
    private MissionOrchestrator? _missionOrchestrator;
    private BroadcastService? _broadcastService;

    [Signal] public delegate void StateChangedEventHandler();
    [Signal] public delegate void MissionLogAddedEventHandler(string source, string message);
    [Signal] public delegate void NodeSelectedEventHandler(string nodeId);
    [Signal] public delegate void RunResolveRequestedEventHandler(string nodeId, string resolution);
    [Signal] public delegate void ScanRunNodeRequestedEventHandler(string nodeId);
    [Signal] public delegate void PreSectorDecisionResolvedEventHandler(string nodeId, string eventId, bool skipSector);

    private CaptainNavCommandHandler _captainNavHandler = null!;
    private EngineerCommandHandler _engineerHandler = null!;
    private TacticalCommandHandler _tacticalHandler = null!;
    private GunnerCommandHandler _gunnerHandler = null!;
    private CommonCommandHandler _commonHandler = null!;
    private DockCommandHandler _dockHandler = null!;

    GameState ICommandContext.State => _state;
    SectorData? ICommandContext.CurrentSector => _sectorData;
    MissionController? ICommandContext.MissionController => _missionController;
    MissionOrchestrator? ICommandContext.Orchestrator => _missionOrchestrator;

    Action<string>? ICommandContext.OnNodeSelected { get; set; }
    Action<string, string>? ICommandContext.OnRunResolveRequested { get; set; }
    Action<string>? ICommandContext.OnScanRunNodeRequested { get; set; }
    Action<string, string, bool>? ICommandContext.OnPreSectorDecisionResolved { get; set; }

    public void SetSectorData(SectorData? data) => _sectorData = data;
    public void SetMissionController(MissionController controller) => _missionController = controller;
    public void SetMissionOrchestrator(MissionOrchestrator orchestrator) => _missionOrchestrator = orchestrator;
    public void SetBroadcastService(BroadcastService broadcast) => _broadcastService = broadcast;

    public void Initialize(GameState state, GameServer server)
    {
        _state = state;
        _server = server;

        _captainNavHandler = new CaptainNavCommandHandler(this);
        _engineerHandler = new EngineerCommandHandler(this);
        _tacticalHandler = new TacticalCommandHandler(this);
        _gunnerHandler = new GunnerCommandHandler(this);
        _commonHandler = new CommonCommandHandler(this);
        _dockHandler = new DockCommandHandler(this);

        ((ICommandContext)this).OnNodeSelected = nodeId =>
            EmitSignal(SignalName.NodeSelected, nodeId);
        ((ICommandContext)this).OnRunResolveRequested = (nodeId, resolution) =>
            EmitSignal(SignalName.RunResolveRequested, nodeId, resolution);
        ((ICommandContext)this).OnScanRunNodeRequested = nodeId =>
            EmitSignal(SignalName.ScanRunNodeRequested, nodeId);
        ((ICommandContext)this).OnPreSectorDecisionResolved = (nodeId, eventId, skipSector) =>
            EmitSignal(SignalName.PreSectorDecisionResolved, nodeId, eventId, skipSector);
    }

    public bool ProcessCommand(string clientId, string messageJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(messageJson);
            var root = doc.RootElement;

            string command = root.GetProperty("command").GetString() ?? "";
            var data = root.TryGetProperty("data", out var d) ? d : default;

            var client = _server.Clients.GetValueOrDefault(clientId);
            if (client?.Role == null)
            {
                GD.PrintErr($"[CommandProcessor] No role for client {clientId}");
                return false;
            }

            var role = client.Role.Value;
            GD.Print($"[CommandProcessor] {role} -> {command}");

            if (_commonHandler.Handle(command, role, data))
                return true;

            if (_dockHandler.Handle(command, role, data))
                return true;

            return role switch
            {
                StationRole.CaptainNav => _captainNavHandler.Handle(command, data, role),
                StationRole.Engineer => _engineerHandler.Handle(command, role, data),
                StationRole.Tactical => _tacticalHandler.Handle(command, role, data),
                StationRole.Gunner => _gunnerHandler.Handle(command, role, data),
                _ => false,
            };
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[CommandProcessor] Error: {ex.Message}");
            return false;
        }
    }

    void ICommandContext.AddLog(string source, string message)
    {
        if (string.IsNullOrWhiteSpace(message)) return;
        _state.AddMissionLogEntry(new MissionLogEntry
        {
            Timestamp = _state.Mission.ElapsedTime,
            Source = source,
            Message = message.Trim(),
        });
        EmitSignal(SignalName.MissionLogAdded, source, message);
    }

    void ICommandContext.EmitStateChanged()
    {
        EmitSignal(SignalName.StateChanged);
    }

    void ICommandContext.NotifyDecisionResolved(string narrative, string effectsLine, string? decisionTitle, string? optionLabel, bool cinematicResolution)
    {
        _broadcastService?.SendDecisionResolvedToCaptainNav(narrative, effectsLine, decisionTitle, optionLabel, cinematicResolution);
    }
}
