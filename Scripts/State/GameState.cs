using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Linq;
using Godot;
using SpacedOut.Mission;
using SpacedOut.Orchestration;
using SpacedOut.Poi;
using SpacedOut.Run;
using SpacedOut.Shared;
using SpacedOut.Tactical;

namespace SpacedOut.State;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum FlightMode { Cruise, Approach, Evasive, Hold }

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum TargetTrackingMode { None, Follow, Orbit, KeepAtRange }

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
public enum ToolMode { Combat, Mining }

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum EngagementRule { Standard, Aggressive, Defensive }

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum AgentBehaviorMode { Idle, Patrol, Transit, Guard, Intercept, Attack, Flee, Destroyed }

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
    public string MissionId { get; set; } = "";
    public string MissionTitle { get; set; } = "";
    public MissionPhase Phase { get; set; } = MissionPhase.Operational;
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

    /// <summary>When true, the sector exit (Sprungkoordinaten) has been revealed — either via
    /// scripted mission progress (tutorial: relay scan) or a probe hit on the exit marker
    /// (M1b procedural sectors).</summary>
    public bool JumpCoordinatesUnlocked { get; set; }

    /// <summary>When true, the current scripted mission gates the exit behind a specific event
    /// (e.g. tutorial: relay extraction). In that mode, probes do NOT reveal the exit marker.
    /// False for procedural (non-scripted) sectors: probe hits reveal the exit.</summary>
    public bool ScriptLocksExitUntilScan { get; set; }

    /// <summary>True when the active sector has no mission script (procedural run node).
    /// Used with <see cref="SpacedOut.Mission.SectorJumpCompletion"/> for exit completion rules.</summary>
    public bool ProceduralSectorMission { get; set; }

    // ── M1b: sector harvest tally ─────────────────────────────────
    /// <summary>Snapshot of RunStateData.Resources at mission start — used by the HUD to show
    /// the current sector's harvest delta.</summary>
    public Dictionary<string, int> MissionStartResourcesSnapshot { get; set; } = new();

    // ── M5: dock state (only populated in Station sectors) ────────
    /// <summary>True while the ship is parked near a <c>station_dock</c> contact in a Station sector.</summary>
    public bool Docked { get; set; }
    /// <summary>Id of the dock contact we're currently docked with.</summary>
    public string? DockedContactId { get; set; }
    /// <summary>Per-station prices + repair rate; null outside Station sectors.</summary>
    public StationInventory? Dock { get; set; }
    /// <summary>Live distance (map units) from the ship to the <c>station_dock</c> contact; -1 outside Station sectors.</summary>
    public float DockDistance { get; set; } = -1f;

    // ── M4: Node-Event (Pre-Sector) ───────────────────────────────
    /// <summary>
    /// True while a <see cref="NodeEventCatalog"/> event fires at the run-map <em>before</em> the sector is built.
    /// While true, sector generation is deferred and all HUDs show a "Funkspruch" banner instead of sector info.
    /// Cleared in <see cref="MissionOrchestrator"/> after the decision is resolved.
    /// </summary>
    public bool PreSectorEventActive { get; set; }

    /// <summary>Id of the <see cref="NodeEvent"/> currently pending at the pre-sector overlay (empty when none).</summary>
    public string PendingPreSectorEventId { get; set; } = "";

    /// <summary>Run-node that owns <see cref="PendingPreSectorEventId"/> — used by the resolver to either skip the sector or build it.</summary>
    public string PendingPreSectorNodeId { get; set; } = "";

    /// <summary>Title of the pending pre-sector event — shown in Godot HUD/Captain banner.</summary>
    public string PendingPreSectorEventTitle { get; set; } = "";

    /// <summary>Risk rating of the active run node (0 = safe, higher = more dangerous). Used by
    /// <see cref="SpacedOut.Mission.DecisionEffectResolver"/> to filter risk-gated spawns
    /// (see <see cref="DeferredAgentSpawn.MinRisk"/>).</summary>
    public int NodeRiskRating { get; set; }

    /// <summary>Latest Captain/CaptainNav line for main-screen comms highlight.</summary>
    public string LastCommsHighlight { get; set; } = "";

    /// <summary>Latest decision resolution summary for main-screen HUD.</summary>
    public string LastDecisionHighlight { get; set; } = "";
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

    /// <summary>FTL-style thematic hint shown under the option in Captain/Nav UI — never reveals exact numbers.</summary>
    public string FlavorHint { get; set; } = "";

    /// <summary>
    /// Prosatext zur Auflösung für den Kommandanten-Toast (nach Wahl). Never serialized — avoids spoilers in state.
    /// </summary>
    [JsonIgnore]
    public string ResolutionNarrative { get; set; } = "";

    /// <summary>Server-side effects applied on resolution. Never serialized to clients.</summary>
    [JsonIgnore]
    public DecisionEffects? Effects { get; set; }
}

