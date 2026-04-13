using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Linq;
using Godot;
using SpacedOut.Run;
using SpacedOut.Shared;

namespace SpacedOut.State;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum FlightMode { Cruise, Approach, Evasive, Hold }

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum MissionPhase { Briefing, Anflug, Stoerung, Krisenfenster, Abschluss, Operational, Ended }

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ObjectiveStatus { InProgress, Completed, Failed }

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum SystemId { Drive, Shields, Sensors, Weapons }

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum SystemStatus { Operational, Degraded, Damaged, Offline }

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ContactType { Unknown, Friendly, Hostile, Neutral, Anomaly }

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum StationRole { CaptainNav, Engineer, Tactical, Gunner, Observer }

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum OverlayCategory { Warning, Marker, Info, Tactical }

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum DiscoveryState { Hidden, Detected, Probed, Scanned }

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum WeaponMode { Precision, Barrage }

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum EngagementRule { Standard, Aggressive, Defensive }

public class ShipSystem
{
    public SystemId Id { get; set; }
    public SystemStatus Status { get; set; } = SystemStatus.Operational;
    public float Heat { get; set; }
    public float RepairProgress { get; set; }
    public bool IsRepairing { get; set; }
    public float CoolantCooldown { get; set; }

    public const float MaxHeat = 100f;
    public const float WarningHeatThreshold = 50f;
    public const float EfficiencyLossThreshold = 70f;
    public const float SevereEfficiencyThreshold = 85f;
    public const float CriticalHeatThreshold = 95f;
    public const float CoolantCooldownTime = 15f;

    public float GetHeatEfficiencyMultiplier()
    {
        if (Heat >= SevereEfficiencyThreshold) return 0.5f;
        if (Heat >= EfficiencyLossThreshold) return 0.8f;
        return 1f;
    }
}

public class EnergyDistribution
{
    public int Drive { get; set; } = 25;
    public int Shields { get; set; } = 25;
    public int Sensors { get; set; } = 25;
    public int Weapons { get; set; } = 25;
    public const int TotalBudget = 100;

    public bool IsValid() => Drive + Shields + Sensors + Weapons == TotalBudget
        && Drive >= 0 && Shields >= 0 && Sensors >= 0 && Weapons >= 0;
}

public class ShipState
{
    public float PositionX { get; set; }
    public float PositionY { get; set; }
    public float PositionZ { get; set; } = 500f;
    public int SpeedLevel { get; set; } = 2;
    public FlightMode FlightMode { get; set; } = FlightMode.Cruise;
    public float HullIntegrity { get; set; } = 100f;
    public EnergyDistribution Energy { get; set; } = new();
    public Dictionary<SystemId, ShipSystem> Systems { get; set; } = new()
    {
        { SystemId.Drive, new ShipSystem { Id = SystemId.Drive } },
        { SystemId.Shields, new ShipSystem { Id = SystemId.Shields } },
        { SystemId.Sensors, new ShipSystem { Id = SystemId.Sensors } },
        { SystemId.Weapons, new ShipSystem { Id = SystemId.Weapons } },
    };

    public const float MaxSpeed = 5f;
    public const int MaxSpeedLevel = 4;
}

public class MissionState
{
    public string MissionId { get; set; } = "bergung_unter_stoerung";
    public string MissionTitle { get; set; } = "Bergung unter Störung";
    public MissionPhase Phase { get; set; } = MissionPhase.Briefing;
    /// <summary>When true, scripted phase timeline and phase UI are active; otherwise sandbox (single <see cref="MissionPhase.Operational"/> phase).</summary>
    public bool UseStructuredMissionPhases { get; set; }
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
    public float PositionZ { get; set; } = 500f;
    public float ThreatLevel { get; set; }
    public float ScanProgress { get; set; }
    public bool IsVisibleOnMainScreen { get; set; }
    public bool IsScanning { get; set; }
    public float VelocityX { get; set; }
    public float VelocityY { get; set; }
    public float VelocityZ { get; set; }

