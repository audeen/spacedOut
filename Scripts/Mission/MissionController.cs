using System;
using System.Collections.Generic;
using Godot;
using SpacedOut.Agents;
using SpacedOut.Run;
using SpacedOut.Shared;
using SpacedOut.State;
using SpacedOut.Tactical;

namespace SpacedOut.Mission;

public partial class MissionController : Node
{
    private GameState _state = null!;

    [Signal] public delegate void PhaseChangedEventHandler(string phase);
    [Signal] public delegate void EventTriggeredEventHandler(string eventId);
    [Signal] public delegate void MissionEndedEventHandler(int result);

    private readonly HashSet<string> _triggeredEvents = new();
    private readonly HashSet<int> _firedProximity = new();
    private readonly HashSet<int> _firedTimeTriggers = new();
    private NodeEncounterConfig? _encounterConfig;
    private MissionScript? _script;

    /// <summary>Runtime-only proximity triggers appended by <see cref="NodeEventCatalog"/> picks (parallel to <see cref="MissionScript.ProximityTriggers"/>).</summary>
    private readonly List<ProximityTrigger> _runtimeProximityTriggers = new();
    /// <summary>Runtime-only time triggers appended by <see cref="NodeEventCatalog"/> picks (parallel to <see cref="MissionScript.TimeTriggers"/>).</summary>
    private readonly List<TimeTrigger> _runtimeTimeTriggers = new();
    private readonly HashSet<int> _firedRuntimeProximity = new();
    private readonly HashSet<int> _firedRuntimeTimeTriggers = new();
    /// <summary>Runtime-only deferred spawns queued via <see cref="QueueDeferredSpawns"/> (e.g. from decision effects).</summary>
    private readonly List<DeferredAgentSpawn> _runtimeDeferredSpawns = new();
    /// <summary>
    /// Spawns queued from a <see cref="NodeEventTrigger.PreSector"/> decision before the next
    /// sector exists. Carried into the upcoming sector by <see cref="StartMission"/> and fired
    /// once the new sector is fully initialised. See <see cref="QueueSectorEntrySpawns"/>.
    /// </summary>
    private readonly List<DeferredAgentSpawn> _pendingSectorEntrySpawns = new();
    /// <summary>Trigger ids whose deferred spawns must auto-fire on the next <see cref="StartMission"/>.</summary>
    private readonly HashSet<string> _pendingSectorEntryTriggers = new();
    /// <summary>Runtime-only scripted decisions registered via <see cref="RegisterRuntimeDecision"/> (e.g. from <see cref="NodeEventCatalog"/>).</summary>
    private readonly Dictionary<string, ScriptedDecision> _runtimeDecisions = new();
    /// <summary>Runtime-only scripted events registered via <see cref="RegisterRuntimeEvent"/>.</summary>
    private readonly Dictionary<string, ScriptedEvent> _runtimeEvents = new();
    private bool _useStructuredPhases;

    private const float DefaultPhase1End = 60f;
    private const float DefaultPhase2End = 180f;
    private const float DefaultPhase3End = 420f;

    private float _damageMultiplier = 1f;

    /// <summary>
    /// Director-Heartbeat: wird vom <see cref="Update"/>-Pfad gefeuert, sobald
    /// <see cref="State.MissionState.ElapsedTime"/> die nächste 30-Sekunden-Schwelle erreicht.
    /// Wiring übernimmt <see cref="SpacedOut.Orchestration.MissionOrchestrator"/>.
    /// Argument: Sekunden seit dem vorherigen Heartbeat-Tick (für Diagnose).
    /// </summary>
    public Action<float>? OnHeartbeatHook { get; set; }

    /// <summary>
    /// Wird gefeuert, sobald ein Hostile-Kontakt im aktiven Sektor zerstört wurde
    /// (Gunner-Kill und Agent-State-Übergang Destroyed). Argument: Contact-Id.
    /// </summary>
    public Action<string>? OnHostileDestroyedHook { get; set; }

    /// <summary>Mission-ElapsedTime in Sekunden (read-only Spiegel auf <see cref="State.MissionState.ElapsedTime"/>).</summary>
    public float ElapsedTime => _state?.Mission.ElapsedTime ?? 0f;

    /// <summary>Letzter Mission-ElapsedTime, an dem der Director-Heartbeat-Hook gefeuert wurde.</summary>
    private float _lastHeartbeatAtElapsed;

    /// <summary>Hostile-Kontakte, die im letzten Tick noch lebten (für Edge-Trigger des Destroyed-Hooks).</summary>
    private readonly HashSet<string> _trackedHostileAliveIds = new();

    /// <summary>Bereits an den Director gemeldete Destroyed-Hostiles (verhindert doppeltes Feuern).</summary>
    private readonly HashSet<string> _reportedDestroyedHostileIds = new();

    public void Initialize(GameState state)
    {
        _state = state;
    }

    public void ApplyEncounterConfig(NodeEncounterConfig? config)
    {
        _encounterConfig = config;
        _damageMultiplier = config != null ? config.DamageMultiplier : 1f;
        _useStructuredPhases = config?.UseStructuredPhases ?? false;
    }

    public void ApplyMissionScript(MissionScript? script)
    {
        _script = script;
        _state.Mission.ScriptLocksExitUntilScan =
            script?.PrimaryObjective?.HideExitUntilScanned ?? false;
        _state.Mission.ProceduralSectorMission = script == null;
    }

    public void StartMission()
    {
        _triggeredEvents.Clear();
        _state.MissionStarted = true;
        _state.Mission.ElapsedTime = 0;
        _lastHeartbeatAtElapsed = 0f;
        _trackedHostileAliveIds.Clear();
        _reportedDestroyedHostileIds.Clear();
        // Wipe leftover runtime spawns from previous sector — only sector-entry pending spawns
        // (queued from a PreSector decision below) survive into this fresh sector.
        _runtimeDeferredSpawns.Clear();
        _state.Mission.PrimaryObjective = ObjectiveStatus.InProgress;
        _state.Mission.SecondaryObjective = ObjectiveStatus.InProgress;
        _state.Mission.UseStructuredMissionPhases = _useStructuredPhases;

        // M1b: snapshot run resources so the HUD can show the sector harvest delta live.
        _state.Mission.MissionStartResourcesSnapshot.Clear();
        if (_state.ActiveRunState != null)
        {
            foreach (var kv in _state.ActiveRunState.Resources)
                _state.Mission.MissionStartResourcesSnapshot[kv.Key] = kv.Value;
        }

        _state.Ship.FlightMode = FlightMode.Cruise;
        _state.Ship.SpeedLevel = 2;
        // M7: respect MaxHullOverride when restoring run hull at mission start.
        var hullCap = _state.ActiveRunState?.MaxHullOverride ?? 100f;
        _state.Ship.HullIntegrity = MathF.Min(hullCap, _state.ActiveRunState?.CurrentHull ?? 100f);
        _state.Engagement = EngagementRule.Standard;
        _state.Gunner = new GunnerState();

        // MissionGenerator has already populated contacts, briefing & ship position.
        // Fall back to hardcoded contacts only when nothing was pre-populated.
        // Route waypoints are set by the navigator (no auto-waypoints).
        if (_state.Contacts.Count == 0)
            SetupContacts();
        if (string.IsNullOrEmpty(_state.Mission.MissionTitle) && _encounterConfig != null
            && !string.IsNullOrEmpty(_encounterConfig.MissionTitle))
            _state.Mission.MissionTitle = _encounterConfig.MissionTitle;
        if (string.IsNullOrEmpty(_state.Mission.MissionTitle))
            _state.Mission.MissionTitle = "Einsatz";

        if (string.IsNullOrEmpty(_state.Mission.BriefingText) && _encounterConfig != null
            && !string.IsNullOrEmpty(_encounterConfig.BriefingText))
            _state.Mission.BriefingText = _encounterConfig.BriefingText;
        if (string.IsNullOrEmpty(_state.Mission.BriefingText))
            _state.Mission.BriefingText = "Mission aktiv.";

        _state.AddMissionLogEntry(new MissionLogEntry
        {
            Timestamp = 0,
            Source = "System",
            Message = $"Mission gestartet: {_state.Mission.MissionTitle}",
            WebToast = MissionLogWebToast.ToastProminent,
        });

        ApplyScriptInitialConditions();

        if (_useStructuredPhases)
        {
            _state.Mission.Phase = MissionPhase.Briefing;
            TransitionToPhase(MissionPhase.Anflug);
        }
        else
        {
            _state.Mission.Phase = MissionPhase.Operational;
            _state.Mission.PhaseTimer = 0;
        }

        _state.Mission.ProceduralSectorMission = _script == null;

        FlushPendingSectorEntrySpawns();
    }