/// <summary>
/// Server-side effect bundle applied by <see cref="SpacedOut.Mission.DecisionEffectResolver"/>
/// when a <see cref="DecisionOption"/> is resolved. Only the numbers are server-authoritative;
/// UI shows <see cref="DecisionOption.FlavorHint"/> instead to keep choices thematic.
/// </summary>
public class DecisionEffects
{
    /// <summary>Delta per resource id (see <see cref="RunResourceIds"/>), applied to <c>ActiveRunState.Resources</c>.</summary>
    public Dictionary<string, int> ResourceDeltas { get; set; } = new();

    /// <summary>Delta to <see cref="ShipState.HullIntegrity"/> (clamped to 0..100).</summary>
    public float HullDelta { get; set; }

    public List<string> FlagsToSet { get; set; } = new();
    public List<string> FlagsToClear { get; set; } = new();

    /// <summary>Heat / status overrides for ship systems.</summary>
    public List<SystemEffect> SystemEffects { get; set; } = new();

    /// <summary>Agents queued to spawn (re-uses <see cref="MissionController"/> deferred-spawn path).</summary>
    public List<DeferredAgentSpawn> SpawnAgents { get; set; } = new();

    /// <summary>POIs queued to spawn (resolved by <see cref="SpacedOut.Orchestration.MissionOrchestrator"/> into 3D entities + <see cref="Contact"/>).</summary>
    public List<DeferredPoiSpawn> SpawnPois { get; set; } = new();

    /// <summary>When true and this is a Pre-Sector event, the sector is skipped and the run node resolves as <see cref="Run.NodeResolution.Success"/>.</summary>
    public bool SkipSector { get; set; }

    /// <summary>Short captain-log line appended after the effects are applied. Null/empty = no log.</summary>
    public string? LogSummary { get; set; }
}

/// <summary>Whether a mission log line should raise a floating toast on web stations.</summary>
public enum MissionLogWebToast
{
    /// <summary>Client applies defaults (e.g. skip duplicate Gunner lines for Gunner station).</summary>
    Unspecified = 0,
    /// <summary>Main screen / in-panel log only — no web popup.</summary>
    LogOnly = 1,
    /// <summary>Standard toast.</summary>
    Toast = 2,
    /// <summary>Longer display (phase changes, mission end, key beats).</summary>
    ToastProminent = 3,
}

public class MissionLogEntry
{
    public float Timestamp { get; set; }
    public string Source { get; set; } = "";
    public string Message { get; set; } = "";
    /// <summary>Web HUD: suppress or emphasize floating toasts for this line.</summary>
    public MissionLogWebToast WebToast { get; set; }
}

/// <summary>Maps mission log <see cref="MissionLogEntry.Source"/> to which station should see web toasts.</summary>
public static class MissionLogRouting
{
    public static bool IsVisibleToRole(string source, StationRole role)
    {
        if (source == "System")
            return role is StationRole.CaptainNav or StationRole.Engineer or StationRole.Tactical
                or StationRole.Gunner;

        return role switch
        {
            StationRole.CaptainNav => source is "CaptainNav" or "Captain" or "Navigation" or "Navigator",
            StationRole.Engineer => source == "Engineer",
            StationRole.Tactical => source == "Tactical",
            StationRole.Gunner => source == "Gunner",
            _ => false,
        };
    }
}

