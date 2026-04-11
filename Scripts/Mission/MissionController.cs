using System;
using System.Collections.Generic;
using Godot;
using SpacedOut.Campaign;
using SpacedOut.Shared;
using SpacedOut.State;

namespace SpacedOut.Mission;

public partial class MissionController : Node
{
    private GameState _state = null!;

    [Signal] public delegate void PhaseChangedEventHandler(string phase);
    [Signal] public delegate void EventTriggeredEventHandler(string eventId);
    [Signal] public delegate void MissionEndedEventHandler(string result);

    private readonly HashSet<string> _triggeredEvents = new();
    private NodeEncounterConfig? _encounterConfig;

    private const float DefaultPhase1End = 60f;
    private const float DefaultPhase2End = 180f;
    private const float DefaultPhase3End = 420f;
    private const float DefaultPhase4End = 600f;
    private const float DefaultMissionTimeout = 720f;

    private float _missionTimeout;
    private float _damageMultiplier = 1f;

    public void Initialize(GameState state)
    {
        _state = state;
    }

    public void ApplyEncounterConfig(NodeEncounterConfig? config)
    {
        _encounterConfig = config;
        if (config != null)
        {
            _missionTimeout = config.TimeLimit;
            _damageMultiplier = config.DamageMultiplier;
        }
        else
        {
            _missionTimeout = DefaultMissionTimeout;
            _damageMultiplier = 1f;
        }
    }