    /// <summary>
    /// Materialises spawns queued via <see cref="QueueSectorEntrySpawns"/> (from PreSector
    /// decisions) into the now-fully-initialised sector, then dispatches each unique trigger id
    /// once. Logs counts so the flow is traceable in Godot's console.
    /// </summary>
    private void FlushPendingSectorEntrySpawns()
    {
        if (_pendingSectorEntrySpawns.Count == 0 && _pendingSectorEntryTriggers.Count == 0) return;

        if (_pendingSectorEntrySpawns.Count > 0)
        {
            foreach (var s in _pendingSectorEntrySpawns)
                _runtimeDeferredSpawns.Add(s);
        }

        int spawnedTriggers = 0;
        foreach (var triggerId in _pendingSectorEntryTriggers)
        {
            if (string.IsNullOrEmpty(triggerId)) continue;
            SpawnDeferredAgents(triggerId);
            spawnedTriggers++;
        }

        GD.Print($"[MissionController] Flushed {_pendingSectorEntrySpawns.Count} pre-sector spawn(s) over {spawnedTriggers} trigger(s).");

        _pendingSectorEntrySpawns.Clear();
        _pendingSectorEntryTriggers.Clear();
    }

    private void ApplyScriptInitialConditions()
    {
        if (_script?.Initial == null) return;

        var initial = _script.Initial;
        _state.Ship.HullIntegrity = initial.HullIntegrity;
        _state.ContactsState.ProbeCharges = initial.ProbeCharges;

        foreach (var (sysId, over) in initial.Systems)
        {
            if (!_state.Ship.Systems.TryGetValue(sysId, out var sys)) continue;
            sys.Heat = over.Heat;
            if (over.Status.HasValue)
                sys.Status = over.Status.Value;
        }
    }

    private void SetupContacts()
    {
        _state.Contacts.Clear();

        _state.Contacts.Add(new Contact
        {
            Id = "research_vessel",
            Type = ContactType.Unknown,
            DisplayName = "Schwaches Signal",
            PositionX = 700,
            PositionY = 650,
            ThreatLevel = 0,
            ScanProgress = 0,
            Discovery = DiscoveryState.Scanned,
            ReleasedToNav = true,
            IsVisibleOnMainScreen = true,
            PreRevealed = true,
        });

        _state.Contacts.Add(new Contact
        {
            Id = "debris_field",
            Type = ContactType.Anomaly,
            DisplayName = "Trümmerfeld",
            PositionX = 400,
            PositionY = 350,
            ThreatLevel = 2,
            ScanProgress = 0,
            Discovery = DiscoveryState.Hidden,
        });

        _state.Contacts.Add(new Contact
        {
            Id = "patrol_drone",
            Type = ContactType.Unknown,
            DisplayName = "Bewegliches Objekt",
            PositionX = 650,
            PositionY = 200,
            ThreatLevel = 4,
            ScanProgress = 0,
            VelocityX = 8f,
            VelocityY = 0f,
            Discovery = DiscoveryState.Hidden,
        });
    }

    public void Update(float delta)
    {
        if (!_state.MissionStarted || _state.IsPaused) return;
        if (_state.Mission.Phase == MissionPhase.Ended) return;

        _state.Mission.ElapsedTime += delta;
        if (_useStructuredPhases)
            _state.Mission.PhaseTimer += delta;

        UpdateShipMovement(delta);
        UpdateSystems(delta);
        UpdateProbes(delta);
        UpdateProbedProbeCoverage();
        UpdateSensorVisibility();
        ClampHiddenExitUntilRelayUnlock();
        UpdateScanning(delta);
        UpdateWeaknessAnalysis(delta);
        UpdateGunner(delta);
        UpdatePoiInteractions(delta);
        UpdateEnemyAttacks(delta);
        UpdateOverlays(delta);
        UpdateAgents(delta);
        UpdateContactMovement(delta);
        UpdateDockProximity();
        if (_useStructuredPhases)
            CheckPhaseTransitions();
        CheckEvents();
        CheckScriptTriggers();
        TrackHostileDestructions();
        CheckDirectorHeartbeat();
        CheckEndConditions(delta);
    }

    /// <summary>
    /// Fires <see cref="OnHeartbeatHook"/> roughly every <c>HeartbeatIntervalSec</c> (30s by
    /// convention with <see cref="SpacedOut.Run.EscalatingDirector.HeartbeatIntervalSec"/>).
    /// Director-Implementation does the actual decision (cooldown, pool, cap).
    /// </summary>
    private void CheckDirectorHeartbeat()
    {
        if (OnHeartbeatHook == null) return;
        const float heartbeatIntervalSec = 30f;
        float elapsed = _state.Mission.ElapsedTime;
        if (elapsed - _lastHeartbeatAtElapsed < heartbeatIntervalSec) return;
        float dt = elapsed - _lastHeartbeatAtElapsed;
        _lastHeartbeatAtElapsed = elapsed;
        OnHeartbeatHook.Invoke(dt);
    }

    /// <summary>
    /// Edge-detect: feuert <see cref="OnHostileDestroyedHook"/> einmalig pro Hostile-Kontakt,
    /// sobald dieser nicht mehr lebt (HitPoints <= 0, IsDestroyed, Type-Wechsel auf Neutral durch
    /// Wreck-Conversion). Bewusst poll-basiert statt direkter Hook-Aufruf in
    /// <see cref="GunnerFireAction"/>, damit auch Wreck-Conversion und Debug-Kills erfasst werden.
    /// </summary>
    private void TrackHostileDestructions()
    {
        if (OnHostileDestroyedHook == null)
        {
            _trackedHostileAliveIds.Clear();
            return;
        }

        var aliveNow = new HashSet<string>();
        for (int i = 0; i < _state.Contacts.Count; i++)
        {
            var c = _state.Contacts[i];
            if (c.Type == ContactType.Hostile && !c.IsDestroyed && c.HitPoints > 0)
                aliveNow.Add(c.Id);
        }

        foreach (var id in _trackedHostileAliveIds)
        {
            if (aliveNow.Contains(id)) continue;
            if (_reportedDestroyedHostileIds.Contains(id)) continue;
            _reportedDestroyedHostileIds.Add(id);
            OnHostileDestroyedHook.Invoke(id);
        }

        _trackedHostileAliveIds.Clear();
        foreach (var id in aliveNow)
            _trackedHostileAliveIds.Add(id);
    }

    private const float DockProximityRange = 60f;
    private const int DockMaxSpeedLevel = 2; // SpeedLevel <= 2 (Cruise default) counts as "low enough to dock"

    /// <summary>
    /// M5: flips <see cref="MissionState.Docked"/> while the ship hovers close to a
    /// <c>station_dock</c> contact at low speed. Only active in Station sectors
    /// (<see cref="MissionState.Dock"/> != null).
    /// </summary>
    private void UpdateDockProximity()
    {
        var m = _state.Mission;
        if (m.Dock == null)
        {
            m.DockDistance = -1f;
            if (m.Docked)
            {
                m.Docked = false;
                m.DockedContactId = null;
            }
            return;
        }

        var dock = _state.Contacts.Find(c => c.Id == "station_dock");
        if (dock == null)
        {
            m.DockDistance = -1f;
            if (m.Docked)
            {
                m.Docked = false;
                m.DockedContactId = null;
            }
            return;
        }

        var ship = _state.Ship;
        float dx = ship.PositionX - dock.PositionX;
        float dy = ship.PositionY - dock.PositionY;
        float dz = ship.PositionZ - dock.PositionZ;
        float dist = MathF.Sqrt(dx * dx + dy * dy + dz * dz);
        m.DockDistance = dist;

        bool nowDocked = dist < DockProximityRange && ship.SpeedLevel <= DockMaxSpeedLevel;
        if (nowDocked == m.Docked) return;

        m.Docked = nowDocked;
        m.DockedContactId = nowDocked ? dock.Id : null;
        _state.AddMissionLogEntry(new MissionLogEntry
        {
            Timestamp = m.ElapsedTime,
            Source = "System",
            Message = nowDocked ? "Andocken bestätigt." : "Abgedockt.",
            WebToast = MissionLogWebToast.Toast,
        });
    }

    private void UpdateShipMovement(float delta)
    {
        var route = _state.Route;
        var ship = _state.Ship;
        var tt = _state.Navigation.TargetTracking;

        if (ship.FlightMode == FlightMode.Hold) return;

        if (tt.Mode != TargetTrackingMode.None)
        {
            var contact = ShipCalculations.FindValidTrackingContact(_state, tt);
            if (contact == null)
            {
                tt.Clear();
                _state.AddMissionLogEntry(new MissionLogEntry
                {
                    Timestamp = _state.Mission.ElapsedTime,
                    Source = "Navigation",
                    Message = "Zielverfolgung beendet (Kontakt nicht verfügbar)",
                    WebToast = MissionLogWebToast.LogOnly,
                });
            }
            else
            {
                if (tt.Mode == TargetTrackingMode.Orbit)
                    ShipCalculations.AdvanceOrbitAngle(tt, ship, delta);

                if (ShipCalculations.TryGetTrackingSteerTarget(tt, ship, contact, out var tx, out var ty, out var tz,
                        out var shouldMove))
                {
                    if (shouldMove)
                        ShipCalculations.StepShipToward(ship, tx, ty, tz, delta);
                    return;
                }
            }
        }

        var unreached = route.Waypoints.FindAll(w => !w.IsReached);
        if (unreached.Count == 0) return;

        var target = unreached[0];
        float dx = target.X - ship.PositionX;
        float dy = target.Y - ship.PositionY;
        float dz = target.Z - ship.PositionZ;
        float dist = MathF.Sqrt(dx * dx + dy * dy + dz * dz);

        if (dist < 5f)
        {
            target.IsReached = true;
            route.CurrentWaypointIndex++;
            _state.AddMissionLogEntry(new MissionLogEntry
            {
                Timestamp = _state.Mission.ElapsedTime,
                Source = "Navigation",
                Message = $"Waypoint erreicht: {target.Label}",
                WebToast = MissionLogWebToast.LogOnly,
            });
            return;
        }

        ShipCalculations.StepShipToward(ship, target.X, target.Y, target.Z, delta);
    }

