using System.Text.Json;
using SpacedOut.Network;
using SpacedOut.State;

namespace SpacedOut.Orchestration;

public class BroadcastService
{
    private readonly GameState _state;
    private readonly GameServer _server;

    private float _timer;
    private const float Interval = 0.1f;

    public BroadcastService(GameState state, GameServer server)
    {
        _state = state;
        _server = server;
    }

    public void Update(float delta)
    {
        _timer += delta;
        if (_timer < Interval) return;
        _timer = 0;
        BroadcastStateUpdates();
    }

    public void BroadcastStateUpdates()
    {
        foreach (var kvp in _server.Clients)
        {
            var client = kvp.Value;
            if (!client.IsConnected || client.Role == null) continue;

            var roleState = _state.GetStateForRole(client.Role.Value);
            var message = JsonSerializer.Serialize(new
            {
                type = "state_update",
                role = client.Role.Value.ToString(),
                elapsed_time = _state.Mission.ElapsedTime,
                mission_phase = _state.Mission.Phase.ToString(),
                mission_title = _state.Mission.MissionTitle,
                use_structured_phases = _state.Mission.UseStructuredMissionPhases,
                mission_started = _state.MissionStarted,
                is_paused = _state.IsPaused,
                data = roleState
            });
            _server.SendToClient(kvp.Key, message);
        }
    }

    public void BroadcastRoleStatus()
    {
        var roles = new System.Collections.Generic.Dictionary<string, bool>
        {
            ["CaptainNav"] = _server.IsRoleTaken(StationRole.CaptainNav),
            ["Engineer"] = _server.IsRoleTaken(StationRole.Engineer),
            ["Tactical"] = _server.IsRoleTaken(StationRole.Tactical),
            ["Gunner"] = _server.IsRoleTaken(StationRole.Gunner),
        };
        var msg = JsonSerializer.Serialize(new
        {
            type = "role_status",
            roles,
            available = _server.GetAvailableRoles().ConvertAll(r => r.ToString()),
        });
        _server.BroadcastToAll(msg);
    }

    public void BroadcastMissionStarted()
    {
        var msg = JsonSerializer.Serialize(new
        {
            type = "mission_started",
            briefing = _state.Mission.BriefingText,
        });
        _server.BroadcastToAll(msg);
    }

    public void BroadcastPaused()
    {
        var msg = JsonSerializer.Serialize(new
        {
            type = _state.IsPaused ? "paused" : "resumed",
        });
        _server.BroadcastToAll(msg);
    }

    public void BroadcastMissionEnded(Mission.MissionResult result)
    {
        var msg = JsonSerializer.Serialize(new
        {
            type = "mission_ended",
            result = result.ToString(),
            elapsed_time = _state.Mission.ElapsedTime,
            primary_objective = _state.Mission.PrimaryObjective.ToString(),
            secondary_objective = _state.Mission.SecondaryObjective.ToString(),
            hull_integrity = _state.Ship.HullIntegrity,
        });
        _server.BroadcastToAll(msg);
    }

    public void BroadcastPhaseChanged(string phase)
    {
        var msg = JsonSerializer.Serialize(new
        {
            type = "phase_changed",
            phase,
            elapsed_time = _state.Mission.ElapsedTime,
        });
        _server.BroadcastToAll(msg);
    }

    public void BroadcastEvent(string eventId)
    {
        var evt = _state.ActiveEvents.Find(e => e.Id == eventId);
        if (evt == null) return;

        var msg = JsonSerializer.Serialize(new
        {
            type = "event",
            event_id = eventId,
            title = evt.Title,
            description = evt.Description,
        });
        _server.BroadcastToAll(msg);
    }

    public void SendWelcome(string clientId)
    {
        var availableRoles = _server.GetAvailableRoles();
        var msg = JsonSerializer.Serialize(new
        {
            type = "welcome",
            client_id = clientId,
            available_roles = availableRoles.ConvertAll(r => r.ToString()),
            mission_started = _state.MissionStarted,
        });
        _server.SendToClient(clientId, msg);
    }

    public void SendError(string clientId, string message)
    {
        var msg = JsonSerializer.Serialize(new
        {
            type = "error",
            message,
        });
        _server.SendToClient(clientId, msg);
    }
}