    public DiscoveryState Discovery { get; set; } = DiscoveryState.Hidden;
    /// <summary>Elapsed-time timestamp when probe snapshot expires (Probed contacts revert to Hidden).</summary>
    public float ProbeExpiry { get; set; }
    /// <summary>Frozen position captured when a probe revealed this contact.</summary>
    public float SnapshotX { get; set; }
    public float SnapshotY { get; set; }
    public float SnapshotZ { get; set; }
    /// <summary>When true, this contact is visible on the navigator's map (must be explicitly released by tactical).</summary>
    public bool ReleasedToNav { get; set; }
    /// <summary>Known objects (stations, mission targets, beacons) that bypass the fog-of-war pipeline entirely.</summary>
    public bool PreRevealed { get; set; }
    /// <summary>Tactical has designated this as a priority target for the gunner (+25% damage).</summary>
    public bool IsDesignated { get; set; }
    /// <summary>Tactical has completed a deep analysis revealing weaknesses (+50% damage).</summary>
    public bool HasWeakness { get; set; }
    /// <summary>Progress of the weakness analysis scan (0-100), starts after initial scan completes.</summary>
    public float WeaknessAnalysisProgress { get; set; }
    /// <summary>True while tactical is actively running a weakness analysis on this contact.</summary>
    public bool IsAnalyzing { get; set; }
    /// <summary>Health points for hostile contacts (0 = destroyed).</summary>
    public float HitPoints { get; set; } = 100f;
    /// <summary>Maximum health points.</summary>
    public float MaxHitPoints { get; set; } = 100f;
    /// <summary>True when this contact has been destroyed by gunner fire.</summary>
    public bool IsDestroyed { get; set; }
    /// <summary>Damage this hostile deals per attack cycle.</summary>
    public float AttackDamage { get; set; }
    /// <summary>Seconds between attacks when in range.</summary>
    public float AttackInterval { get; set; } = 10f;
    /// <summary>Countdown until next attack.</summary>
    public float AttackCooldown { get; set; }
    /// <summary>Maximum range at which this contact attacks the player ship.</summary>
    public float AttackRange { get; set; } = 200f;
    /// <summary>True when this hostile is currently targeting the player ship.</summary>
    public bool IsTargetingPlayer { get; set; }
}

public class MapResourceZone
{
    public string Id { get; set; } = "";
    public string ResourceType { get; set; } = "";
    public float X { get; set; }
    public float Y { get; set; }
    public float MapRadius { get; set; }
    public float Density { get; set; }
    public string MapColorHex { get; set; } = "#ffffff";
    public string Discovery { get; set; } = "Hidden";
}

public class SensorProbe
{
    public string Id { get; set; } = "";
    public float X { get; set; }
    public float Y { get; set; }
    public float Z { get; set; } = 500f;
    public float RevealRadius { get; set; } = 150f;
    public float RemainingTime { get; set; } = 25f;
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
    public float Z { get; set; } = 500f;
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

public class GunnerState
{
    public string? SelectedTargetId { get; set; }
    public float TargetLockProgress { get; set; }
    public WeaponMode Mode { get; set; } = WeaponMode.Precision;
    public bool IsDefensiveMode { get; set; }
    public float FireCooldown { get; set; }

    public const float PrecisionLockTime = 3.5f;
    public const float BarrageLockTime = 1.5f;
    public const float PrecisionFireInterval = 5f;
    public const float BarrageFireInterval = 1.2f;
    public const float PrecisionBaseDamage = 35f;
    public const float BarrageBaseDamage = 10f;
    public const float PrecisionHeatPerShot = 12f;
    public const float BarrageHeatPerShot = 8f;
    public const float DefensiveDamageMultiplier = 0.5f;
}

public class PinnedEntity
{
    public string EntityId { get; set; } = "";
    public string Label { get; set; } = "";
    public string Detail { get; set; } = "";
    public float PinnedAt { get; set; }
}

public class GameState
{
    public MissionState Mission { get; set; } = new();
    public ShipState Ship { get; set; } = new();
    public bool IsPaused { get; set; }
    public bool MissionStarted { get; set; }
    public EngagementRule Engagement { get; set; } = EngagementRule.Standard;

    // Sub-state objects
    public NavigationState Navigation { get; } = new();
    public ContactsState ContactsState { get; } = new();
    public OverlayState OverlayState { get; } = new();
    public RunStateSnapshot Run { get; } = new();
    public GunnerState Gunner { get; set; } = new();
    public DebugFlags Debug { get; } = new();
    public List<PinnedEntity> PinnedEntities { get; set; } = new();
    public List<MapResourceZone> ResourceZones { get; set; } = new();
    public const int MaxPins = 3;

    // Backwards-compatible facade properties
    public List<Contact> Contacts { get => ContactsState.Items; set => ContactsState.Items = value; }
    public List<OverlayRequest> Overlays { get => OverlayState.Items; set => OverlayState.Items = value; }
    public RouteState Route { get => Navigation.Route; set => Navigation.Route = value; }
    public List<GameEvent> ActiveEvents { get => ContactsState.ActiveEvents; set => ContactsState.ActiveEvents = value; }
    public bool ShowStarMapOnMainScreen { get => OverlayState.ShowStarMap; set => OverlayState.ShowStarMap = value; }
    public bool ShowTacticalOnMainScreen { get => OverlayState.ShowTactical; set => OverlayState.ShowTactical = value; }
    public bool ShowRunMapOnMainScreen { get => Run.ShowRunMap; set => Run.ShowRunMap = value; }
    public bool RunActive { get => Run.IsActive; set => Run.IsActive = value; }
    public RunStateData? ActiveRunState { get => Run.State; set => Run.State = value; }
    public RunDefinition? ActiveRunDefinition { get => Run.Definition; set => Run.Definition = value; }