    private void UpdateSystems(float delta)
    {
        var ship = _state.Ship;
        var dbg = _state.Debug;

        foreach (var kvp in ship.Systems)
        {
            var sys = kvp.Value;

            if (sys.CoolantCooldown > 0)
            {
                float cdSpeed = dbg.NoCooldowns ? 10f : 1f;
                sys.CoolantCooldown = Math.Max(0, sys.CoolantCooldown - delta * cdSpeed);
            }

            if (dbg.NoHeat)
            {
                sys.Heat = Math.Max(sys.Heat - 10f * delta, 0);
                continue;
            }

            float energyLevel = kvp.Key switch
            {
                SystemId.Drive => ship.Energy.Drive,
                SystemId.Shields => ship.Energy.Shields,
                SystemId.Sensors => ship.Energy.Sensors,
                SystemId.Weapons => ship.Energy.Weapons,
                _ => 25
            };

            float heatGenRate = (energyLevel - 25f) * 0.15f;
            float heatDissipation = 2f;

            if (sys.Status != SystemStatus.Offline)
                sys.Heat = Math.Clamp(sys.Heat + (heatGenRate - heatDissipation) * delta, 0, ShipSystem.MaxHeat);

            if (sys.Heat >= ShipSystem.MaxHeat && sys.Status != SystemStatus.Offline)
            {
                sys.Status = SystemStatus.Offline;
                _state.AddMissionLogEntry(new MissionLogEntry
                {
                    Timestamp = _state.Mission.ElapsedTime,
                    Source = "System",
                    Message = $"⚠ {kvp.Key} automatisch abgeschaltet (Überhitzung)",
                    WebToast = MissionLogWebToast.Toast,
                });
            }
            else if (sys.Heat >= ShipSystem.CriticalHeatThreshold && sys.Status == SystemStatus.Operational)
            {
                sys.Status = SystemStatus.Degraded;
            }

            if (sys.IsRepairing && sys.Status != SystemStatus.Operational)
            {
                sys.RepairProgress += 8f * delta;
                if (sys.RepairProgress >= 100f)
                {
                    sys.RepairProgress = 100f;
                    sys.IsRepairing = false;
                    sys.Status = SystemStatus.Operational;
                    sys.Heat = Math.Max(sys.Heat - 30f, 0);
                    _state.AddMissionLogEntry(new MissionLogEntry
                    {
                        Timestamp = _state.Mission.ElapsedTime,
                        Source = "Engineer",
                        Message = $"System repariert: {kvp.Key}",
                        WebToast = MissionLogWebToast.Toast,
                    });
                }
            }
        }
    }

    /// <summary>
    /// Tutorial: <c>sector_exit</c> must not become Probed/Detected from probes or other effects until
    /// <see cref="MissionState.JumpCoordinatesUnlocked"/> (after relay extraction).
    /// </summary>
    private void ClampHiddenExitUntilRelayUnlock()
    {
        if (_script?.PrimaryObjective?.HideExitUntilScanned != true) return;
        if (_state.Mission.JumpCoordinatesUnlocked) return;
        var exit = _state.Contacts.Find(c => c.Id == "sector_exit");
        if (exit == null) return;
        if (exit.Discovery != DiscoveryState.Hidden)
        {
            exit.Discovery = DiscoveryState.Hidden;
            exit.IsScanning = false;
            exit.ScanProgress = 0;
        }
    }

    private void UpdateSensorVisibility()
    {
        float sensorRange = ShipCalculations.CalculateSensorRange(_state.Ship, _state.ContactsState.ActiveSensors);
        float elapsed = _state.Mission.ElapsedTime;
        bool reveal = _state.Debug.RevealContacts;

        foreach (var contact in _state.Contacts)
        {
            // Procedural exit: same unlock as a probe hit when the ship is within sensor range.
            // Tutorial (ScriptLocksExitUntilScan) keeps the exit hidden until relay/script events.
            if (contact.Id == "sector_exit" && !_state.Mission.JumpCoordinatesUnlocked)
            {
                if (reveal)
                    SectorExitCoordinateUnlock.ApplyDebugPassiveReveal(_state.Mission, contact);
                else if (!_state.Mission.ScriptLocksExitUntilScan)
                {
                    float dxExit = contact.PositionX - _state.Ship.PositionX;
                    float dyExit = contact.PositionY - _state.Ship.PositionY;
                    float distExit = MathF.Sqrt(dxExit * dxExit + dyExit * dyExit);
                    if (distExit <= sensorRange &&
                        SectorExitCoordinateUnlock.TryApplyIfEligible(_state.Mission, contact))
                    {
                        _state.AddMissionLogEntry(new MissionLogEntry
                        {
                            Timestamp = _state.Mission.ElapsedTime,
                            Source = "Tactical",
                            Message = "Sprungkoordinaten erfasst — Ausgang aufgedeckt.",
                            WebToast = MissionLogWebToast.ToastProminent,
                        });
                    }
                }

                continue;
            }

            if (contact.PreRevealed) continue;
            if (contact.Discovery == DiscoveryState.Scanned) continue;

            if (reveal)
            {
                contact.Discovery = DiscoveryState.Detected;
                continue;
            }

            float dx = contact.PositionX - _state.Ship.PositionX;
            float dy = contact.PositionY - _state.Ship.PositionY;
            float dist = MathF.Sqrt(dx * dx + dy * dy);

            if (dist <= sensorRange)
            {
                // Align with tactical.js / TacticalDisplay: passive Detected blips only in inner third (or full if flag).
                float strongR = contact.RadarShowDetectedInFullRange
                    ? sensorRange
                    : sensorRange / 3f;
                bool probeGhostDone = contact.ProbeExpiry >= 0f && elapsed >= contact.ProbeExpiry;
                bool inStrongPassive = dist <= strongR;

                if (contact.Discovery == DiscoveryState.Probed)
                {
                    if (inStrongPassive)
                    {
                        contact.Discovery = DiscoveryState.Detected;
                        continue;
                    }

                    if (probeGhostDone)
                    {
                        contact.Discovery = DiscoveryState.Hidden;
                        contact.IsScanning = false;
                    }

                    continue;
                }

                contact.Discovery = DiscoveryState.Detected;
                continue;
            }
            else if (contact.Discovery == DiscoveryState.Detected && !contact.PersistDetectedBeyondSensorRange)
            {
                contact.Discovery = DiscoveryState.Hidden;
            }
            else if (contact.Discovery == DiscoveryState.Probed && contact.ProbeExpiry >= 0f &&
                     elapsed >= contact.ProbeExpiry)
            {
                contact.Discovery = DiscoveryState.Hidden;
                contact.IsScanning = false;
            }
        }
    }

    private void UpdateProbes(float delta)
    {
        var cs = _state.ContactsState;

        for (int i = cs.ActiveProbes.Count - 1; i >= 0; i--)
        {
            cs.ActiveProbes[i].RemainingTime -= delta;
            if (cs.ActiveProbes[i].RemainingTime <= 0)
                cs.ActiveProbes.RemoveAt(i);
        }

        if (_state.Debug.InfiniteProbes)
        {
            cs.ProbeCharges = ContactsState.MaxProbeCharges;
            return;
        }

        if (cs.ProbeCharges < ContactsState.MaxProbeCharges)
        {
            float energyMult = _state.Ship.Energy.Sensors / 25f;
            float cdMult = _state.Debug.NoCooldowns ? 5f : 1f;
            cs.ProbeRechargeTimer += delta * Math.Max(energyMult, 0.1f) * cdMult;
            if (cs.ProbeRechargeTimer >= ContactsState.ProbeRechargeTime)
            {
                cs.ProbeRechargeTimer = 0;
                cs.ProbeCharges++;
            }
        }
    }

