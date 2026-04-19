using System.Linq;
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
        _state.MissionLogEntryAdded += OnMissionLogEntryAdded;
        _state.PendingDecisionAdded += OnPendingDecisionAdded;
    }

    private void OnPendingDecisionAdded(MissionDecision decision)
    {
        foreach (var kvp in _server.Clients)
        {
            var client = kvp.Value;
            if (!client.IsConnected || client.Role != StationRole.CaptainNav) continue;

            var msg = JsonSerializer.Serialize(new
            {
                type = "pending_decision",
                decision = new
                {
                    id = decision.Id,
                    title = decision.Title,
                    description = decision.Description,
                    options = decision.Options.Select(o => new
                    {
                        id = o.Id,
                        label = o.Label,
                        description = o.Description,
                        flavor_hint = o.FlavorHint,
                    }).ToList(),
                }
            });
            _server.SendToClient(kvp.Key, msg);
        }
    }

    private void OnMissionLogEntryAdded(MissionLogEntry entry)
    {
        foreach (var kvp in _server.Clients)
        {
            var client = kvp.Value;
            if (!client.IsConnected || client.Role == null) continue;
            if (!MissionLogRouting.IsVisibleToRole(entry.Source, client.Role.Value)) continue;

            var msg = JsonSerializer.Serialize(new
            {
                type = "mission_log_line",
                source = entry.Source,
                message = entry.Message,
                timestamp = entry.Timestamp,
                web_toast = entry.WebToast.ToString(),
            });
            _server.SendToClient(kvp.Key, msg);
        }
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

    /// <summary>
    /// Kommandant (CaptainNav): Prosatext + Ergebniszeile nach <c>ResolveDecision</c>.
    /// </summary>
    public void SendDecisionResolvedToCaptainNav(
        string narrative,
        string effectsLine,
        string? decisionTitle,
        string? optionLabel,
        bool cinematicResolution)
    {
        var msg = JsonSerializer.Serialize(new
        {
            type = "decision_resolved",
            narrative,
            effects_line = effectsLine,
            decision_title = decisionTitle,
            option_label = optionLabel,
            resolution_style = cinematicResolution ? "cinematic" : "toast",
        });
        foreach (var kvp in _server.Clients)
        {
            var client = kvp.Value;
            if (!client.IsConnected || client.Role != StationRole.CaptainNav) continue;
            _server.SendToClient(kvp.Key, msg);
        }
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