    public Dictionary<string, object> GetStateForRole(StationRole role)
    {
        var data = new Dictionary<string, object>();

        switch (role)
        {
            case StationRole.CaptainNav:
                data["mission"] = Mission;
                data["hull_integrity"] = Ship.HullIntegrity;
                data["flight_mode"] = Ship.FlightMode.ToString();
                data["engagement_rule"] = Engagement.ToString();
                data["overlays"] = Overlays.FindAll(o => !o.Dismissed);
                data["pending_decisions"] = Mission.PendingDecisions.FindAll(d => !d.IsResolved);
                data["active_events"] = ActiveEvents.FindAll(e => e.IsActive);
                data["log"] = Mission.Log;
                data["systems_summary"] = GetSystemsSummary();
                data["ship_x"] = Ship.PositionX;
                data["ship_y"] = Ship.PositionY;
                data["ship_z"] = Ship.PositionZ;
                data["speed_level"] = Ship.SpeedLevel;
                data["route"] = Route;
                data["contacts"] = Contacts.FindAll(c =>
                    c.Discovery == DiscoveryState.Scanned && c.ReleasedToNav && !c.IsDestroyed);
                data["sensor_range"] = CalculateSensorRange();
                data["drive_energy"] = Ship.Energy.Drive;
                data["drive_status"] = Ship.Systems[SystemId.Drive].Status.ToString();
                data["mission_phase"] = Mission.Phase.ToString();
                data["mission_title"] = Mission.MissionTitle;
                data["use_structured_phases"] = Mission.UseStructuredMissionPhases;
                data["star_map_on_main_screen"] = ShowStarMapOnMainScreen;
                data["resource_zones"] = ResourceZones.FindAll(z => z.Discovery != "Hidden");
                break;

            case StationRole.Engineer:
                data["energy"] = Ship.Energy;
                data["systems"] = Ship.Systems;
                data["hull_integrity"] = Ship.HullIntegrity;
                data["flight_mode"] = Ship.FlightMode.ToString();
                data["active_events"] = ActiveEvents.FindAll(e => e.IsActive);
                data["spare_parts"] = ActiveRunState?.Resources.GetValueOrDefault(
                    RunResourceIds.SpareParts, 0) ?? 0;
                break;

            case StationRole.Tactical:
                data["contacts"] = Contacts.FindAll(c => c.Discovery != DiscoveryState.Hidden && !c.IsDestroyed);
                data["sensor_energy"] = Ship.Energy.Sensors;
                data["sensor_status"] = Ship.Systems[SystemId.Sensors].Status.ToString();
                data["sensor_range"] = CalculateSensorRange();
                data["active_events"] = ActiveEvents.FindAll(e => e.IsActive);
                data["ship_x"] = Ship.PositionX;
                data["ship_y"] = Ship.PositionY;
                data["ship_z"] = Ship.PositionZ;
                data["tactical_on_main_screen"] = ShowTacticalOnMainScreen;
                data["probes"] = ContactsState.ActiveProbes;
                data["probe_charges"] = ContactsState.ProbeCharges;
                data["probe_max_charges"] = ContactsState.MaxProbeCharges;
                data["probe_recharge_timer"] = ContactsState.ProbeRechargeTimer;
                data["probe_recharge_time"] = ContactsState.ProbeRechargeTime;
                data["designation_count"] = Contacts.FindAll(c => c.IsDesignated).Count;
                data["active_sensors"] = ContactsState.ActiveSensors;
                data["pinned_entities"] = PinnedEntities;
                data["max_pins"] = MaxPins;
                data["resource_zones"] = ResourceZones.FindAll(z => z.Discovery != "Hidden");
                break;

            case StationRole.Gunner:
                data["contacts"] = Contacts.FindAll(c =>
                    c.Discovery == DiscoveryState.Scanned && !c.IsDestroyed);
                data["selected_target"] = Gunner.SelectedTargetId ?? "";
                data["target_lock_progress"] = Gunner.TargetLockProgress;
                data["weapon_mode"] = Gunner.Mode.ToString();
                data["weapon_heat"] = Ship.Systems[SystemId.Weapons].Heat;
                data["weapon_status"] = Ship.Systems[SystemId.Weapons].Status.ToString();
                data["weapon_energy"] = Ship.Energy.Weapons;
                data["fire_cooldown"] = Gunner.FireCooldown;
                data["is_defensive_mode"] = Gunner.IsDefensiveMode;
                data["ship_x"] = Ship.PositionX;
                data["ship_y"] = Ship.PositionY;
                data["ship_z"] = Ship.PositionZ;
                data["engagement_rule"] = Engagement.ToString();
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

        AppendRunData(data, role);
        return data;
    }

    private void AppendRunData(Dictionary<string, object> data, StationRole role)
    {
        if (!RunActive || ActiveRunState == null || ActiveRunDefinition == null)
            return;

        var st = ActiveRunState;
        var def = ActiveRunDefinition;

        data["run_active"] = true;
        data["run_id"] = st.RunId;
        data["campaign_seed"] = st.CampaignSeed;
        data["run_current_node_id"] = st.CurrentNodeId ?? "";
        data["run_current_depth"] = st.CurrentDepth;
        data["run_resources"] = st.Resources.ToDictionary(k => k.Key, k => k.Value);
        data["run_flags"] = st.Flags.ToList();
        data["run_visited"] = new List<string>(st.VisitedNodeIds);

        string lastRes = "";
        if (!string.IsNullOrEmpty(st.LastResolvedNodeId) &&
            st.NodeStates.TryGetValue(st.LastResolvedNodeId, out var lastRt))
            lastRes = lastRt.Resolution.ToString();
        data["run_last_resolution"] = lastRes;

        var nodes = def.Nodes.Values.Select(n =>
        {
            st.NodeStates.TryGetValue(n.Id, out var nrt);
            var nk = nrt?.Knowledge ?? NodeKnowledgeState.Unknown;
            var hide = nk is NodeKnowledgeState.Unknown or NodeKnowledgeState.Detected;
            return new Dictionary<string, object>
            {
                ["id"] = n.Id,
                ["title"] = hide ? "" : n.Title,
                ["type"] = hide ? "?" : n.Type.ToString(),
                ["depth"] = n.Depth,
                ["risk"] = n.RiskRating,
                ["layout_x"] = n.LayoutX,
                ["layout_y"] = n.LayoutY,
                ["state"] = nrt != null ? nrt.State.ToString() : "",
                ["knowledge"] = nk.ToString(),
                ["next"] = new List<string>(n.NextNodeIds),
            };
        }).ToList();
        data["run_nodes"] = nodes;

        if (role == StationRole.CaptainNav)
        {
            if (!string.IsNullOrEmpty(st.CurrentNodeId) &&
                def.Nodes.TryGetValue(st.CurrentNodeId, out var cur))
                data["run_nav_preview"] = string.Join(", ", cur.NextNodeIds);
            else
                data["run_nav_preview"] = "";
        }

        if (role is StationRole.Engineer or StationRole.Tactical or StationRole.Gunner)
        {
            data["run_placeholder"] = true;
        }
    }

    private Dictionary<string, string> GetSystemsSummary()
    {
        var summary = new Dictionary<string, string>();
        foreach (var kvp in Ship.Systems)
            summary[kvp.Key.ToString()] = kvp.Value.Status.ToString();
        return summary;
    }

    private float CalculateSensorRange() =>
        ShipCalculations.CalculateSensorRange(Ship, ContactsState.ActiveSensors);

    private bool IsInSensorRange(Contact c)
    {
        float range = CalculateSensorRange();
        float dx = c.PositionX - Ship.PositionX;
        float dy = c.PositionY - Ship.PositionY;
        return dx * dx + dy * dy <= range * range;
    }

    public Dictionary<string, object> GetMainScreenState()
    {
        var data = new Dictionary<string, object>();
        data["ship_x"] = Ship.PositionX;
        data["ship_y"] = Ship.PositionY;
        data["ship_z"] = Ship.PositionZ;
        data["flight_mode"] = Ship.FlightMode.ToString();
        data["speed_level"] = Ship.SpeedLevel;
        data["hull_integrity"] = Ship.HullIntegrity;
        data["contacts"] = Contacts.FindAll(c => c.IsVisibleOnMainScreen && c.Discovery == DiscoveryState.Scanned);
        data["overlays"] = Overlays.FindAll(o => o.ApprovedByCaptain && !o.Dismissed);
        data["mission_phase"] = Mission.Phase.ToString();
        data["mission_title"] = Mission.MissionTitle;
        data["use_structured_phases"] = Mission.UseStructuredMissionPhases;
        data["active_events"] = ActiveEvents.FindAll(e => e.IsActive);
        data["route"] = Route;
        data["pinned_entities"] = PinnedEntities;
        return data;
    }
}
