using System;
using System.Text.Json;
using SpacedOut.Mission;
using SpacedOut.Run;
using SpacedOut.State;

namespace SpacedOut.Commands.Handlers;

public class CaptainNavCommandHandler
{
    private readonly ICommandContext _ctx;

    public CaptainNavCommandHandler(ICommandContext ctx)
    {
        _ctx = ctx;
    }

    public bool Handle(string command, JsonElement data, StationRole role)
    {
        return command switch
        {
            // Captain commands
            CommandNames.ApproveOverlay => HandleApproveOverlay(data),
            CommandNames.DismissOverlay => HandleDismissOverlay(data),
            CommandNames.SetMissionPriority => HandleSetMissionPriority(data),
            CommandNames.ResolveDecision => HandleResolveDecision(data),
            CommandNames.RequestStatus => HandleRequestStatus(role, data),
            CommandNames.SetEngagementRule => HandleSetEngagementRule(data),

            // Navigator commands
            CommandNames.SetWaypoint => HandleSetWaypoint(data),
            CommandNames.RemoveWaypoint => HandleRemoveWaypoint(data),
            CommandNames.ChangeFlightMode => HandleChangeFlightMode(data),
            CommandNames.HighlightRoute => HandleHighlightRoute(data),
            CommandNames.ToggleStarMapOnMainScreen => HandleToggleStarMap(),
            CommandNames.SetCourseToContact => HandleSetCourseToContact(data),
            CommandNames.SetTargetTracking => HandleSetTargetTracking(data),
            CommandNames.LeaveSector => HandleLeaveSector(),

            _ => false,
        };
    }

    // ── Captain ──

    private bool HandleApproveOverlay(JsonElement data)
    {
        string overlayId = data.GetProperty("overlay_id").GetString() ?? "";
        var overlay = _ctx.State.Overlays.Find(o => o.Id == overlayId);
        if (overlay == null) return false;

        overlay.ApprovedByCaptain = true;
        _ctx.AddLog("CaptainNav", $"Overlay genehmigt: {overlay.Text}");
        _ctx.EmitStateChanged();
        return true;
    }

    private bool HandleDismissOverlay(JsonElement data)
    {
        string overlayId = data.GetProperty("overlay_id").GetString() ?? "";
        var overlay = _ctx.State.Overlays.Find(o => o.Id == overlayId);
        if (overlay == null) return false;

        overlay.Dismissed = true;
        _ctx.EmitStateChanged();
        return true;
    }

    private bool HandleSetMissionPriority(JsonElement data)
    {
        string priority = data.GetProperty("priority").GetString() ?? "";
        _ctx.AddLog("CaptainNav", $"Missionspriorität gesetzt: {priority}");
        _ctx.EmitStateChanged();
        return true;
    }