    public void StartMission()
    {
        _triggeredEvents.Clear();
        _state.MissionStarted = true;
        _state.Mission.Phase = MissionPhase.Briefing;
        _state.Mission.ElapsedTime = 0;
        _state.Mission.PrimaryObjective = ObjectiveStatus.InProgress;
        _state.Mission.SecondaryObjective = ObjectiveStatus.InProgress;

        _state.Ship.FlightMode = FlightMode.Cruise;
        _state.Ship.SpeedLevel = 2;
        _state.Ship.HullIntegrity = 100;

        if (_encounterConfig == null)
        {
            _missionTimeout = DefaultMissionTimeout;
            _damageMultiplier = 1f;
        }

        // MissionGenerator has already populated contacts, waypoints,
        // briefing & ship position.  Fall back to hardcoded defaults
        // only when nothing was pre-populated.
        if (_state.Contacts.Count == 0)
            SetupContacts();
        if (_state.Route.Waypoints.Count == 0)
            SetupInitialRoute();
        if (string.IsNullOrEmpty(_state.Mission.BriefingText))
        {
            _state.Mission.MissionTitle = "Bergung unter Störung";
            _state.Mission.BriefingText =
                "Ein beschädigtes Forschungsschiff sendet ein Notsignal. " +
                "Lokalisieren, annähern, Bergung sichern.";
        }

        _state.Mission.Log.Add(new MissionLogEntry
        {
            Timestamp = 0,
            Source = "System",
            Message = $"Mission gestartet: {_state.Mission.MissionTitle}"
        });

        TransitionToPhase(MissionPhase.Anflug);
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
            ScanProgress = 10,
        });

        _state.Contacts.Add(new Contact
        {
            Id = "debris_field",
            Type = ContactType.Anomaly,
            DisplayName = "Trümmerfeld",
            PositionX = 400,
            PositionY = 350,
            ThreatLevel = 2,
            ScanProgress = 40,
            IsVisibleOnMainScreen = true,
        });
    }

    private void SetupInitialRoute()
    {
        _state.Route.Waypoints.Clear();
        _state.Route.Waypoints.Add(new Waypoint
        {
            Id = "wp_start", X = 100, Y = 100, Label = "Start", IsReached = true
        });
        _state.Route.Waypoints.Add(new Waypoint
        {
            Id = "wp_approach", X = 400, Y = 400, Label = "Annäherung"
        });
        _state.Route.Waypoints.Add(new Waypoint
        {
            Id = "wp_target", X = 700, Y = 650, Label = "Zielgebiet"
        });
        _state.Route.CurrentWaypointIndex = 1;
    }

    public void Update(float delta)
    {
        if (!_state.MissionStarted || _state.IsPaused) return;
        if (_state.Mission.Phase == MissionPhase.Ended) return;

        _state.Mission.ElapsedTime += delta;
        _state.Mission.PhaseTimer += delta;

        UpdateShipMovement(delta);
        UpdateSystems(delta);
        UpdateScanning(delta);
        UpdateOverlays(delta);
        UpdateContactMovement(delta);
        CheckPhaseTransitions();
        CheckEvents();
        CheckEndConditions();
    }

    private void UpdateShipMovement(float delta)
    {
        var route = _state.Route;
        var ship = _state.Ship;

        if (ship.FlightMode == FlightMode.Hold) return;

        var unreached = route.Waypoints.FindAll(w => !w.IsReached);
        if (unreached.Count == 0) return;

        var target = unreached[0];
        float dx = target.X - ship.PositionX;
        float dy = target.Y - ship.PositionY;
        float dist = MathF.Sqrt(dx * dx + dy * dy);

        if (dist < 5f)
        {
            target.IsReached = true;
            route.CurrentWaypointIndex++;
            _state.Mission.Log.Add(new MissionLogEntry
            {
                Timestamp = _state.Mission.ElapsedTime,
                Source = "Navigation",
                Message = $"Waypoint erreicht: {target.Label}"
            });
            return;
        }

        float speed = ShipCalculations.CalculateShipSpeed(ship);

        float moveX = (dx / dist) * speed * delta;
        float moveY = (dy / dist) * speed * delta;

        if (MathF.Abs(moveX) > MathF.Abs(dx)) moveX = dx;
        if (MathF.Abs(moveY) > MathF.Abs(dy)) moveY = dy;

        ship.PositionX += moveX;
        ship.PositionY += moveY;
    }

    private void UpdateSystems(float delta)
    {
        var ship = _state.Ship;

        foreach (var kvp in ship.Systems)
        {
            var sys = kvp.Value;

            // Heat generation based on energy allocation
            float energyLevel = kvp.Key switch
            {
                SystemId.Drive => ship.Energy.Drive,
                SystemId.Shields => ship.Energy.Shields,
                SystemId.Sensors => ship.Energy.Sensors,
                _ => 33
            };

            float heatGenRate = (energyLevel - 25f) * 0.15f;
            float heatDissipation = 2f;

            if (sys.Status != SystemStatus.Offline)
                sys.Heat = Math.Clamp(sys.Heat + (heatGenRate - heatDissipation) * delta, 0, ShipSystem.MaxHeat);

            if (sys.Heat >= ShipSystem.CriticalHeatThreshold && sys.Status == SystemStatus.Operational)
                sys.Status = SystemStatus.Degraded;

            // Repair progress
            if (sys.IsRepairing && sys.Status != SystemStatus.Operational)
            {
                sys.RepairProgress += 8f * delta;
                if (sys.RepairProgress >= 100f)
                {
                    sys.RepairProgress = 100f;
                    sys.IsRepairing = false;
                    sys.Status = SystemStatus.Operational;
                    sys.Heat = Math.Max(sys.Heat - 30f, 0);
                    _state.Mission.Log.Add(new MissionLogEntry
                    {
                        Timestamp = _state.Mission.ElapsedTime,
                        Source = "Engineer",
                        Message = $"System repariert: {kvp.Key}"
                    });
                }
            }
        }
    }

    private void UpdateScanning(float delta)
    {
        float sensorEnergy = _state.Ship.Energy.Sensors / 33f;
        float statusMult = ShipCalculations.GetScanStatusMultiplier(
            _state.Ship.Systems[SystemId.Sensors].Status);
        float scanSpeed = 12f * sensorEnergy * statusMult;

        foreach (var contact in _state.Contacts)
        {
            if (!contact.IsScanning || contact.ScanProgress >= 100) continue;

            contact.ScanProgress = Math.Clamp(contact.ScanProgress + scanSpeed * delta, 0, 100);

            if (contact.ScanProgress >= 100)
            {
                contact.IsScanning = false;
                ClassifyContact(contact);
            }
        }
    }

    private void ClassifyContact(Contact contact)
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
                contact.Type = ContactType.Neutral;
                break;
        }

        _state.Mission.Log.Add(new MissionLogEntry
        {
            Timestamp = _state.Mission.ElapsedTime,
            Source = "Tactical",
            Message = $"Kontakt klassifiziert: {contact.DisplayName} ({contact.Type})"
        });
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
            contact.PositionX += contact.VelocityX * delta;
            contact.PositionY += contact.VelocityY * delta;
        }
    }

    private void CheckPhaseTransitions()
    {
        float t = _state.Mission.ElapsedTime;
        var currentPhase = _state.Mission.Phase;

        float timeScale = _missionTimeout / DefaultMissionTimeout;
        float p1 = DefaultPhase1End * timeScale;
        float p2 = DefaultPhase2End * timeScale;
        float p3 = DefaultPhase3End * timeScale;

        if (currentPhase == MissionPhase.Anflug && t >= p1)
            TransitionToPhase(MissionPhase.Stoerung);
        else if (currentPhase == MissionPhase.Stoerung && t >= p2)
            TransitionToPhase(MissionPhase.Krisenfenster);
        else if (currentPhase == MissionPhase.Krisenfenster && t >= p3)
            TransitionToPhase(MissionPhase.Abschluss);
    }

    private void TransitionToPhase(MissionPhase newPhase)
    {
        _state.Mission.Phase = newPhase;
        _state.Mission.PhaseTimer = 0;
        EmitSignal(SignalName.PhaseChanged, newPhase.ToString());

        _state.Mission.Log.Add(new MissionLogEntry
        {
            Timestamp = _state.Mission.ElapsedTime,
            Source = "System",
            Message = $"Phase: {newPhase}"
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
        float t = _state.Mission.ElapsedTime;
        float timeScale = _missionTimeout / DefaultMissionTimeout;

        foreach (var (id, defaultTime) in EventSchedule)
        {
            if (_triggeredEvents.Contains(id)) continue;
            if (t < defaultTime * timeScale) continue;

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
        }
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

        _state.Mission.Log.Add(new MissionLogEntry
        {
            Timestamp = _state.Mission.ElapsedTime,
            Source = "System",
            Message = "⚠ Sensorstörung erkannt - Scan-Effizienz reduziert"
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

        _state.Mission.PendingDecisions.Add(new MissionDecision
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

        _state.Contacts.Add(new Contact
        {
            Id = "unknown_contact",
            Type = ContactType.Unknown,
            DisplayName = "Unbekannte Signatur",
            PositionX = ux,
            PositionY = uy,
            ThreatLevel = 5,
            ScanProgress = 0,
            VelocityX = vx,
            VelocityY = vy,
        });

        _state.ActiveEvents.Add(new GameEvent
        {
            Id = "unknown_approach",
            Title = "Unbekannter Kontakt",
            Description = "Ein unidentifiziertes Objekt nähert sich. Scannen und bewerten empfohlen.",
            IsActive = true,
            TimeRemaining = 180f,
        });

        _state.Mission.PendingDecisions.Add(new MissionDecision
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
        });

        EmitSignal(SignalName.EventTriggered, "recovery_window");
    }

    private void CheckEndConditions()
    {
        float t = _state.Mission.ElapsedTime;

        if (t >= _missionTimeout)
        {
            EndMission("timeout");
            return;
        }

        // Critical hull
        if (_state.Ship.HullIntegrity <= 0)
        {
            EndMission("destroyed");
            return;
        }

        // Check recovery window completion
        var recoveryEvent = _state.ActiveEvents.Find(e => e.Id == "recovery_window");
        if (recoveryEvent != null)
        {
            var primaryTarget = _state.Contacts.Find(c => c.Id == "primary_target");
            if (primaryTarget != null)
            {
                float dx = _state.Ship.PositionX - primaryTarget.PositionX;
                float dy = _state.Ship.PositionY - primaryTarget.PositionY;
                float dist = MathF.Sqrt(dx * dx + dy * dy);

                if (dist < 50f && primaryTarget.ScanProgress >= 100)
                {
                    _state.Mission.PrimaryObjective = ObjectiveStatus.Completed;
                    EndMission("success");
                    return;
                }
            }
        }

        // Phase 4 auto-end
        if (_state.Mission.Phase == MissionPhase.Abschluss && _state.Mission.PhaseTimer > 120f)
        {
            var target = _state.Contacts.Find(c => c.Id == "primary_target");
            float dx = _state.Ship.PositionX - (target?.PositionX ?? 500);
            float dy = _state.Ship.PositionY - (target?.PositionY ?? 500);
            float dist = MathF.Sqrt(dx * dx + dy * dy);

            if (dist < 100f)
            {
                _state.Mission.PrimaryObjective = ObjectiveStatus.Completed;
                EndMission("partial");
            }
            else
            {
                _state.Mission.PrimaryObjective = ObjectiveStatus.Failed;
                EndMission("failed");
            }
        }

        // Update active events timers
        for (int i = _state.ActiveEvents.Count - 1; i >= 0; i--)
        {
            var evt = _state.ActiveEvents[i];
            // TimeRemaining is decremented here
            if (evt.TimeRemaining > 0)
                evt.TimeRemaining -= (float)GetProcessDeltaTime();

            if (evt.TimeRemaining <= 0 && evt.IsActive)
            {
                evt.IsActive = false;
                evt.IsResolved = true;
            }
        }
    }

    private void EndMission(string result)
    {
        if (_state.Mission.Phase == MissionPhase.Ended) return;

        _state.Mission.Phase = MissionPhase.Ended;
        _state.MissionStarted = false;

        // Evaluate secondary objective
        var unknownContact = _state.Contacts.Find(c => c.Id == "unknown_contact");
        if (unknownContact != null && unknownContact.ScanProgress >= 100)
            _state.Mission.SecondaryObjective = ObjectiveStatus.Completed;
        else
            _state.Mission.SecondaryObjective = ObjectiveStatus.Failed;

        string displayResult = result switch
        {
            "success" => "Voller Erfolg! Primärziel gesichert.",
            "partial" => "Teil-Erfolg. Primärziel teilweise erreicht.",
            "failed" => "Fehlschlag. Primärziel nicht erreicht.",
            "timeout" => "Zeitüberschreitung. Mission abgebrochen.",
            "destroyed" => "Schiff zerstört. Mission gescheitert.",
            _ => "Mission beendet."
        };

        _state.Mission.Log.Add(new MissionLogEntry
        {
            Timestamp = _state.Mission.ElapsedTime,
            Source = "System",
            Message = $"Mission beendet: {displayResult}"
        });

        EmitSignal(SignalName.MissionEnded, result);
        GD.Print($"[Mission] Ended: {result}");
    }

    public void TriggerEventManually(string eventId)
    {
        if (_triggeredEvents.Contains(eventId)) return;
        _triggeredEvents.Add(eventId);
        TriggerEventById(eventId);
    }

    public void SetPhaseManually(MissionPhase phase)
    {
        TransitionToPhase(phase);
    }

    public void ResetMission()
    {
        _triggeredEvents.Clear();
        _encounterConfig = null;
        _missionTimeout = DefaultMissionTimeout;
        _damageMultiplier = 1f;
        _state.ActiveEvents.Clear();
        _state.Overlays.Clear();
        _state.Contacts.Clear();
        _state.Mission = new MissionState();
        _state.Ship = new ShipState();
        _state.Route = new RouteState();
        _state.MissionStarted = false;
        _state.IsPaused = false;
    }
}