    /// <summary>
    /// While an active probe ring covers a Probed contact, radar shows live position; when coverage ends,
    /// freeze <see cref="Contact.SnapshotX"/> for ghost display until <see cref="Contact.ProbeExpiry"/>.
    /// </summary>
    private void UpdateProbedProbeCoverage()
    {
        var probes = _state.ContactsState.ActiveProbes;
        foreach (var contact in _state.Contacts)
        {
            if (contact.Discovery != DiscoveryState.Probed)
            {
                contact.ProbeCoveredLastFrame = false;
                continue;
            }

            bool covered = false;
            foreach (var p in probes)
            {
                float dx = contact.PositionX - p.X;
                float dy = contact.PositionY - p.Y;
                if (MathF.Sqrt(dx * dx + dy * dy) <= p.RevealRadius)
                {
                    covered = true;
                    break;
                }
            }

            if (contact.ProbeCoveredLastFrame && !covered)
            {
                contact.SnapshotX = contact.PositionX;
                contact.SnapshotY = contact.PositionY;
                contact.SnapshotZ = contact.PositionZ;
                contact.ProbeExpiry = _state.Mission.ElapsedTime + ContactsState.ProbeGhostMemorySeconds;
            }

            contact.ProbeCoveredLastFrame = covered;
        }
    }

    private void UpdateScanning(float delta)
    {
        bool instant = _state.Debug.InstantScans;
        float sensorEnergy = _state.Ship.Energy.Sensors / 25f;
        float statusMult = ShipCalculations.GetScanStatusMultiplier(
            _state.Ship.Systems[SystemId.Sensors].Status);
        float heatMult = _state.Ship.Systems[SystemId.Sensors].GetHeatEfficiencyMultiplier();
        float activeMult = _state.ContactsState.ActiveSensors ? 1.5f : 1f;
        float sensorRange = ShipCalculations.CalculateSensorRange(_state.Ship, _state.ContactsState.ActiveSensors);

        foreach (var contact in _state.Contacts)
        {
            if (!contact.IsScanning || contact.ScanProgress >= 100) continue;
            if (contact.Discovery == DiscoveryState.Hidden)
            {
                contact.IsScanning = false;
                continue;
            }

            if (!ContactScanRules.CanScanContact(contact, _state))
            {
                contact.IsScanning = false;
                continue;
            }

            if (instant)
            {
                contact.ScanProgress = 100;
            }
            else
            {
                float dx = contact.PositionX - _state.Ship.PositionX;
                float dy = contact.PositionY - _state.Ship.PositionY;
                float distance = MathF.Sqrt(dx * dx + dy * dy);
                float distanceFactor = Math.Clamp(sensorRange / (distance + 50f), 0.15f, 1.5f);

                float scanSpeed = 12f * sensorEnergy * statusMult * heatMult * activeMult * distanceFactor;
                contact.ScanProgress = Math.Clamp(contact.ScanProgress + scanSpeed * delta, 0, 100);
            }

            if (contact.ScanProgress >= 100)
            {
                contact.IsScanning = false;
                contact.Discovery = DiscoveryState.Scanned;
                contact.IsVisibleOnMainScreen = true;
                ClassifyContact(contact);
            }
        }
    }

    private void ClassifyContact(Contact contact)
    {
        bool scriptHandled = TryClassifyFromScript(contact);

        if (!scriptHandled)
        {
            switch (contact.Id)
            {
                case "primary_target":
                    contact.Type = ContactType.Friendly;
                    contact.DisplayName = "Primärziel (identifiziert)";
                    contact.ThreatLevel = 0;
                    break;
                case "unknown_contact":
                    contact.Type = ContactType.Neutral;
                    contact.DisplayName = "Unbekanntes Schiff - Klasse: Transporter";
                    contact.ThreatLevel = 3;
                    break;
                default:
                    if (contact.Agent != null)
                        break;
                    contact.Type = ContactType.Neutral;
                    break;
            }
        }

        _state.AddMissionLogEntry(new MissionLogEntry
        {
            Timestamp = _state.Mission.ElapsedTime,
            Source = "Tactical",
            Message = $"Kontakt klassifiziert: {contact.DisplayName} ({contact.Type})",
            WebToast = MissionLogWebToast.Toast,
        });
    }

    private bool TryClassifyFromScript(Contact contact)
    {
        if (_script?.Classifications == null) return false;

        string? matchKey = null;
        if (_script.Classifications.ContainsKey(contact.Id))
            matchKey = contact.Id;
        else if (contact.Agent != null && _script.Classifications.ContainsKey(contact.Agent.AgentType))
            matchKey = contact.Agent.AgentType;

        if (matchKey == null) return false;

        var cls = _script.Classifications[matchKey];
        if (cls.Name != null)
            contact.DisplayName = cls.Name;
        if (cls.Type.HasValue)
            contact.Type = cls.Type.Value;
        if (cls.Log != null)
        {
            _state.AddMissionLogEntry(new MissionLogEntry
            {
                Timestamp = _state.Mission.ElapsedTime,
                Source = "System",
                Message = cls.Log,
                WebToast = MissionLogWebToast.Toast,
            });
        }
        return true;
    }

    private void UpdateOverlays(float delta)
    {
        for (int i = _state.Overlays.Count - 1; i >= 0; i--)
        {
            var overlay = _state.Overlays[i];
            overlay.RemainingTime -= delta;
            if (overlay.RemainingTime <= 0 || overlay.Dismissed)
                _state.Overlays.RemoveAt(i);
        }
    }

    private void UpdateContactMovement(float delta)
    {
        foreach (var contact in _state.Contacts)
        {
            if (contact.IsDestroyed) continue;
            contact.PositionX += contact.VelocityX * delta;
            contact.PositionY += contact.VelocityY * delta;
            contact.PositionZ += contact.VelocityZ * delta;
        }
    }

    private void UpdateAgents(float delta)
    {
        AgentBehaviorSystem.Update(_state.Contacts, _state.Ship, delta);

        for (int i = _state.Contacts.Count - 1; i >= 0; i--)
        {
            var c = _state.Contacts[i];
            // Gunner loot wrecks clear Agent — only transit/flee despawn uses Destroyed mode here.
            if (c.Agent?.Mode == AgentBehaviorMode.Destroyed && !c.IsDestroyed)
                c.ApplyCombatDestruction();
        }
    }

    private void UpdateWeaknessAnalysis(float delta)
    {
        bool instant = _state.Debug.InstantScans;
        float sensorEnergy = _state.Ship.Energy.Sensors / 25f;
        float statusMult = ShipCalculations.GetScanStatusMultiplier(
            _state.Ship.Systems[SystemId.Sensors].Status);

        foreach (var contact in _state.Contacts)
        {
            if (!contact.IsAnalyzing || contact.HasWeakness) continue;

            if (instant)
            {
                contact.WeaknessAnalysisProgress = 100;
            }
            else
            {
                float dx = contact.PositionX - _state.Ship.PositionX;
                float dy = contact.PositionY - _state.Ship.PositionY;
                float distance = MathF.Sqrt(dx * dx + dy * dy);
                float sensorRange = ShipCalculations.CalculateSensorRange(_state.Ship, _state.ContactsState.ActiveSensors);
                float distanceFactor = Math.Clamp(sensorRange / (distance + 50f), 0.1f, 1.2f);

                float analysisSpeed = 6f * sensorEnergy * statusMult * distanceFactor;
                contact.WeaknessAnalysisProgress = Math.Clamp(
                    contact.WeaknessAnalysisProgress + analysisSpeed * delta, 0, 100);
            }

            if (contact.WeaknessAnalysisProgress >= 100)
            {
                contact.IsAnalyzing = false;
                contact.HasWeakness = true;
                _state.AddMissionLogEntry(new MissionLogEntry
                {
                    Timestamp = _state.Mission.ElapsedTime,
                    Source = "Tactical",
                    Message = $"Schwachstelle identifiziert: {contact.DisplayName} (+50% Schaden für Gunner)",
                    WebToast = MissionLogWebToast.Toast,
                });
            }
        }
    }

    private void UpdateGunner(float delta)
    {
        var gunner = _state.Gunner;
        var weaponSys = _state.Ship.Systems[SystemId.Weapons];
        var dbg = _state.Debug;

        if (gunner.FireCooldown > 0)
        {
            float cdMult = dbg.NoCooldowns ? 10f : 1f;
            gunner.FireCooldown = Math.Max(0, gunner.FireCooldown - delta * cdMult);
        }

        if (!string.IsNullOrEmpty(gunner.SelectedTargetId))
        {
            var sel = _state.Contacts.Find(c => c.Id == gunner.SelectedTargetId);
            bool invalid = sel == null || sel.IsDestroyed || sel.Discovery != DiscoveryState.Scanned;
            if (!invalid && sel != null)
            {
                invalid = gunner.Tool == ToolMode.Mining
                    ? !GunnerContactRules.IsDrillablePoiForGunnerList(sel)
                    : !GunnerContactRules.IsSelectableForCombat(sel);
            }
            if (invalid)
            {
                gunner.SelectedTargetId = null;
                gunner.TargetLockProgress = 0;
            }
        }

        if (!string.IsNullOrEmpty(gunner.SelectedTargetId) && gunner.TargetLockProgress < 100f)
        {
            var contact = _state.Contacts.Find(c => c.Id == gunner.SelectedTargetId);
            if (contact == null || contact.IsDestroyed || contact.Discovery != DiscoveryState.Scanned)
            {
                gunner.SelectedTargetId = null;
                gunner.TargetLockProgress = 0;
                return;
            }

            if (weaponSys.Status == SystemStatus.Offline && !dbg.GodMode) return;

            if (dbg.InstantLock)
            {
                gunner.TargetLockProgress = 100f;
            }
            else
            {
                float lockSpeed = ShipCalculations.CalculateTargetLockSpeed(_state.Ship);
                float baseLockTime = gunner.Mode == WeaponMode.Precision
                    ? GunnerState.PrecisionLockTime
                    : GunnerState.BarrageLockTime;
                float lockPerSecond = lockSpeed > 0 ? 100f / baseLockTime * lockSpeed : 0;
                gunner.TargetLockProgress = Math.Clamp(gunner.TargetLockProgress + lockPerSecond * delta, 0, 100);
            }
        }

        if (gunner.IsDefensiveMode && string.IsNullOrEmpty(gunner.SelectedTargetId))
        {
            var nearest = FindNearestHostile();
            if (nearest != null)
            {
                gunner.SelectedTargetId = nearest.Id;
                gunner.TargetLockProgress = 0;
            }
        }

        if (gunner.IsAutofire && gunner.Tool == ToolMode.Combat)
            GunnerFireAction.TryExecuteFire(_state);
    }

