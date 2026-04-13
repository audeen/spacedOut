using System;
using System.Text.Json;
using SpacedOut.State;

namespace SpacedOut.Commands.Handlers;

public class NavigatorCommandHandler
{
    private readonly ICommandContext _ctx;

    public NavigatorCommandHandler(ICommandContext ctx)
    {
        _ctx = ctx;
    }

    public bool Handle(string command, JsonElement data)
    {
        return command switch
        {
            CommandNames.SetWaypoint => HandleSetWaypoint(data),
            CommandNames.RemoveWaypoint => HandleRemoveWaypoint(data),
            CommandNames.ChangeFlightMode => HandleChangeFlightMode(data),
            CommandNames.HighlightRoute => HandleHighlightRoute(data),
            CommandNames.ToggleStarMapOnMainScreen => HandleToggleStarMap(),
            _ => false,
        };
    }

    private bool HandleSetWaypoint(JsonElement data)
    {
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
        _ctx.AddLog("Navigator", $"Waypoint gesetzt: {wp.Label} ({x:F0}, {y:F0}{altStr})");
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
        _ctx.AddLog("Navigator", $"Flugmodus: {mode}");
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
        _ctx.AddLog("Navigator", "Route auf Hauptschirm gesendet");
        _ctx.EmitStateChanged();
        return true;
    }

    private bool HandleToggleStarMap()
    {
        _ctx.State.ShowStarMapOnMainScreen = !_ctx.State.ShowStarMapOnMainScreen;
        string status = _ctx.State.ShowStarMapOnMainScreen ? "eingeblendet" : "ausgeblendet";
        _ctx.AddLog("Navigator", $"Sektorkarte auf Hauptschirm {status}");
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