public class Contact
{
    public string Id { get; set; } = "";
    /// <summary>Level-gen asset id (e.g. poi_derelict_probe). Empty for agents / legacy contacts.</summary>
    public string AssetId { get; set; } = "";
    public ContactType Type { get; set; } = ContactType.Unknown;
    public string DisplayName { get; set; } = "";
    public float PositionX { get; set; }
    public float PositionY { get; set; }
    public float PositionZ { get; set; } = 500f;
    /// <summary>Fixed tier 0–5 (none … critical). Set from sector/mission data only.</summary>
    public int ThreatLevel { get; set; }
    public float ScanProgress { get; set; }
    public bool IsVisibleOnMainScreen { get; set; }
    public bool IsScanning { get; set; }
    public float VelocityX { get; set; }
    public float VelocityY { get; set; }
    public float VelocityZ { get; set; }

    public DiscoveryState Discovery { get; set; } = DiscoveryState.Hidden;
    /// <summary>
    /// Ghost-memory end time (<see cref="Mission.MissionState.ElapsedTime"/>). Negative = still in live probe phase
    /// (timer starts when coverage ends). When non-negative and elapsed &gt;= value, Probed reverts to Hidden (or Detected in range).
    /// </summary>
    public float ProbeExpiry { get; set; }
    /// <summary>Frozen position captured when live probe coverage ends (not on deploy).</summary>
    public float SnapshotX { get; set; }
    public float SnapshotY { get; set; }
    public float SnapshotZ { get; set; }
    /// <summary>Previous tick: contact was inside an active probe reveal ring (simulation only).</summary>
    [JsonIgnore]
    public bool ProbeCoveredLastFrame { get; set; }
    /// <summary>When true, this contact is visible on the navigator's map (must be explicitly released by tactical).</summary>
    public bool ReleasedToNav { get; set; }
    /// <summary>Known objects (stations, mission targets, beacons) that bypass the fog-of-war pipeline entirely.</summary>
    public bool PreRevealed { get; set; }
    /// <summary>
    /// When true, a Detected blip is drawn out to full sensor range; when false, only the inner range ring (sensor/3).
    /// </summary>
    public bool RadarShowDetectedInFullRange { get; set; }
    /// <summary>
    /// When true, <see cref="DiscoveryState.Detected"/> is not cleared to Hidden when the ship moves beyond passive sensor range (mission sensor lock).
    /// </summary>
    public bool PersistDetectedBeyondSensorRange { get; set; }
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

    // ── POI interaction state ────────────────────────────────────
    /// <summary>Blueprint id from PoiBlueprintCatalog (empty = not a POI).</summary>
    public string PoiType { get; set; } = "";
    public PoiPhase PoiPhase { get; set; } = PoiPhase.None;
    /// <summary>Progress of the current POI phase (0-100).</summary>
    public float PoiProgress { get; set; }
    /// <summary>Reward profile chosen at spawn (for multi-variant POIs like drifting_pod).</summary>
    public string PoiRewardProfile { get; set; } = "";
    /// <summary>True when Tactical analysis has revealed a trap.</summary>
    public bool PoiTrapRevealed { get; set; }
    /// <summary>True while the Gunner is actively drilling this POI.</summary>
    public bool PoiDrilling { get; set; }
    /// <summary>True while the Engineer is actively extracting from this POI.</summary>
    public bool PoiExtracting { get; set; }
    /// <summary>Remaining seconds before instability causes failure (0 = no timer).</summary>
    public float PoiInstabilityTimer { get; set; }
    /// <summary>True while the Tactical is actively analyzing this POI.</summary>
    public bool PoiAnalyzing { get; set; }