    private Contact? FindNearestHostile()
    {
        Contact? best = null;
        float bestDist = float.MaxValue;
        foreach (var c in _state.Contacts)
        {
            if (c.IsDestroyed || c.Discovery != DiscoveryState.Scanned) continue;
            if (c.Type != ContactType.Hostile && !c.IsDesignated) continue;
            float dx = c.PositionX - _state.Ship.PositionX;
            float dy = c.PositionY - _state.Ship.PositionY;
            float d = dx * dx + dy * dy;
            if (d < bestDist) { bestDist = d; best = c; }
        }
        return best;
    }

    private void UpdateEnemyAttacks(float delta)
    {
        if (_state.Debug.Invulnerable) return;

        float shieldAbsorption = ShipCalculations.CalculateShieldAbsorption(_state.Ship);
        bool activeSensors = _state.ContactsState.ActiveSensors;
        bool forceEnemyHit = _state.Debug.GodMode;

        foreach (var contact in _state.Contacts)
        {
            if (contact.IsDestroyed || contact.Type != ContactType.Hostile) continue;
            if (contact.AttackDamage <= 0) continue;

            float dx = _state.Ship.PositionX - contact.PositionX;
            float dy = _state.Ship.PositionY - contact.PositionY;
            float dist = MathF.Sqrt(dx * dx + dy * dy);

            float effectiveRange = contact.AttackRange;
            if (activeSensors) effectiveRange *= 1.3f;

            contact.IsTargetingPlayer = dist <= effectiveRange;

            if (!contact.IsTargetingPlayer) continue;

            contact.AttackCooldown -= delta;
            if (contact.AttackCooldown > 0) continue;

            contact.AttackCooldown = contact.AttackInterval;

            var (pvx, pvy) = ShipCalculations.GetShipVelocityXY(_state);
            float lateral = CombatAccuracy.ComputeLateralRelativeSpeed(
                contact.VelocityX, contact.VelocityY, pvx, pvy, dx, dy, dist);

            float playerSpeed = ShipCalculations.CalculateShipSpeed(_state.Ship);
            float enemyAcc = contact.Agent?.WeaponAccuracy ?? 0f;

            float pHit = CombatAccuracy.ComputeEnemyHitChance(
                dist, lateral, effectiveRange, enemyAcc, contact.ThreatLevel,
                _state.Ship.FlightMode, playerSpeed, _state.Engagement);

            bool enemyHit = CombatAccuracy.RollHit(pHit, forceEnemyHit);

            var enemyVisual = WeaponVisualKind.KineticTracer;
            if (contact.Agent != null &&
                AgentDefinition.TryGet(contact.Agent.AgentType, out var enemyDef))
                enemyVisual = enemyDef.WeaponVisual;

            _state.CombatFx.PendingShots.Add(new ShotEvent
            {
                ShooterId = contact.Id,
                TargetId = "player",
                Visual = enemyVisual,
                Hit = enemyHit,
                TimestampSec = _state.Mission.ElapsedTime,
            });

            if (!enemyHit)
            {
                _state.AddMissionLogEntry(new MissionLogEntry
                {
                    Timestamp = _state.Mission.ElapsedTime,
                    Source = "System",
                    Message = $"{contact.DisplayName}: Salve verfehlt",
                    WebToast = MissionLogWebToast.LogOnly,
                });
                continue;
            }

            float rawDamage = contact.AttackDamage * _damageMultiplier;

            float absorbed = rawDamage * shieldAbsorption;
            float hullDamage = rawDamage - absorbed;

            _state.Ship.HullIntegrity = Math.Max(0, _state.Ship.HullIntegrity - hullDamage);

            if (absorbed > 0)
                _state.Ship.Systems[SystemId.Shields].Heat = Math.Clamp(
                    _state.Ship.Systems[SystemId.Shields].Heat + absorbed * 0.5f, 0, ShipSystem.MaxHeat);

            _state.AddMissionLogEntry(new MissionLogEntry
            {
                Timestamp = _state.Mission.ElapsedTime,
                Source = "System",
                Message = $"Treffer von {contact.DisplayName}: {hullDamage:F0} Hüllenschaden" +
                    (absorbed > 0 ? $" ({absorbed:F0} absorbiert)" : ""),
                WebToast = MissionLogWebToast.Toast,
            });
        }
    }

    private void CheckPhaseTransitions()
    {
        float t = _state.Mission.ElapsedTime;
        var currentPhase = _state.Mission.Phase;

        if (currentPhase == MissionPhase.Anflug && t >= DefaultPhase1End)
            TransitionToPhase(MissionPhase.Stoerung);
        else if (currentPhase == MissionPhase.Stoerung && t >= DefaultPhase2End)
            TransitionToPhase(MissionPhase.Krisenfenster);
        else if (currentPhase == MissionPhase.Krisenfenster && t >= DefaultPhase3End)
            TransitionToPhase(MissionPhase.Abschluss);
    }

    private void TransitionToPhase(MissionPhase newPhase)
    {
        _state.Mission.Phase = newPhase;
        _state.Mission.PhaseTimer = 0;
        if (_state.Mission.UseStructuredMissionPhases)
            EmitSignal(SignalName.PhaseChanged, newPhase.ToString());

        _state.AddMissionLogEntry(new MissionLogEntry
        {
            Timestamp = _state.Mission.ElapsedTime,
            Source = "System",
            Message = $"Phase: {newPhase}",
            WebToast = MissionLogWebToast.ToastProminent,
        });

        GD.Print($"[Mission] Phase → {newPhase}");
    }

    private static readonly (string id, float defaultTime)[] EventSchedule =
    {
        ("sensor_shimmer", 70f),
        ("shield_stress", 200f),
        ("unknown_approach", 250f),
        ("recovery_window", 430f),
    };

    private void CheckEvents()
    {
        if (_script?.UseOnlyScriptTriggers == true)
            return;

        float t = _state.Mission.ElapsedTime;

        foreach (var (id, defaultTime) in EventSchedule)
        {
            if (_triggeredEvents.Contains(id)) continue;
            if (t < defaultTime) continue;

            bool shouldTrigger = _encounterConfig == null
                || _encounterConfig.ForcedEvents.Contains(id);
            if (!shouldTrigger) continue;

            _triggeredEvents.Add(id);
            TriggerEventById(id);
        }
    }

    private void TriggerEventById(string id)
    {
        switch (id)
        {
            case "sensor_shimmer": TriggerSensorShimmer(); break;
            case "shield_stress": TriggerShieldStress(); break;
            case "unknown_approach": TriggerUnknownContact(); break;
            case "recovery_window": TriggerRecoveryWindow(); break;
            default: TryTriggerScriptedEvent(id); break;
        }
    }

    private void TryTriggerScriptedEvent(string eventId)
    {
        ScriptedEvent? evt = null;
        if (_script?.Events != null && _script.Events.TryGetValue(eventId, out var scriptEvt))
            evt = scriptEvt;
        else if (_runtimeEvents.TryGetValue(eventId, out var runtimeEvt))
            evt = runtimeEvt;
        if (evt == null) return;

        GD.Print($"[Mission] Scripted event: {eventId}");

        if (evt.SystemEffects != null)
        {
            foreach (var fx in evt.SystemEffects)
            {
                if (!_state.Ship.Systems.TryGetValue(fx.System, out var sys)) continue;
                sys.Heat = Math.Clamp(sys.Heat + fx.HeatDelta, 0, ShipSystem.MaxHeat);
                if (fx.SetStatus.HasValue)
                    sys.Status = fx.SetStatus.Value;
            }
        }

        _state.ActiveEvents.Add(new GameEvent
        {
            Id = eventId,
            Title = evt.Title,
            Description = evt.Description,
            IsActive = true,
            TimeRemaining = evt.Duration,
            ShowOnMainScreen = evt.ShowOnMainScreen,
        });

        if (!string.IsNullOrEmpty(evt.LogEntry))
        {
            bool funkspruchLine = evt.LogEntry.StartsWith("Funkspruch:", StringComparison.Ordinal);
            _state.AddMissionLogEntry(new MissionLogEntry
            {
                Timestamp = _state.Mission.ElapsedTime,
                Source = "System",
                Message = evt.LogEntry,
                WebToast = funkspruchLine ? MissionLogWebToast.LogOnly : MissionLogWebToast.Toast,
            });
        }

        if (!string.IsNullOrEmpty(evt.DecisionId))
            AddScriptedDecision(evt.DecisionId);

        EmitSignal(SignalName.EventTriggered, eventId);
    }

