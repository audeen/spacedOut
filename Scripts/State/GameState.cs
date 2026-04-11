using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using Godot;
using SpacedOut.Shared;

namespace SpacedOut.State;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum FlightMode { Cruise, Approach, Evasive, Hold }

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum MissionPhase { Briefing, Anflug, Stoerung, Krisenfenster, Abschluss, Ended }

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ObjectiveStatus { InProgress, Completed, Failed }

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum SystemId { Drive, Shields, Sensors }

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum SystemStatus { Operational, Degraded, Damaged, Offline }

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ContactType { Unknown, Friendly, Hostile, Neutral, Anomaly }

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum StationRole { Captain, Navigator, Engineer, Tactical, Observer }

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum OverlayCategory { Warning, Marker, Info, Tactical }

public class ShipSystem
{
    public SystemId Id { get; set; }
    public SystemStatus Status { get; set; } = SystemStatus.Operational;
    public float Heat { get; set; }
    public float RepairProgress { get; set; }
    public bool IsRepairing { get; set; }

    public const float MaxHeat = 100f;
    public const float OverheatThreshold = 80f;
    public const float CriticalHeatThreshold = 95f;
}

public class EnergyDistribution
{
    public int Drive { get; set; } = 34;
    public int Shields { get; set; } = 33;
    public int Sensors { get; set; } = 33;
    public const int TotalBudget = 100;

    public bool IsValid() => Drive + Shields + Sensors == TotalBudget
        && Drive >= 0 && Shields >= 0 && Sensors >= 0;
}

public class ShipState
{
    public float PositionX { get; set; }
    public float PositionY { get; set; }
    public int SpeedLevel { get; set; } = 2;
    public FlightMode FlightMode { get; set; } = FlightMode.Cruise;
    public float HullIntegrity { get; set; } = 100f;
    public EnergyDistribution Energy { get; set; } = new();
    public Dictionary<SystemId, ShipSystem> Systems { get; set; } = new()
    {
        { SystemId.Drive, new ShipSystem { Id = SystemId.Drive } },
        { SystemId.Shields, new ShipSystem { Id = SystemId.Shields } },
        { SystemId.Sensors, new ShipSystem { Id = SystemId.Sensors } },
    };

    public const float MaxSpeed = 5f;
    public const int MaxSpeedLevel = 4;
}

public class MissionState
{
    public string MissionId { get; set; } = "bergung_unter_stoerung";
    public string MissionTitle { get; set; } = "Bergung unter Störung";
    public MissionPhase Phase { get; set; } = MissionPhase.Briefing;
    public ObjectiveStatus PrimaryObjective { get; set; } = ObjectiveStatus.InProgress;
    public ObjectiveStatus SecondaryObjective { get; set; } = ObjectiveStatus.InProgress;
    public float ElapsedTime { get; set; }
    public float PhaseTimer { get; set; }
    public string BriefingText { get; set; } = "";
    public List<MissionDecision> PendingDecisions { get; set; } = new();
    public List<string> CompletedDecisions { get; set; } = new();
    public List<MissionLogEntry> Log { get; set; } = new();

    // Pre-computed map position of the encounter marker (for TriggerUnknownContact)
    public float EncounterSpawnX { get; set; } = 900f;
    public float EncounterSpawnY { get; set; } = 800f;
}

public class MissionDecision
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public List<DecisionOption> Options { get; set; } = new();
    public bool IsResolved { get; set; }
    public string? ChosenOptionId { get; set; }
}

public class DecisionOption
{
    public string Id { get; set; } = "";
    public string Label { get; set; } = "";
    public string Description { get; set; } = "";
}

public class MissionLogEntry
{
    public float Timestamp { get; set; }
    public string Source { get; set; } = "";
    public string Message { get; set; } = "";
}

public class Contact
{
    public string Id { get; set; } = "";
    public ContactType Type { get; set; } = ContactType.Unknown;
    public string DisplayName { get; set; } = "";
    public float PositionX { get; set; }
    public float PositionY { get; set; }
    public float ThreatLevel { get; set; }
    public float ScanProgress { get; set; }
    public bool IsVisibleOnMainScreen { get; set; }
    public bool IsScanning { get; set; }
    public float VelocityX { get; set; }
    public float VelocityY { get; set; }
}

public class OverlayRequest
{
    public string Id { get; set; } = "";
    public StationRole SourceStation { get; set; }
    public OverlayCategory Category { get; set; }
    public int Priority { get; set; } = 1;
    public string Text { get; set; } = "";
    public string? MarkerTargetId { get; set; }
    public float DurationSeconds { get; set; } = 10f;
    public float RemainingTime { get; set; }
    public bool ApprovedByCaptain { get; set; }
    public bool Dismissed { get; set; }
}

public class RouteState
{
    public List<Waypoint> Waypoints { get; set; } = new();
    public int CurrentWaypointIndex { get; set; }
    public float RiskValue { get; set; }
    public float EstimatedTimeRemaining { get; set; }
}

