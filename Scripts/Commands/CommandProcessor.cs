using System;
using System.Collections.Generic;
using System.Text.Json;
using Godot;
using SpacedOut.Network;
using SpacedOut.State;

namespace SpacedOut.Commands;

public partial class CommandProcessor : Node
{
    private GameState _state = null!;
    private GameServer _server = null!;

    [Signal] public delegate void StateChangedEventHandler();
    [Signal] public delegate void OverlayRequestedEventHandler(string overlayJson);
    [Signal] public delegate void MissionLogAddedEventHandler(string source, string message);
    [Signal] public delegate void NodeSelectedEventHandler(string nodeId);

    public void Initialize(GameState state, GameServer server)
    {
        _state = state;
        _server = server;
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

            return command switch
            {
                // Captain
                "ApproveOverlay" => HandleApproveOverlay(data),
                "DismissOverlay" => HandleDismissOverlay(data),
                "SetMissionPriority" => HandleSetMissionPriority(data),
                "ResolveDecision" => HandleResolveDecision(data),
                "RequestStatus" => HandleRequestStatus(role, data),

                // Campaign (any role)
                "SelectNode" => HandleSelectNode(data),

                // Navigator
                "SetWaypoint" => HandleSetWaypoint(data),
                "RemoveWaypoint" => HandleRemoveWaypoint(data),
                "ChangeFlightMode" => HandleChangeFlightMode(data),
                "HighlightRoute" => HandleHighlightRoute(data),
                "ToggleStarMapOnMainScreen" => HandleToggleStarMap(),

                // Engineer
                "SetEnergyDistribution" => HandleSetEnergyDistribution(data),
                "StartRepair" => HandleStartRepair(data),
                "TriggerEmergencyShutdown" => HandleEmergencyShutdown(data),
                "RaiseSystemWarning" => HandleRaiseWarning(role, data),

                // Tactical
                "ScanContact" => HandleScanContact(data),
                "MarkContact" => HandleMarkContact(data),
                "SetThreatPriority" => HandleSetThreatPriority(data),
                "RaiseTacticalWarning" => HandleRaiseWarning(role, data),
                "ToggleTacticalOnMainScreen" => HandleToggleTactical(),

                _ => false
            };
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[CommandProcessor] Error: {ex.Message}");
            return false;
        }
    }

    #region Campaign Commands

    private bool HandleSelectNode(JsonElement data)
    {
        string nodeId = data.GetProperty("node_id").GetString() ?? "";
        if (string.IsNullOrEmpty(nodeId)) return false;

        EmitSignal(SignalName.NodeSelected, nodeId);
        return true;
    }

    #endregion

    #region Captain Commands

    private bool HandleApproveOverlay(JsonElement data)
    {
        string overlayId = data.GetProperty("overlay_id").GetString() ?? "";
        var overlay = _state.Overlays.Find(o => o.Id == overlayId);
        if (overlay == null) return false;

        overlay.ApprovedByCaptain = true;
        AddLog("Captain", $"Overlay genehmigt: {overlay.Text}");
        EmitSignal(SignalName.StateChanged);
        return true;
    }

    private bool HandleDismissOverlay(JsonElement data)
    {
        string overlayId = data.GetProperty("overlay_id").GetString() ?? "";
        var overlay = _state.Overlays.Find(o => o.Id == overlayId);
        if (overlay == null) return false;

        overlay.Dismissed = true;
        EmitSignal(SignalName.StateChanged);
        return true;
    }

    private bool HandleSetMissionPriority(JsonElement data)
    {
        string priority = data.GetProperty("priority").GetString() ?? "";
        AddLog("Captain", $"Missionspriorität gesetzt: {priority}");
        EmitSignal(SignalName.StateChanged);
        return true;
    }

    private bool HandleResolveDecision(JsonElement data)
    {
        string decisionId = data.GetProperty("decision_id").GetString() ?? "";
        string optionId = data.GetProperty("option_id").GetString() ?? "";

        var decision = _state.Mission.PendingDecisions.Find(d => d.Id == decisionId);
        if (decision == null || decision.IsResolved) return false;

        decision.IsResolved = true;
        decision.ChosenOptionId = optionId;
        _state.Mission.CompletedDecisions.Add(decisionId);

        var option = decision.Options.Find(o => o.Id == optionId);
        AddLog("Captain", $"Entscheidung: {decision.Title} → {option?.Label ?? optionId}");
        EmitSignal(SignalName.StateChanged);
        return true;
    }

    private bool HandleRequestStatus(StationRole fromRole, JsonElement data)
    {
        string target = data.GetProperty("target").GetString() ?? "";
        AddLog(fromRole.ToString(), $"Status angefordert: {target}");
        return true;
    }

    #endregion

    #region Navigator Commands

    private bool HandleSetWaypoint(JsonElement data)
    {
        float x = data.GetProperty("x").GetSingle();
        float y = data.GetProperty("y").GetSingle();
        string label = data.TryGetProperty("label", out var l) ? l.GetString() ?? "" : "";

        var wp = new Waypoint
        {
            Id = Guid.NewGuid().ToString("N")[..8],
            X = x, Y = y,
            Label = label.Length > 0 ? label : $"WP-{_state.Route.Waypoints.Count + 1}"
        };
        _state.Route.Waypoints.Add(wp);
        AddLog("Navigator", $"Waypoint gesetzt: {wp.Label} ({x:F0}, {y:F0})");
        RecalculateRoute();
        EmitSignal(SignalName.StateChanged);
        return true;
    }

    private bool HandleRemoveWaypoint(JsonElement data)
    {
        string wpId = data.GetProperty("waypoint_id").GetString() ?? "";
        int removed = _state.Route.Waypoints.RemoveAll(w => w.Id == wpId);
        if (removed > 0)
        {
            RecalculateRoute();
            EmitSignal(SignalName.StateChanged);
        }
        return removed > 0;
    }

    private bool HandleChangeFlightMode(JsonElement data)
    {
        string modeStr = data.GetProperty("mode").GetString() ?? "";
        if (!Enum.TryParse<FlightMode>(modeStr, true, out var mode)) return false;

        _state.Ship.FlightMode = mode;
        AddLog("Navigator", $"Flugmodus: {mode}");
        EmitSignal(SignalName.StateChanged);
        return true;
    }

    private bool HandleHighlightRoute(JsonElement data)
    {
        var wps = _state.Route.Waypoints.FindAll(w => !w.IsReached);
        string routeText = wps.Count > 0
            ? $"Route: {wps.Count} Waypoints · Nächstes Ziel: {wps[0].Label}"
            : "Keine aktiven Waypoints";

        var overlay = new OverlayRequest
        {
            Id = Guid.NewGuid().ToString("N")[..8],
            SourceStation = StationRole.Navigator,
            Category = OverlayCategory.Info,
            Priority = 1,
            Text = routeText,
            DurationSeconds = 20f,
            RemainingTime = 20f,
            ApprovedByCaptain = true,
        };
        _state.Overlays.Add(overlay);
        AddLog("Navigator", "Route auf Hauptschirm gesendet");
        EmitSignal(SignalName.StateChanged);
        return true;
    }

    private bool HandleToggleStarMap()
    {
        _state.ShowStarMapOnMainScreen = !_state.ShowStarMapOnMainScreen;
        string status = _state.ShowStarMapOnMainScreen ? "eingeblendet" : "ausgeblendet";
        AddLog("Navigator", $"Sektorkarte auf Hauptschirm {status}");
        EmitSignal(SignalName.StateChanged);
        return true;
    }

    private void RecalculateRoute()
    {
        var wps = _state.Route.Waypoints.FindAll(w => !w.IsReached);
        if (wps.Count == 0)
        {
            _state.Route.EstimatedTimeRemaining = 0;
            _state.Route.RiskValue = 0;
            return;
        }

        float totalDist = 0;
        float prevX = _state.Ship.PositionX, prevY = _state.Ship.PositionY;
        foreach (var wp in wps)
        {
            float dx = wp.X - prevX, dy = wp.Y - prevY;
            totalDist += MathF.Sqrt(dx * dx + dy * dy);
            prevX = wp.X; prevY = wp.Y;
        }

        float speed = _state.Ship.SpeedLevel * (ShipState.MaxSpeed / ShipState.MaxSpeedLevel);
        _state.Route.EstimatedTimeRemaining = speed > 0 ? totalDist / speed : float.MaxValue;
        _state.Route.RiskValue = Math.Clamp(_state.ActiveEvents.Count * 2.5f, 0, 10);
    }

    #endregion

    #region Engineer Commands

    private bool HandleSetEnergyDistribution(JsonElement data)
    {
        int drive = data.GetProperty("drive").GetInt32();
        int shields = data.GetProperty("shields").GetInt32();
        int sensors = data.GetProperty("sensors").GetInt32();

        var dist = new EnergyDistribution { Drive = drive, Shields = shields, Sensors = sensors };
        if (!dist.IsValid()) return false;

        _state.Ship.Energy = dist;
        AddLog("Engineer", $"Energie: Antrieb {drive} / Schilde {shields} / Sensorik {sensors}");
        EmitSignal(SignalName.StateChanged);
        return true;
    }

    private bool HandleStartRepair(JsonElement data)
    {
        string systemStr = data.GetProperty("system").GetString() ?? "";
        if (!Enum.TryParse<SystemId>(systemStr, true, out var systemId)) return false;

        var system = _state.Ship.Systems[systemId];
        if (system.Status == SystemStatus.Operational) return false;
        if (system.IsRepairing) return false;

        // Only one repair at a time
        foreach (var s in _state.Ship.Systems.Values)
            s.IsRepairing = false;

        system.IsRepairing = true;
        system.RepairProgress = 0;
        AddLog("Engineer", $"Reparatur gestartet: {systemId}");
        EmitSignal(SignalName.StateChanged);
        return true;
    }

    private bool HandleEmergencyShutdown(JsonElement data)
    {
        string systemStr = data.GetProperty("system").GetString() ?? "";
        if (!Enum.TryParse<SystemId>(systemStr, true, out var systemId)) return false;

        var system = _state.Ship.Systems[systemId];
        system.Status = SystemStatus.Offline;
        system.Heat = 0;
        system.IsRepairing = false;
        AddLog("Engineer", $"Notabschaltung: {systemId}");
        EmitSignal(SignalName.StateChanged);
        return true;
    }

    #endregion

    #region Tactical Commands

    private bool HandleScanContact(JsonElement data)
    {
        string contactId = data.GetProperty("contact_id").GetString() ?? "";
        var contact = _state.Contacts.Find(c => c.Id == contactId);
        if (contact == null) return false;
        if (contact.ScanProgress >= 100) return false;

        // Stop other scans
        foreach (var c in _state.Contacts) c.IsScanning = false;

        contact.IsScanning = true;
        AddLog("Tactical", $"Scan gestartet: {contact.DisplayName}");
        EmitSignal(SignalName.StateChanged);
        return true;
    }

    private bool HandleMarkContact(JsonElement data)
    {
        string contactId = data.GetProperty("contact_id").GetString() ?? "";
        var contact = _state.Contacts.Find(c => c.Id == contactId);
        if (contact == null) return false;

        var overlay = new OverlayRequest
        {
            Id = Guid.NewGuid().ToString("N")[..8],
            SourceStation = StationRole.Tactical,
            Category = OverlayCategory.Marker,
            Priority = 2,
            Text = $"⚡ Kontakt: {contact.DisplayName} (Bedrohung: {contact.ThreatLevel:F0})",
            MarkerTargetId = contactId,
            DurationSeconds = 25f,
            RemainingTime = 25f,
            ApprovedByCaptain = true,
        };
        _state.Overlays.Add(overlay);
        contact.IsVisibleOnMainScreen = true;
        AddLog("Tactical", $"Kontakt auf Hauptschirm markiert: {contact.DisplayName}");
        EmitSignal(SignalName.StateChanged);
        return true;
    }

    private bool HandleSetThreatPriority(JsonElement data)
    {
        string contactId = data.GetProperty("contact_id").GetString() ?? "";
        float threat = data.GetProperty("threat_level").GetSingle();
        var contact = _state.Contacts.Find(c => c.Id == contactId);
        if (contact == null) return false;

        contact.ThreatLevel = Math.Clamp(threat, 0, 10);
        AddLog("Tactical", $"Bedrohungsstufe {contact.DisplayName}: {threat:F0}");
        EmitSignal(SignalName.StateChanged);
        return true;
    }

    private bool HandleToggleTactical()
    {
        _state.ShowTacticalOnMainScreen = !_state.ShowTacticalOnMainScreen;
        string status = _state.ShowTacticalOnMainScreen ? "eingeblendet" : "ausgeblendet";
        AddLog("Tactical", $"Taktische Ansicht auf Hauptschirm {status}");
        EmitSignal(SignalName.StateChanged);
        return true;
    }

    private bool HandleRaiseWarning(StationRole fromRole, JsonElement data)
    {
        string message = data.GetProperty("message").GetString() ?? "";
        var overlay = new OverlayRequest
        {
            Id = Guid.NewGuid().ToString("N")[..8],
            SourceStation = fromRole,
            Category = OverlayCategory.Warning,
            Priority = 3,
            Text = message,
            DurationSeconds = 60f,
            RemainingTime = 60f,
        };
        _state.Overlays.Add(overlay);
        AddLog(fromRole.ToString(), $"Warnung gesendet: {message}");
        EmitSignal(SignalName.StateChanged);
        return true;
    }

    #endregion

    private void AddLog(string source, string message)
    {
        _state.Mission.Log.Add(new MissionLogEntry
        {
            Timestamp = _state.Mission.ElapsedTime,
            Source = source,
            Message = message,
        });
        EmitSignal(SignalName.MissionLogAdded, source, message);
    }
}