    private bool HandleResolveDecision(JsonElement data)
    {
        string decisionId = data.GetProperty("decision_id").GetString() ?? "";
        string optionId = data.GetProperty("option_id").GetString() ?? "";

        var decision = _ctx.State.Mission.PendingDecisions.Find(d => d.Id == decisionId);
        if (decision == null || decision.IsResolved) return false;

        decision.IsResolved = true;
        decision.ChosenOptionId = optionId;
        _ctx.State.Mission.CompletedDecisions.Add(decisionId);

        var option = decision.Options.Find(o => o.Id == optionId);

        bool isPreSector = _ctx.State.Mission.PreSectorEventActive
            && decisionId == PreSectorDecisionId(_ctx.State.Mission.PendingPreSectorEventId);
        string preSectorNodeId = _ctx.State.Mission.PendingPreSectorNodeId;
        string preSectorEventId = _ctx.State.Mission.PendingPreSectorEventId;

        DecisionEffectResolver.Apply(
            option?.Effects,
            _ctx.State,
            _ctx.State.ActiveRunState,
            _ctx.MissionController,
            addLog: (src, msg) => _ctx.AddLog(src, msg),
            sourceLabel: "Event",
            orchestrator: _ctx.Orchestrator);

        string narrative = !string.IsNullOrWhiteSpace(option?.ResolutionNarrative)
            ? option!.ResolutionNarrative.Trim()
            : $"Die Brücke setzt „{option?.Label ?? optionId}“ für „{decision.Title}“ um.";
        string effectsLine = DecisionEffectsSummary.FormatGerman(option?.Effects);
        _ctx.NotifyDecisionResolved(narrative, effectsLine, decision.Title, option?.Label, cinematicResolution: isPreSector);

        _ctx.State.Mission.LastDecisionHighlight = $"{decision.Title} → {option?.Label ?? optionId}";

        if (isPreSector)
        {
            bool skip = option?.Effects?.SkipSector ?? false;
            _ctx.State.Mission.PreSectorEventActive = false;
            _ctx.State.Mission.PendingPreSectorEventId = "";
            _ctx.State.Mission.PendingPreSectorEventTitle = "";
            _ctx.State.Mission.PendingPreSectorNodeId = "";
            _ctx.OnPreSectorDecisionResolved?.Invoke(preSectorNodeId, preSectorEventId, skip);
        }

        _ctx.EmitStateChanged();
        return true;
    }

    /// <summary>Convention: pre-sector decisions are keyed <c>presector:&lt;eventId&gt;</c> so the handler can route them to <see cref="Orchestration.MissionOrchestrator.ResolvePreSectorDecision"/>.</summary>
    public static string PreSectorDecisionId(string eventId) => $"presector:{eventId}";

    private bool HandleRequestStatus(StationRole fromRole, JsonElement data)
    {
        string target = data.GetProperty("target").GetString() ?? "";
        _ctx.AddLog(fromRole.ToString(), $"Status angefordert: {target}");
        return true;
    }

    private bool HandleSetEngagementRule(JsonElement data)
    {
        string ruleStr = data.GetProperty("rule").GetString() ?? "";
        if (!Enum.TryParse<EngagementRule>(ruleStr, true, out var rule)) return false;

        _ctx.State.Engagement = rule;
        _ctx.AddLog("CaptainNav", $"Einsatzregel: {rule}");
        _ctx.EmitStateChanged();
        return true;
    }

    private bool HandleLeaveSector()
    {
        if (_ctx.MissionController == null) return false;
        if (!_ctx.MissionController.TryCaptainLeaveSector()) return false;
        _ctx.AddLog("CaptainNav", "Sektor verlassen — Sprung eingeleitet.");
        _ctx.EmitStateChanged();
        return true;
    }

    // ── Navigator ──

    private bool HandleSetWaypoint(JsonElement data)
    {
        _ctx.State.Navigation.TargetTracking.Clear();

        float x = data.GetProperty("x").GetSingle();
        float y = data.GetProperty("y").GetSingle();
        float z = data.TryGetProperty("z", out var zVal) ? zVal.GetSingle() : _ctx.State.Ship.PositionZ;
        string label = data.TryGetProperty("label", out var l) ? l.GetString() ?? "" : "";

        var wp = new Waypoint
        {
            Id = Guid.NewGuid().ToString("N")[..8],
            X = x, Y = y, Z = z,
            Label = label.Length > 0 ? label : $"WP-{_ctx.State.Route.Waypoints.Count + 1}"
        };
        _ctx.State.Route.Waypoints.Add(wp);
        float altDiff = z - 500f;
        string altStr = MathF.Abs(altDiff) > 10f ? $", ALT {(altDiff >= 0 ? "+" : "")}{altDiff:F0}" : "";
        _ctx.AddLog("CaptainNav", $"Waypoint gesetzt: {wp.Label} ({x:F0}, {y:F0}{altStr})");
        RecalculateRoute();
        _ctx.EmitStateChanged();
        return true;
    }