public class Waypoint
{
    public string Id { get; set; } = "";
    public float X { get; set; }
    public float Y { get; set; }
    public string Label { get; set; } = "";
    public bool IsReached { get; set; }
}

public class GameEvent
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public bool IsActive { get; set; }
    public bool IsResolved { get; set; }
    public float TimeRemaining { get; set; }
}

public class GameState
{
    public MissionState Mission { get; set; } = new();
    public ShipState Ship { get; set; } = new();
    public List<Contact> Contacts { get; set; } = new();
    public List<OverlayRequest> Overlays { get; set; } = new();
    public RouteState Route { get; set; } = new();
    public List<GameEvent> ActiveEvents { get; set; } = new();
    public bool IsPaused { get; set; }
    public bool MissionStarted { get; set; }
    public bool ShowStarMapOnMainScreen { get; set; }
    public bool ShowTacticalOnMainScreen { get; set; }
    public bool ShowSectorMapOnMainScreen { get; set; }
    public bool CampaignActive { get; set; }

    public Dictionary<string, object> GetStateForRole(StationRole role)
    {
        var data = new Dictionary<string, object>();

        switch (role)
        {
            case StationRole.Captain:
                data["mission"] = Mission;
                data["hull_integrity"] = Ship.HullIntegrity;
                data["flight_mode"] = Ship.FlightMode.ToString();
                data["overlays"] = Overlays.FindAll(o => !o.Dismissed);
                data["pending_decisions"] = Mission.PendingDecisions.FindAll(d => !d.IsResolved);
                data["active_events"] = ActiveEvents.FindAll(e => e.IsActive);
                data["log"] = Mission.Log;
                data["systems_summary"] = GetSystemsSummary();
                break;

            case StationRole.Navigator:
                data["ship_x"] = Ship.PositionX;
                data["ship_y"] = Ship.PositionY;
                data["flight_mode"] = Ship.FlightMode.ToString();
                data["speed_level"] = Ship.SpeedLevel;
                data["route"] = Route;
                data["contacts"] = Contacts.FindAll(c => c.ScanProgress > 20 || c.IsVisibleOnMainScreen);
                data["drive_energy"] = Ship.Energy.Drive;
                data["drive_status"] = Ship.Systems[SystemId.Drive].Status.ToString();
                data["mission_phase"] = Mission.Phase.ToString();
                data["star_map_on_main_screen"] = ShowStarMapOnMainScreen;
                break;

            case StationRole.Engineer:
                data["energy"] = Ship.Energy;
                data["systems"] = Ship.Systems;
                data["hull_integrity"] = Ship.HullIntegrity;
                data["flight_mode"] = Ship.FlightMode.ToString();
                data["active_events"] = ActiveEvents.FindAll(e => e.IsActive);
                break;

            case StationRole.Tactical:
                data["contacts"] = Contacts;
                data["sensor_energy"] = Ship.Energy.Sensors;
                data["sensor_status"] = Ship.Systems[SystemId.Sensors].Status.ToString();
                data["sensor_range"] = CalculateSensorRange();
                data["active_events"] = ActiveEvents.FindAll(e => e.IsActive);
                data["ship_x"] = Ship.PositionX;
                data["ship_y"] = Ship.PositionY;
                data["tactical_on_main_screen"] = ShowTacticalOnMainScreen;
                break;

            case StationRole.Observer:
                data["mission"] = Mission;
                data["ship"] = Ship;
                data["contacts"] = Contacts;
                data["overlays"] = Overlays;
                data["route"] = Route;
                data["active_events"] = ActiveEvents;
                break;
        }

        return data;
    }

    private Dictionary<string, string> GetSystemsSummary()
    {
        var summary = new Dictionary<string, string>();
        foreach (var kvp in Ship.Systems)
            summary[kvp.Key.ToString()] = kvp.Value.Status.ToString();
        return summary;
    }

    private float CalculateSensorRange() =>
        ShipCalculations.CalculateSensorRange(Ship);

    public Dictionary<string, object> GetMainScreenState()
    {
        var data = new Dictionary<string, object>();
        data["ship_x"] = Ship.PositionX;
        data["ship_y"] = Ship.PositionY;
        data["flight_mode"] = Ship.FlightMode.ToString();
        data["speed_level"] = Ship.SpeedLevel;
        data["hull_integrity"] = Ship.HullIntegrity;
        data["contacts"] = Contacts.FindAll(c => c.IsVisibleOnMainScreen);
        data["overlays"] = Overlays.FindAll(o => o.ApprovedByCaptain && !o.Dismissed);
        data["mission_phase"] = Mission.Phase.ToString();
        data["active_events"] = ActiveEvents.FindAll(e => e.IsActive);
        data["route"] = Route;
        return data;
    }
}