    /// <summary>AI behavior state for agent-controlled contacts (null for static/scripted contacts).</summary>
    [JsonIgnore]
    public AgentState? Agent { get; set; }

    /// <summary>
    /// Combat removal: no drift, hidden from main-screen contact lists. Not used for loot-wreck conversion.
    /// </summary>
    public void ApplyCombatDestruction()
    {
        IsDestroyed = true;
        VelocityX = VelocityY = VelocityZ = 0;
        IsVisibleOnMainScreen = false;
    }
}

public class AgentState
{
    public string AgentType { get; set; } = "";
    public AgentBehaviorMode Mode { get; set; }
    public float AnchorX { get; set; }
    public float AnchorY { get; set; }
    public float AnchorZ { get; set; } = 500f;
    public float DestinationX { get; set; }
    public float DestinationY { get; set; }
    public float DetectionRadius { get; set; } = 250f;
    public float FleeThreshold { get; set; } = 0.2f;
    public float OrbitAngle { get; set; }
    public float BaseSpeed { get; set; }
    /// <summary>Enemy gunnery skill (0–1) from agent definition; used for hit chance vs. the player.</summary>
    public float WeaponAccuracy { get; set; } = 0.55f;
    public float ShieldAbsorption { get; set; }
    public float ModeTimer { get; set; }
    public int OrbitDirection { get; set; } = 1;
    public float PhaseOffset { get; set; }

    /// <summary>Multiplier for attack ideal standoff (applied to AttackRange × 0.7). Set at spawn.</summary>
    public float AttackIdealDistFactor { get; set; } = 1f;
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
    /// <summary>When false, event is omitted from Godot main screen &quot;Aktive Ereignisse&quot; (still in CaptainNav / other stations).</summary>
    public bool ShowOnMainScreen { get; set; } = true;
}

public class GunnerState
{
    public string? SelectedTargetId { get; set; }
    public float TargetLockProgress { get; set; }
    public WeaponMode Mode { get; set; } = WeaponMode.Precision;
    public ToolMode Tool { get; set; } = ToolMode.Combat;
    public bool IsDefensiveMode { get; set; }
    /// <summary>When true, mission tick fires automatically while locked (combat tool only).</summary>
    public bool IsAutofire { get; set; }
    public float FireCooldown { get; set; }

    /// <summary>Increments on each shot (hit/miss); clients use to show one-shot UI feedback.</summary>
    public int LastShotFeedbackSeq { get; set; }

    /// <summary>Short German line for gunner HUD toast (last shot only).</summary>
    public string LastShotFeedbackText { get; set; } = "";

    /// <summary>Contact id currently being drilled (null = not drilling).</summary>
    public string? DrillTargetId { get; set; }

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
    [JsonIgnore]
    public CombatFxState CombatFx { get; } = new();
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
    public RunOutcome RunOutcome { get => Run.Outcome; set => Run.Outcome = value; }
    public bool ShowMainMenu { get => Run.ShowMainMenu; set => Run.ShowMainMenu = value; }

    // ── M7 (Meta-Progression) ─────────────────────────────────────
    /// <summary>Sternenstaub gained at the last run-end — surfaced in the <c>RunEndOverlay</c>.</summary>
    public int LastRunStardustGain { get; set; }
    /// <summary>Active loadout perk id for the current run (null when none active or no run in progress).</summary>
    public string? ActivePerkId { get; set; }
    /// <summary>Display name of the active perk (cached so web clients don't need the catalog).</summary>
    public string ActivePerkName { get; set; } = "";
    /// <summary>True while the meta profile/upgrade overlay is visible.</summary>
    public bool ShowProfile { get; set; }

    /// <summary>Raised for every line appended to <see cref="MissionState.Log"/> (including mission controller paths).</summary>
    public event Action<MissionLogEntry>? MissionLogEntryAdded;

    public void AddMissionLogEntry(MissionLogEntry entry)
    {
        Mission.Log.Add(entry);
        if (entry.Source == "CaptainNav")
            Mission.LastCommsHighlight = entry.Message ?? "";
        MissionLogEntryAdded?.Invoke(entry);
    }