    private bool HandleRemoveWaypoint(JsonElement data)
    {
        string wpId = data.GetProperty("waypoint_id").GetString() ?? "";
        int removed = _ctx.State.Route.Waypoints.RemoveAll(w => w.Id == wpId);
        if (removed > 0)
        {
            RecalculateRoute();
            _ctx.EmitStateChanged();
        }
        return removed > 0;
    }

    private bool HandleChangeFlightMode(JsonElement data)
    {
        string modeStr = data.GetProperty("mode").GetString() ?? "";
        if (!Enum.TryParse<FlightMode>(modeStr, true, out var mode)) return false;

        _ctx.State.Ship.FlightMode = mode;
        _ctx.AddLog("CaptainNav", $"Flugmodus: {mode}");
        _ctx.EmitStateChanged();
        return true;
    }

    private bool HandleHighlightRoute(JsonElement data)
    {
        var wps = _ctx.State.Route.Waypoints.FindAll(w => !w.IsReached);
        string routeText = wps.Count > 0
            ? $"Route: {wps.Count} Waypoints · Nächstes Ziel: {wps[0].Label}"
            : "Keine aktiven Waypoints";

        var overlay = new OverlayRequest
        {
            Id = Guid.NewGuid().ToString("N")[..8],
            SourceStation = StationRole.CaptainNav,
            Category = OverlayCategory.Info,
            Priority = 1,
            Text = routeText,
            DurationSeconds = 20f,
            RemainingTime = 20f,
            ApprovedByCaptain = true,
        };
        _ctx.State.Overlays.Add(overlay);
        _ctx.AddLog("CaptainNav", "Route auf Hauptschirm gesendet");
        _ctx.EmitStateChanged();
        return true;
    }

    private bool HandleToggleStarMap()
    {
        _ctx.State.ShowStarMapOnMainScreen = !_ctx.State.ShowStarMapOnMainScreen;
        string status = _ctx.State.ShowStarMapOnMainScreen ? "eingeblendet" : "ausgeblendet";
        _ctx.AddLog("CaptainNav", $"Sektorkarte auf Hauptschirm {status}");
        _ctx.EmitStateChanged();
        return true;
    }

    private bool HandleSetCourseToContact(JsonElement data)
    {
        string contactId = data.GetProperty("contact_id").GetString() ?? "";
        string mode = data.TryGetProperty("mode", out var m) ? m.GetString() ?? "approach" : "approach";

        var contact = _ctx.State.Contacts.Find(c => c.Id == contactId);
        if (contact == null) return false;

        _ctx.State.Navigation.TargetTracking.Clear();

        float targetX, targetY, targetZ;
        if (mode == "flee")
        {
            float dx = _ctx.State.Ship.PositionX - contact.PositionX;
            float dy = _ctx.State.Ship.PositionY - contact.PositionY;
            float dist = MathF.Sqrt(dx * dx + dy * dy);
            if (dist < 1f) dist = 1f;
            targetX = _ctx.State.Ship.PositionX + (dx / dist) * 300f;
            targetY = _ctx.State.Ship.PositionY + (dy / dist) * 300f;
            targetZ = _ctx.State.Ship.PositionZ;
        }
        else
        {
            targetX = contact.PositionX;
            targetY = contact.PositionY;
            targetZ = contact.PositionZ;
        }

        _ctx.State.Route.Waypoints.RemoveAll(w => !w.IsReached);
        var wp = new Waypoint
        {
            Id = Guid.NewGuid().ToString("N")[..8],
            X = targetX, Y = targetY, Z = targetZ,
            Label = mode == "flee" ? $"Flucht von {contact.DisplayName}" : $"Kurs auf {contact.DisplayName}"
        };
        _ctx.State.Route.Waypoints.Add(wp);
        _ctx.AddLog("CaptainNav", mode == "flee"
            ? $"Fluchtvektor von {contact.DisplayName}"
            : $"Angriffsvektor auf {contact.DisplayName}");
        RecalculateRoute();
        _ctx.EmitStateChanged();
        return true;
    }