    private void TriggerSensorShimmer()
    {
        GD.Print("[Mission] Event: Sensor Shimmer");

        _state.Ship.Systems[SystemId.Sensors].Status = SystemStatus.Degraded;
        _state.Ship.Systems[SystemId.Sensors].Heat += 25f;

        _state.ActiveEvents.Add(new GameEvent
        {
            Id = "sensor_shimmer",
            Title = "Sensorstörung",
            Description = "Elektromagnetische Interferenz beeinträchtigt die Sensorik. Scan-Geschwindigkeit reduziert.",
            IsActive = true,
            TimeRemaining = 120f,
        });

        _state.AddMissionLogEntry(new MissionLogEntry
        {
            Timestamp = _state.Mission.ElapsedTime,
            Source = "System",
            Message = "⚠ Sensorstörung erkannt - Scan-Effizienz reduziert",
            WebToast = MissionLogWebToast.Toast,
        });

        EmitSignal(SignalName.EventTriggered, "sensor_shimmer");
    }

    private void TriggerShieldStress()
    {
        GD.Print("[Mission] Event: Shield Stress");

        _state.Ship.Systems[SystemId.Shields].Heat += 40f;
        _state.Ship.HullIntegrity -= 5f * _damageMultiplier;

        _state.ActiveEvents.Add(new GameEvent
        {
            Id = "shield_stress",
            Title = "Frontschild-Belastung",
            Description = "Trümmerpartikel belasten die Frontschilde. Energie-Umverteilung empfohlen.",
            IsActive = true,
            TimeRemaining = 90f,
        });

        _state.AddPendingDecision(new MissionDecision
        {
            Id = "shield_response",
            Title = "Schild-Krise",
            Description = "Die Frontschilde stehen unter Stress. Wie reagieren wir?",
            Options = new List<DecisionOption>
            {
                new() { Id = "reinforce", Label = "Schilde verstärken", Description = "Energie zu Schilden umleiten" },
                new() { Id = "evade", Label = "Ausweichkurs", Description = "Navigator soll Kurs ändern" },
                new() { Id = "push_through", Label = "Durchhalten", Description = "Kurs beibehalten, Risiko akzeptieren" },
            }
        });

        EmitSignal(SignalName.EventTriggered, "shield_stress");
    }

    private void TriggerUnknownContact()
    {
        GD.Print("[Mission] Event: Unknown Contact");

        // Use the pre-placed encounter marker position from level generation
        float ux = _state.Mission.EncounterSpawnX;
        float uy = _state.Mission.EncounterSpawnY;
        float vx = (_state.Ship.PositionX - ux) * 0.003f;
        float vy = (_state.Ship.PositionY - uy) * 0.003f;

        int salvageRoll = HashCode.Combine(
            "unknown_contact".GetHashCode(),
            (int)(_state.Mission.ElapsedTime * 1000)) & 0x7FFFFFFF;
        string salvageProfile = (salvageRoll % 2) == 0 ? "flight_data" : "spare_cargo";

        _state.Contacts.Add(new Contact
        {
            Id = "unknown_contact",
            Type = ContactType.Unknown,
            DisplayName = "Unbekannte Signatur",
            PositionX = ux,
            PositionY = uy,
            ThreatLevel = 3,
            ScanProgress = 0,
            VelocityX = vx,
            VelocityY = vy,
            PoiType = "argos_blackbox",
            PoiRewardProfile = salvageProfile,
        });

        _state.ActiveEvents.Add(new GameEvent
        {
            Id = "unknown_approach",
            Title = "Unbekannter Kontakt",
            Description = "Ein unidentifiziertes Objekt nähert sich. Scannen und bewerten empfohlen.",
            IsActive = true,
            TimeRemaining = 180f,
            ShowOnMainScreen = false,
        });

        _state.AddPendingDecision(new MissionDecision
        {
            Id = "contact_response",
            Title = "Unbekannter Kontakt nähert sich",
            Description = "Ein unidentifiziertes Objekt wurde erkannt und nähert sich. Wie priorisieren wir?",
            Options = new List<DecisionOption>
            {
                new() { Id = "rescue_first", Label = "Mission priorisieren", Description = "Weiter zum Ziel, Kontakt beobachten" },
                new() { Id = "investigate", Label = "Kontakt untersuchen", Description = "Scannen und annähern" },
                new() { Id = "retreat", Label = "Rückzug", Description = "Sicherheitsabstand herstellen" },
            }
        });

        EmitSignal(SignalName.EventTriggered, "unknown_approach");
    }

    private void TriggerRecoveryWindow()
    {
        GD.Print("[Mission] Event: Recovery Window");

        _state.ActiveEvents.Add(new GameEvent
        {
            Id = "recovery_window",
            Title = "Bergungsfenster",
            Description = "Das Zeitfenster für das Primärziel ist offen. Koordination erforderlich!",
            IsActive = true,
            TimeRemaining = 120f,
            ShowOnMainScreen = false,
        });

        EmitSignal(SignalName.EventTriggered, "recovery_window");
    }

    // ── MissionScript trigger evaluation ─────────────────────────────

    private void CheckScriptTriggers()
    {
        CheckProximityTriggers();
        CheckTimeTriggers();
    }

    private void CheckProximityTriggers()
    {
        if (_script != null)
        {
            for (int i = 0; i < _script.ProximityTriggers.Count; i++)
            {
                if (_firedProximity.Contains(i)) continue;
                if (!EvaluateProximityTrigger(_script.ProximityTriggers[i])) continue;
                if (_script.ProximityTriggers[i].Once) _firedProximity.Add(i);
                FireScriptTrigger(_script.ProximityTriggers[i].EventId,
                    _script.ProximityTriggers[i].DecisionId, _script.ProximityTriggers[i].LogEntry);
            }
        }

        for (int i = 0; i < _runtimeProximityTriggers.Count; i++)
        {
            if (_firedRuntimeProximity.Contains(i)) continue;
            if (!EvaluateProximityTrigger(_runtimeProximityTriggers[i])) continue;
            if (_runtimeProximityTriggers[i].Once) _firedRuntimeProximity.Add(i);
            FireScriptTrigger(_runtimeProximityTriggers[i].EventId,
                _runtimeProximityTriggers[i].DecisionId, _runtimeProximityTriggers[i].LogEntry);
        }
    }

    private bool EvaluateProximityTrigger(ProximityTrigger trigger)
    {
        var refPos = ResolveRef(trigger.Ref);
        if (refPos == null) return false;

        float dx = _state.Ship.PositionX - refPos.Value.X;
        float dy = _state.Ship.PositionY - refPos.Value.Y;
        float dz = _state.Ship.PositionZ - refPos.Value.Z;
        float dist = MathF.Sqrt(dx * dx + dy * dy + dz * dz);
        return dist <= trigger.Radius;
    }

    private void CheckTimeTriggers()
    {
        float t = _state.Mission.ElapsedTime;

        if (_script != null)
        {
            for (int i = 0; i < _script.TimeTriggers.Count; i++)
            {
                if (_firedTimeTriggers.Contains(i)) continue;
                var trigger = _script.TimeTriggers[i];
                if (t < trigger.Time) continue;
                if (_triggeredEvents.Contains(trigger.EventId)) continue;
                if (trigger.Once) _firedTimeTriggers.Add(i);
                FireScriptTrigger(trigger.EventId, trigger.DecisionId, trigger.LogEntry);
            }
        }

        for (int i = 0; i < _runtimeTimeTriggers.Count; i++)
        {
            if (_firedRuntimeTimeTriggers.Contains(i)) continue;
            var trigger = _runtimeTimeTriggers[i];
            if (t < trigger.Time) continue;
            if (_triggeredEvents.Contains(trigger.EventId)) continue;
            if (trigger.Once) _firedRuntimeTimeTriggers.Add(i);
            FireScriptTrigger(trigger.EventId, trigger.DecisionId, trigger.LogEntry);
        }
    }

    private void FireScriptTrigger(string eventId, string? decisionId, string? logEntry)
    {
        if (!_triggeredEvents.Contains(eventId))
        {
            _triggeredEvents.Add(eventId);
            TriggerEventById(eventId);
        }

        SpawnDeferredAgents(eventId);

        if (decisionId != null)
            AddScriptedDecision(decisionId);

        if (logEntry != null)
        {
            _state.AddMissionLogEntry(new MissionLogEntry
            {
                Timestamp = _state.Mission.ElapsedTime,
                Source = "System",
                Message = logEntry,
                WebToast = MissionLogWebToast.Toast,
            });
        }
    }