    /// <summary>Raised when a new entry is appended to <see cref="MissionState.PendingDecisions"/> (Kommandant-Web-Toasts).</summary>
    public event Action<MissionDecision>? PendingDecisionAdded;

    public void AddPendingDecision(MissionDecision decision)
    {
        Mission.PendingDecisions.Add(decision);
        PendingDecisionAdded?.Invoke(decision);
    }

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
                var tt = Navigation.TargetTracking;
                data["target_tracking"] = new Dictionary<string, object>
                {
                    ["mode"] = tt.Mode.ToString(),
                    ["contact_id"] = tt.TrackedContactId ?? "",
                    ["range"] = tt.Range,
                    ["orbit_clockwise"] = tt.OrbitClockwise,
                };
                data["contacts"] = Contacts.FindAll(c =>
                    c.Discovery == DiscoveryState.Scanned && !c.IsDestroyed && IsContactAvailableToCaptainNav(c));
                data["sensor_range"] = CalculateSensorRange();
                data["drive_energy"] = Ship.Energy.Drive;
                data["drive_status"] = Ship.Systems[SystemId.Drive].Status.ToString();
                data["mission_phase"] = Mission.Phase.ToString();
                data["mission_title"] = Mission.MissionTitle;
                data["use_structured_phases"] = Mission.UseStructuredMissionPhases;
                data["star_map_on_main_screen"] = ShowStarMapOnMainScreen;
                data["sector_jump_available"] = SectorJumpCompletion.IsReady(this);
                data["resource_zones"] = GameFeatures.ResourceZonesEnabled
                    ? ResourceZones.FindAll(z => z.Discovery != "Hidden")
                    : new List<MapResourceZone>();
                break;

            case StationRole.Engineer:
                data["energy"] = Ship.Energy;
                data["systems"] = Ship.Systems;
                data["hull_integrity"] = Ship.HullIntegrity;
                data["flight_mode"] = Ship.FlightMode.ToString();
                data["active_events"] = ActiveEvents.FindAll(e => e.IsActive);
                data["spare_parts"] = ActiveRunState?.Resources.GetValueOrDefault(
                    RunResourceIds.SpareParts, 0) ?? 0;
                data["poi_contacts"] = Contacts
                    .FindAll(c =>
                        !string.IsNullOrEmpty(c.PoiType) && c.Discovery == DiscoveryState.Scanned
                        && c.PoiPhase != PoiPhase.None && c.PoiPhase != PoiPhase.Complete
                        && c.PoiPhase != PoiPhase.Failed && !c.IsDestroyed)
                    .Select(c =>
                    {
                        var bp = PoiBlueprintCatalog.GetOrNull(c.PoiType);
                        return new Dictionary<string, object>
                        {
                            ["Id"] = c.Id,
                            ["DisplayName"] = c.DisplayName,
                            ["PositionX"] = c.PositionX,
                            ["PositionY"] = c.PositionY,
                            ["PoiPhase"] = c.PoiPhase.ToString(),
                            ["PoiProgress"] = c.PoiProgress,
                            ["PoiExtracting"] = c.PoiExtracting,
                            ["UsesTractorBeam"] = bp?.UsesTractorBeam ?? false,
                            ["RequiresDrill"] = bp?.RequiresDrill ?? false,
                        };
                    })
                    .ToList();
                data["ship_x"] = Ship.PositionX;
                data["ship_y"] = Ship.PositionY;
                break;