    private bool HandleSetTargetTracking(JsonElement data)
    {
        string modeStr = data.GetProperty("mode").GetString() ?? "None";
        if (!Enum.TryParse<TargetTrackingMode>(modeStr, true, out var mode))
            return false;

        var tt = _ctx.State.Navigation.TargetTracking;
        var ship = _ctx.State.Ship;

        if (mode == TargetTrackingMode.None)
        {
            tt.Clear();
            _ctx.AddLog("CaptainNav", "Zielverfolgung beendet");
            RecalculateRoute();
            _ctx.EmitStateChanged();
            return true;
        }

        string contactId = data.GetProperty("contact_id").GetString() ?? "";
        var contact = _ctx.State.Contacts.Find(c => c.Id == contactId);
        if (contact == null || contact.IsDestroyed || contact.Discovery != DiscoveryState.Scanned
            || !_ctx.State.IsContactAvailableToCaptainNav(contact))
            return false;

        float range = tt.Range;
        if (data.TryGetProperty("range", out var rEl))
            range = rEl.GetSingle();
        range = Math.Clamp(range, TargetTrackingState.MinRange, TargetTrackingState.MaxRange);

        bool cw = tt.OrbitClockwise;
        if (data.TryGetProperty("orbit_clockwise", out var cwEl))
            cw = cwEl.GetBoolean();

        bool sameOrbit = tt.Mode == TargetTrackingMode.Orbit
            && tt.TrackedContactId == contactId
            && mode == TargetTrackingMode.Orbit;

        tt.Mode = mode;
        tt.TrackedContactId = contactId;
        tt.Range = range;
        tt.OrbitClockwise = cw;

        if (mode == TargetTrackingMode.Orbit && !sameOrbit)
            tt.OrbitAngle = MathF.Atan2(ship.PositionY - contact.PositionY, ship.PositionX - contact.PositionX);

        _ctx.State.Route.Waypoints.RemoveAll(w => !w.IsReached);

        string logMsg = mode switch
        {
            TargetTrackingMode.Follow => $"Zielverfolgung: Folgen — {contact.DisplayName}",
            TargetTrackingMode.Orbit => $"Zielverfolgung: Orbit {range:F0} m — {contact.DisplayName}",
            TargetTrackingMode.KeepAtRange => $"Zielverfolgung: Abstand {range:F0} m — {contact.DisplayName}",
            _ => "Zielverfolgung"
        };
        _ctx.AddLog("CaptainNav", logMsg);
        RecalculateRoute();
        _ctx.EmitStateChanged();
        return true;
    }

    private void RecalculateRoute()
    {
        var state = _ctx.State;
        var wps = state.Route.Waypoints.FindAll(w => !w.IsReached);
        if (wps.Count == 0)
        {
            state.Route.EstimatedTimeRemaining = 0;
            state.Route.RiskValue = 0;
            return;
        }

        float totalDist = 0;
        float prevX = state.Ship.PositionX, prevY = state.Ship.PositionY, prevZ = state.Ship.PositionZ;
        foreach (var wp in wps)
        {
            float dx = wp.X - prevX, dy = wp.Y - prevY, dz = wp.Z - prevZ;
            totalDist += MathF.Sqrt(dx * dx + dy * dy + dz * dz);
            prevX = wp.X; prevY = wp.Y; prevZ = wp.Z;
        }

        float speed = state.Ship.SpeedLevel * (ShipState.MaxSpeed / ShipState.MaxSpeedLevel);
        state.Route.EstimatedTimeRemaining = speed > 0 ? totalDist / speed : float.MaxValue;
        state.Route.RiskValue = Math.Clamp(state.ActiveEvents.Count * 2.5f, 0, 10);
    }
}