    private int _deferredAgentCounter;

    /// <summary>
    /// Moves spawn along ship→spawn so passive sensors do not reveal the contact immediately (anticlimactic pop-in).
    /// </summary>
    private void PushDeferredSpawnOutsideSensorRange(ref float spawnX, ref float spawnY)
    {
        const float margin = 80f;
        float shipX = _state.Ship.PositionX;
        float shipY = _state.Ship.PositionY;
        float sensorR = ShipCalculations.CalculateSensorRange(_state.Ship, _state.ContactsState.ActiveSensors);
        float minDist = sensorR + margin;

        float dx = spawnX - shipX;
        float dy = spawnY - shipY;
        float d = MathF.Sqrt(dx * dx + dy * dy);
        if (d >= minDist) return;

        if (d < 0.001f)
        {
            dx = 1f;
            dy = 0f;
            d = 1f;
        }
        else
        {
            float inv = 1f / d;
            dx *= inv;
            dy *= inv;
        }

        spawnX = shipX + dx * minDist;
        spawnY = shipY + dy * minDist;
        spawnX = Math.Clamp(spawnX, 5f, 995f);
        spawnY = Math.Clamp(spawnY, 5f, 995f);
    }

    private void SpawnDeferredAgents(string triggerId)
    {
        var scriptSpawns = _script?.DeferredAgentSpawns;
        if (scriptSpawns != null)
        {
            foreach (var spawn in scriptSpawns)
                SpawnSingleDeferredAgent(spawn, triggerId);
        }

        foreach (var spawn in _runtimeDeferredSpawns)
            SpawnSingleDeferredAgent(spawn, triggerId);
    }

    private void SpawnSingleDeferredAgent(DeferredAgentSpawn spawn, string triggerId)
    {
            if (spawn.TriggerId != triggerId) return;
            if (!AgentDefinition.TryGet(spawn.AgentType, out var def)) return;

            float spawnX, spawnY, spawnZ = 500f;
            float anchorX, anchorY, anchorZ = 500f;

            // Resolve anchor position
            if (spawn.AnchorRef.HasValue)
            {
                var anchor = ResolveRef(spawn.AnchorRef.Value);
                if (anchor.HasValue)
                {
                    anchorX = anchor.Value.X;
                    anchorY = anchor.Value.Y;
                    anchorZ = anchor.Value.Z;
                }
                else
                {
                    anchorX = 500f;
                    anchorY = 500f;
                    anchorZ = 500f;
                }
            }
            else
            {
                anchorX = _state.Ship.PositionX;
                anchorY = _state.Ship.PositionY;
                anchorZ = 500f;
            }

            // Spawn at map edge, opposite side from ship relative to anchor
            float edgeAngle;
            if (spawn.Origin == SpawnOrigin.NearLandmark)
            {
                float dx = anchorX - _state.Ship.PositionX;
                float dy = anchorY - _state.Ship.PositionY;
                edgeAngle = MathF.Atan2(dy, dx) + MathF.PI;
            }
            else
            {
                float dx = _state.Ship.PositionX - 500f;
                float dy = _state.Ship.PositionY - 500f;
                edgeAngle = MathF.Atan2(dy, dx) + MathF.PI
                    + ((_deferredAgentCounter % 3 - 1) * 0.5f);
            }
            spawnX = 500f + MathF.Cos(edgeAngle) * 480f;
            spawnY = 500f + MathF.Sin(edgeAngle) * 480f;
            spawnX = Math.Clamp(spawnX, 5f, 995f);
            spawnY = Math.Clamp(spawnY, 5f, 995f);

            PushDeferredSpawnOutsideSensorRange(ref spawnX, ref spawnY);

            float vDx = anchorX - spawnX;
            float vDy = anchorY - spawnY;
            float vLen = MathF.Sqrt(vDx * vDx + vDy * vDy);
            float speed = def.BaseSpeed;
            float vx = vLen > 0.1f ? vDx / vLen * speed : 0f;
            float vy = vLen > 0.1f ? vDy / vLen * speed : 0f;

            string contactId = $"deferred_{spawn.AgentType}_{_deferredAgentCounter++:D2}";

            var contact = new Contact
            {
                Id = contactId,
                Type = def.ContactType,
                DisplayName = def.DisplayName,
                PositionX = spawnX,
                PositionY = spawnY,
                PositionZ = spawnZ,
                ThreatLevel = def.ThreatLevel,
                ScanProgress = 0,
                Discovery = DiscoveryState.Hidden,
                // Same as MissionGenerator for sector agents: movable contacts use full sensor ring for Detected blips.
                RadarShowDetectedInFullRange = true,
                VelocityX = vx,
                VelocityY = vy,
                HitPoints = def.HitPoints,
                MaxHitPoints = def.HitPoints,
                AttackDamage = def.AttackDamage,
                AttackInterval = def.AttackInterval,
                AttackRange = def.AttackRange,
                Agent = new AgentState
                {
                    AgentType = spawn.AgentType,
                    Mode = spawn.InitialMode == AgentBehaviorMode.Guard
                        ? AgentBehaviorMode.Intercept
                        : spawn.InitialMode,
                    AnchorX = anchorX,
                    AnchorY = anchorY,
                    AnchorZ = anchorZ,
                    DestinationX = anchorX,
                    DestinationY = anchorY,
                    DetectionRadius = def.DetectionRadius,
                    FleeThreshold = def.FleeThreshold,
                    BaseSpeed = def.BaseSpeed,
                    WeaponAccuracy = def.WeaponAccuracy,
                    ShieldAbsorption = def.ShieldAbsorption,
                },
            };

            AgentSpawnPersonality.Apply(contact.Agent!, contact.Id, contact.PositionX, contact.PositionY, def.BaseSpeed);

            _state.Contacts.Add(contact);
            GD.Print($"[Mission] Deferred agent spawned: {spawn.AgentType} at ({spawnX:F0},{spawnY:F0}) -> ({anchorX:F0},{anchorY:F0})");
    }

    /// <summary>Public wrapper for <see cref="ResolveRef"/> used by orchestrators (e.g. runtime POI spawns).</summary>
    public (float X, float Y, float Z)? ResolveRefMap(TriggerRef triggerRef) => ResolveRef(triggerRef);

    private (float X, float Y, float Z)? ResolveRef(TriggerRef triggerRef)
    {
        switch (triggerRef)
        {
            case TriggerRef.Landmark:
            {
                var c = _state.Contacts.Find(c => c.Id == "primary_target");
                if (c == null) return null;
                return (c.PositionX, c.PositionY, c.PositionZ);
            }
            case TriggerRef.NearestBeacon:
            {
                var storyBeacon = _state.Contacts.Find(c => c.Id == "story_beacon");
                if (storyBeacon != null)
                    return (storyBeacon.PositionX, storyBeacon.PositionY, storyBeacon.PositionZ);

                float bestDist = float.MaxValue;
                Contact? best = null;
                foreach (var c in _state.Contacts)
                {
                    if (c.Type != ContactType.Neutral || c.IsDestroyed) continue;
                    if (c.Id == "primary_target") continue;
                    bool isBeacon = c.PreRevealed && c.Type == ContactType.Neutral;
                    if (!isBeacon) continue;

                    float dx = _state.Ship.PositionX - c.PositionX;
                    float dy = _state.Ship.PositionY - c.PositionY;
                    float dz = _state.Ship.PositionZ - c.PositionZ;
                    float d = MathF.Sqrt(dx * dx + dy * dy + dz * dz);
                    if (d < bestDist) { bestDist = d; best = c; }
                }
                if (best == null) return null;
                return (best.PositionX, best.PositionY, best.PositionZ);
            }
            case TriggerRef.MapCenter:
                return (500f, 500f, 500f);
            case TriggerRef.Encounter:
                return (_state.Mission.EncounterSpawnX, _state.Mission.EncounterSpawnY, 500f);
            case TriggerRef.Exit:
            {
                var exit = _state.Contacts.Find(c => c.Id == "sector_exit")
                    ?? _state.Contacts.Find(c => c.DisplayName == "Ausgang");
                if (exit == null) return null;
                return (exit.PositionX, exit.PositionY, exit.PositionZ);
            }
            default:
                return null;
        }
    }

    private void AddScriptedDecision(string decisionId)
    {
        ScriptedDecision? sd = null;
        if (_script?.Decisions != null && _script.Decisions.TryGetValue(decisionId, out var scriptSd))
            sd = scriptSd;
        else if (_runtimeDecisions.TryGetValue(decisionId, out var runtimeSd))
            sd = runtimeSd;
        if (sd == null) return;

        if (_state.Mission.PendingDecisions.Exists(d => d.Id == decisionId)) return;
        if (_state.Mission.CompletedDecisions.Contains(decisionId)) return;

        _state.AddPendingDecision(new MissionDecision
        {
            Id = decisionId,
            Title = sd.Title,
            Description = sd.Description,
            Options = sd.Options,
        });
    }