            case StationRole.Tactical:
                var tacticalContacts = Contacts.FindAll(c => c.Discovery != DiscoveryState.Hidden && !c.IsDestroyed);
                data["contacts"] = tacticalContacts;
                var contactActions = new Dictionary<string, List<ActionDescriptor>>();
                foreach (var c in tacticalContacts)
                    contactActions[c.Id] = ContactActionResolver.Resolve(c, this);
                data["contact_actions"] = contactActions;
                data["sensor_energy"] = Ship.Energy.Sensors;
                data["sensor_status"] = Ship.Systems[SystemId.Sensors].Status.ToString();
                data["sensor_range"] = CalculateSensorRange();
                data["sensor_range_nominal"] = ShipCalculations.CalculateSensorRangeIgnoringSystemStatus(
                    Ship, ContactsState.ActiveSensors);
                data["sensor_status_mult_range"] = ShipCalculations.GetStatusMultiplier(
                    Ship.Systems[SystemId.Sensors].Status);
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
                data["resource_zones"] = GameFeatures.ResourceZonesEnabled
                    ? ResourceZones.FindAll(z => z.Discovery != "Hidden")
                    : new List<MapResourceZone>();
                break;

            case StationRole.Gunner:
                data["contacts"] = Gunner.Tool == ToolMode.Mining
                    ? Contacts.FindAll(GunnerContactRules.IsDrillablePoiForGunnerList)
                    : Contacts.FindAll(c =>
                        c.Discovery == DiscoveryState.Scanned && !c.IsDestroyed
                        && GunnerContactRules.IsSelectableForCombat(c));
                data["selected_target"] = Gunner.SelectedTargetId ?? "";
                data["target_lock_progress"] = Gunner.TargetLockProgress;
                data["weapon_mode"] = Gunner.Mode.ToString();
                data["tool_mode"] = Gunner.Tool.ToString();
                data["drill_target"] = Gunner.DrillTargetId ?? "";
                data["weapon_heat"] = Ship.Systems[SystemId.Weapons].Heat;
                data["weapon_status"] = Ship.Systems[SystemId.Weapons].Status.ToString();
                data["weapon_energy"] = Ship.Energy.Weapons;
                data["fire_cooldown"] = Gunner.FireCooldown;
                data["is_defensive_mode"] = Gunner.IsDefensiveMode;
                data["is_autofire"] = Gunner.IsAutofire;
                data["gunner_shot_feedback_seq"] = Gunner.LastShotFeedbackSeq;
                data["gunner_shot_feedback"] = Gunner.LastShotFeedbackText;
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
        AppendDockData(data);
        AppendEventBanners(data, role);
        return data;
    }

    /// <summary>
    /// M4: broadcast minimal indicators for an active <see cref="NodeEvent"/>:
    /// <list type="bullet">
    ///   <item><c>mission_pending_decision</c> (bool) — any undecided <see cref="MissionDecision"/> pending.</item>
    ///   <item><c>mission_pre_sector_event_active</c> (bool) — Pre-Sector overlay dialog is open.</item>
    ///   <item><c>mission_pre_sector_event_title</c> (string) — short title for Captain banner.</item>
    /// </list>
    /// Details (options, effects) are only exposed to <see cref="StationRole.CaptainNav"/> via the regular
    /// <c>pending_decisions</c> channel — other roles only see the indicator flags.
    /// </summary>
    private void AppendEventBanners(Dictionary<string, object> data, StationRole role)
    {
        bool pendingDecision = Mission.PendingDecisions.Exists(d => !d.IsResolved);
        data["mission_pending_decision"] = pendingDecision;
        data["mission_pre_sector_event_active"] = Mission.PreSectorEventActive;
        data["mission_pre_sector_event_title"] = Mission.PreSectorEventActive
            ? Mission.PendingPreSectorEventTitle
            : "";
    }

    /// <summary>
    /// Common dock block (Station sectors only). Added for every role so web clients
    /// can uniformly show dock status / prices.
    /// </summary>
    private void AppendDockData(Dictionary<string, object> data)
    {
        var m = Mission;
        data["mission_docked"] = m.Docked;
        data["docked_contact_id"] = m.DockedContactId ?? "";
        data["dock_distance"] = m.DockDistance;
        if (m.Dock != null)
        {
            var d = m.Dock;
            data["dock"] = new Dictionary<string, object>
            {
                ["FuelPrice"] = d.FuelPrice,
                ["PartsPrice"] = d.PartsPrice,
                ["DataPrice"] = d.DataPrice,
                ["FuelSellPrice"] = d.SellPriceFor(RunResourceIds.Fuel),
                ["PartsSellPrice"] = d.SellPriceFor(RunResourceIds.SpareParts),
                ["DataSellPrice"] = d.SellPriceFor(RunResourceIds.ScienceData),
                ["HullPerPart"] = d.HullPerPart,
                ["Available"] = true,
                ["ProximityRange"] = 60,
                ["MaxSpeedLevel"] = 2,
            };
        }
        else
        {
            data["dock"] = new Dictionary<string, object> { ["Available"] = false };
        }
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
        data["run_scan_cost"] = RunController.ScanCostScience;

        int effectiveDepth = ComputeEffectiveDepth(st, def);
        int scanHorizon = effectiveDepth + RunController.MaxScanDepthAhead;

        var nodes = def.Nodes.Values.Select(n =>
        {
            st.NodeStates.TryGetValue(n.Id, out var nrt);
            var nk = nrt?.Knowledge ?? NodeKnowledgeState.Silhouette;
            bool sighted = nk != NodeKnowledgeState.Silhouette;
            bool scanned = nk == NodeKnowledgeState.Scanned;
            return new Dictionary<string, object>
            {
                ["id"] = n.Id,
                ["title"] = sighted ? n.Title : "",
                ["type"] = sighted ? n.Type.ToString() : "?",
                ["depth"] = n.Depth,
                ["risk"] = sighted ? n.RiskRating : 0,
                ["layout_x"] = n.LayoutX,
                ["layout_y"] = n.LayoutY,
                ["state"] = nrt != null ? nrt.State.ToString() : "",
                ["knowledge"] = nk.ToString(),
                ["scannable"] = n.Depth <= scanHorizon,
                ["next"] = new List<string>(n.NextNodeIds),
                ["fuel_cost"] = NodeEncounterConfig.GetFuelCostFor(n.Type),
                ["briefing_preview"] = scanned
                    ? MissionOrchestrator.GetBriefingPreviewForRunNode(n)
                    : "",
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

    /// <summary>
    /// Mirrors <see cref="RunController.CurrentDepth"/> for snapshot construction (no controller available here).
    /// </summary>
    private static int ComputeEffectiveDepth(RunStateData st, RunDefinition def)
    {
        if (!string.IsNullOrEmpty(st.CurrentNodeId)
            && def.Nodes.TryGetValue(st.CurrentNodeId, out var cn))
            return cn.Depth;
        if (!string.IsNullOrEmpty(st.LastResolvedNodeId)
            && def.Nodes.TryGetValue(st.LastResolvedNodeId, out var ln))
            return ln.Depth;
        return st.CurrentDepth;
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

    /// <summary>
    /// Navigator/Captain contact list: tactical <see cref="Contact.ReleasedToNav"/>, or hostile contacts
    /// that are fully scanned and currently within the ship sensor radius (no tactical release required).
    /// </summary>
    public bool IsContactAvailableToCaptainNav(Contact c)
    {
        if (c.ReleasedToNav) return true;
        if (c.Type != ContactType.Hostile || c.Discovery != DiscoveryState.Scanned || c.IsDestroyed)
            return false;
        return IsInSensorRange(c);
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
        data["contacts"] = Contacts.FindAll(c =>
            c.IsVisibleOnMainScreen && c.Discovery == DiscoveryState.Scanned && !c.IsDestroyed);
        data["overlays"] = Overlays.FindAll(o => o.ApprovedByCaptain && !o.Dismissed);
        data["mission_phase"] = Mission.Phase.ToString();
        data["mission_title"] = Mission.MissionTitle;
        data["use_structured_phases"] = Mission.UseStructuredMissionPhases;
        data["active_events"] = ActiveEvents.FindAll(e => e.IsActive && e.ShowOnMainScreen);
        data["route"] = Route;
        data["pinned_entities"] = PinnedEntities;
        return data;
    }
}