    /// <summary>Captain command and automatic tick: complete mission when jump conditions match <see cref="SectorJumpCompletion"/>.</summary>
    public bool TryCaptainLeaveSector()
    {
        if (!_state.MissionStarted || _state.Mission.Phase == MissionPhase.Ended) return false;
        return TryCompleteMissionAtExit();
    }

    private bool TryCompleteMissionAtExit()
    {
        if (!SectorJumpCompletion.IsReady(_state)) return false;
        _state.Mission.PrimaryObjective = ObjectiveStatus.Completed;
        EndMission(MissionResult.Success);
        return true;
    }

    private void CheckEndConditions(float delta)
    {
        if (_state.Ship.HullIntegrity <= 0 && !_state.Debug.Invulnerable)
        {
            EndMission(MissionResult.Destroyed);
            return;
        }

        if (_state.Debug.Invulnerable && _state.Ship.HullIntegrity < 1)
            _state.Ship.HullIntegrity = 1;

        // Jump-out / legacy recovery: shared rules in SectorJumpCompletion (Captain LeaveSector uses same check).
        if (TryCompleteMissionAtExit())
            return;

        // Update active events timers
        bool pauseTimers = _script?.PauseActiveEventTimers == true;
        if (!pauseTimers)
        {
            for (int i = _state.ActiveEvents.Count - 1; i >= 0; i--)
            {
                var evt = _state.ActiveEvents[i];
                if (evt.TimeRemaining > 0)
                    evt.TimeRemaining -= delta;

                if (evt.TimeRemaining <= 0 && evt.IsActive)
                {
                    evt.IsActive = false;
                    evt.IsResolved = true;
                }
            }
        }

        // Bergungsfenster zu Ende ohne Primärziel — intrinsisch ans Event gekoppelt, kein globales Limit
        if (_script == null || _script.FailMissionWhenRecoveryWindowExpires)
        {
            var recoveryExpired = _state.ActiveEvents.Find(e => e.Id == "recovery_window");
            if (recoveryExpired is { IsActive: false, IsResolved: true }
                && _state.Mission.PrimaryObjective == ObjectiveStatus.InProgress)
            {
                _state.Mission.PrimaryObjective = ObjectiveStatus.Failed;
                EndMission(MissionResult.Failed);
            }
        }
    }

    public void DebugForceEndMission(MissionResult result)
    {
        if (!_state.MissionStarted) return;
        if (_state.Mission.Phase == MissionPhase.Ended) return;

        _state.Mission.PrimaryObjective = result is MissionResult.Success or MissionResult.Partial
            ? ObjectiveStatus.Completed
            : ObjectiveStatus.Failed;

        EndMission(result);
    }

    private void EndMission(MissionResult result)
    {
        if (_state.Mission.Phase == MissionPhase.Ended) return;

        _state.Mission.Phase = MissionPhase.Ended;
        _state.MissionStarted = false;

        var unknownContact = _state.Contacts.Find(c => c.Id == "unknown_contact");
        _state.Mission.SecondaryObjective =
            unknownContact is { ScanProgress: >= 100 }
                ? ObjectiveStatus.Completed
                : ObjectiveStatus.Failed;

        string displayResult = result switch
        {
            MissionResult.Success => _script?.PrimaryObjective?.HideExitUntilScanned == true
                ? "Voller Erfolg! Sektorausgang erreicht."
                : "Voller Erfolg! Primärziel gesichert.",
            MissionResult.Partial => "Teil-Erfolg. Primärziel teilweise erreicht.",
            MissionResult.Failed => "Fehlschlag. Primärziel nicht erreicht.",
            MissionResult.Timeout => "Zeitüberschreitung. Mission abgebrochen.",
            MissionResult.Destroyed => "Schiff zerstört. Mission gescheitert.",
            _ => "Mission beendet."
        };

        _state.AddMissionLogEntry(new MissionLogEntry
        {
            Timestamp = _state.Mission.ElapsedTime,
            Source = "System",
            Message = $"Mission beendet: {displayResult}",
            WebToast = MissionLogWebToast.ToastProminent,
        });

        EmitSignal(SignalName.MissionEnded, (int)result);
        GD.Print($"[Mission] Ended: {result}");
    }

    public void TriggerEventManually(string eventId)
    {
        if (_triggeredEvents.Contains(eventId)) return;
        _triggeredEvents.Add(eventId);
        TriggerEventById(eventId);
    }

    // ── Runtime injection API (used by NodeEventCatalog / DecisionEffectResolver) ─────────

    /// <summary>Registers a runtime-only <see cref="ScriptedDecision"/> so <see cref="AddScriptedDecision"/> can resolve it.</summary>
    public void RegisterRuntimeDecision(string id, ScriptedDecision decision)
    {
        if (string.IsNullOrEmpty(id) || decision == null) return;
        _runtimeDecisions[id] = decision;
    }

    /// <summary>Registers a runtime-only <see cref="ScriptedEvent"/> so <see cref="TryTriggerScriptedEvent"/> can resolve it.</summary>
    public void RegisterRuntimeEvent(string id, ScriptedEvent evt)
    {
        if (string.IsNullOrEmpty(id) || evt == null) return;
        _runtimeEvents[id] = evt;
    }

    /// <summary>Appends a proximity trigger that will be evaluated alongside the active script.</summary>
    public void AddRuntimeProximityTrigger(ProximityTrigger trigger)
    {
        if (trigger == null) return;
        _runtimeProximityTriggers.Add(trigger);
    }

    /// <summary>Appends a time trigger that will be evaluated alongside the active script.</summary>
    public void AddRuntimeTimeTrigger(TimeTrigger trigger)
    {
        if (trigger == null) return;
        _runtimeTimeTriggers.Add(trigger);
    }

    /// <summary>Queues deferred spawn definitions that fire when their <see cref="DeferredAgentSpawn.TriggerId"/> is reached.</summary>
    public void QueueDeferredSpawns(IEnumerable<DeferredAgentSpawn> spawns)
    {
        if (spawns == null) return;
        foreach (var s in spawns)
            if (s != null) _runtimeDeferredSpawns.Add(s);
    }

    /// <summary>
    /// Queues spawns from a <see cref="NodeEventTrigger.PreSector"/> decision so they materialise
    /// in the *next* sector instead of the current (about-to-be-discarded) one. The next
    /// <see cref="StartMission"/> moves these into <see cref="_runtimeDeferredSpawns"/> and
    /// auto-fires each unique <see cref="DeferredAgentSpawn.TriggerId"/> after the sector is
    /// fully initialised. Generic across all events — no per-event wiring required.
    /// </summary>
    public void QueueSectorEntrySpawns(IEnumerable<DeferredAgentSpawn> spawns)
    {
        if (spawns == null) return;
        foreach (var s in spawns)
        {
            if (s == null) continue;
            _pendingSectorEntrySpawns.Add(s);
            if (!string.IsNullOrEmpty(s.TriggerId))
                _pendingSectorEntryTriggers.Add(s.TriggerId);
        }
    }

    /// <summary>Immediately dispatches a synthetic trigger (e.g. from a NodeEvent pre-sector decision) firing queued spawns.</summary>
    public void FireRuntimeTriggerNow(string triggerId, string? eventId = null, string? decisionId = null, string? logEntry = null)
    {
        if (!string.IsNullOrEmpty(eventId) || !string.IsNullOrEmpty(decisionId) || !string.IsNullOrEmpty(logEntry))
            FireScriptTrigger(eventId ?? "", decisionId ?? "", logEntry ?? "");
        else if (!string.IsNullOrEmpty(triggerId))
            SpawnDeferredAgents(triggerId);
    }

    public void SetPhaseManually(MissionPhase phase)
    {
        _useStructuredPhases = true;
        _state.Mission.UseStructuredMissionPhases = true;
        TransitionToPhase(phase);
    }

    public void ResetMission()
    {
        _triggeredEvents.Clear();
        _firedProximity.Clear();
        _firedTimeTriggers.Clear();
        _firedRuntimeProximity.Clear();
        _firedRuntimeTimeTriggers.Clear();
        _runtimeProximityTriggers.Clear();
        _runtimeTimeTriggers.Clear();
        _runtimeDeferredSpawns.Clear();
        _pendingSectorEntrySpawns.Clear();
        _pendingSectorEntryTriggers.Clear();
        _runtimeDecisions.Clear();
        _runtimeEvents.Clear();
        _deferredAgentCounter = 0;
        _encounterConfig = null;
        _script = null;
        _useStructuredPhases = false;
        _damageMultiplier = 1f;
        _state.ActiveEvents.Clear();
        _state.Overlays.Clear();
        _state.Contacts.Clear();
        _state.ContactsState.ActiveProbes.Clear();
        _state.ContactsState.ProbeCharges = 3;
        _state.ContactsState.ProbeRechargeTimer = 0;
        _state.ContactsState.ActiveSensors = false;
        _state.Mission = new MissionState();
        _state.Ship = new ShipState();
        _state.Route = new RouteState();
        _state.Gunner = new GunnerState();
        _state.MissionStarted = false;
        _state.IsPaused = false;
    }
}
