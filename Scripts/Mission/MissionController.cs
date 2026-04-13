using System;
using System.Collections.Generic;
using Godot;
using SpacedOut.Run;
using SpacedOut.Shared;
using SpacedOut.State;

namespace SpacedOut.Mission;

public partial class MissionController : Node
{
    private GameState _state = null!;

    [Signal] public delegate void PhaseChangedEventHandler(string phase);
    [Signal] public delegate void EventTriggeredEventHandler(string eventId);
    [Signal] public delegate void MissionEndedEventHandler(int result);

    private readonly HashSet<string> _triggeredEvents = new();
    private NodeEncounterConfig? _encounterConfig;
    private bool _useStructuredPhases;

    private const float DefaultPhase1End = 60f;
    private const float DefaultPhase2End = 180f;
    private const float DefaultPhase3End = 420f;

    private float _damageMultiplier = 1f;

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

    public void StartMission()
    {
        _triggeredEvents.Clear();
        _state.MissionStarted = true;
        _state.Mission.ElapsedTime = 0;
        _state.Mission.PrimaryObjective = ObjectiveStatus.InProgress;
        _state.Mission.SecondaryObjective = ObjectiveStatus.InProgress;
        _state.Mission.UseStructuredMissionPhases = _useStructuredPhases;

        _state.Ship.FlightMode = FlightMode.Cruise;
        _state.Ship.SpeedLevel = 2;
        _state.Ship.HullIntegrity = 100;
        _state.Engagement = EngagementRule.Standard;
        _state.Gunner = new GunnerState();

        // MissionGenerator has already populated contacts, briefing & ship position.
        // Fall back to hardcoded contacts only when nothing was pre-populated.
        // Route waypoints are set by the navigator (no auto-waypoints).
        if (_state.Contacts.Count == 0)
            SetupContacts();
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
        UpdateSensorVisibility();
        UpdateProbes(delta);
        UpdateScanning(delta);
        UpdateWeaknessAnalysis(delta);
        UpdateGunner(delta);
        UpdateEnemyAttacks(delta);
        UpdateOverlays(delta);
        UpdateContactMovement(delta);
        UpdatePatrolDrone(delta);
        if (_useStructuredPhases)
            CheckPhaseTransitions();
        CheckEvents();
        CheckEndConditions(delta);
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
        float dz = target.Z - ship.PositionZ;
        float dist = MathF.Sqrt(dx * dx + dy * dy + dz * dz);

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
        float moveZ = (dz / dist) * speed * delta;

        if (MathF.Abs(moveX) > MathF.Abs(dx)) moveX = dx;
        if (MathF.Abs(moveY) > MathF.Abs(dy)) moveY = dy;
        if (MathF.Abs(moveZ) > MathF.Abs(dz)) moveZ = dz;

        ship.PositionX += moveX;
        ship.PositionY += moveY;
        ship.PositionZ += moveZ;
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
                _state.Mission.Log.Add(new MissionLogEntry
                {
                    Timestamp = _state.Mission.ElapsedTime,
                    Source = "System",
                    Message = $"⚠ {kvp.Key} automatisch abgeschaltet (Überhitzung)"
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

    private void UpdateSensorVisibility()
    {
        float sensorRange = ShipCalculations.CalculateSensorRange(_state.Ship, _state.ContactsState.ActiveSensors);
        float elapsed = _state.Mission.ElapsedTime;
        bool reveal = _state.Debug.RevealContacts;

        foreach (var contact in _state.Contacts)
        {
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
                contact.Discovery = DiscoveryState.Detected;
            }
            else if (contact.Discovery == DiscoveryState.Detected)
            {
                contact.Discovery = DiscoveryState.Hidden;
            }
            else if (contact.Discovery == DiscoveryState.Probed && elapsed >= contact.ProbeExpiry)
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
            contact.PositionZ += contact.VelocityZ * delta;
        }
    }

    private void UpdatePatrolDrone(float delta)
    {
        var drone = _state.Contacts.Find(c => c.Id == "patrol_drone");
        if (drone == null) return;

        float t = _state.Mission.ElapsedTime;
        float angularSpeed = 0.15f;
        float angle = t * angularSpeed;
        drone.VelocityX = MathF.Cos(angle) * 8f;
        drone.VelocityY = MathF.Sin(angle) * 8f;
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
                _state.Mission.Log.Add(new MissionLogEntry
                {
                    Timestamp = _state.Mission.ElapsedTime,
                    Source = "Tactical",
                    Message = $"Schwachstelle identifiziert: {contact.DisplayName} (+50% Schaden für Gunner)"
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
    }

    private Contact? FindNearestHostile()
    {
        Contact? best = null;
        float bestDist = float.MaxValue;
        foreach (var c in _state.Contacts)
        {
            if (c.IsDestroyed || c.Discovery != DiscoveryState.Scanned) continue;
            if (c.ThreatLevel < 7) continue;
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

        foreach (var contact in _state.Contacts)
        {
            if (contact.IsDestroyed || contact.Type != ContactType.Hostile) continue;
            if (contact.AttackDamage <= 0) continue;

            float dx = contact.PositionX - _state.Ship.PositionX;
            float dy = contact.PositionY - _state.Ship.PositionY;
            float dist = MathF.Sqrt(dx * dx + dy * dy);

            float effectiveRange = contact.AttackRange;
            if (activeSensors) effectiveRange *= 1.3f;

            contact.IsTargetingPlayer = dist <= effectiveRange;

            if (!contact.IsTargetingPlayer) continue;

            contact.AttackCooldown -= delta;
            if (contact.AttackCooldown > 0) continue;

            contact.AttackCooldown = contact.AttackInterval;

            float evasionMult = _state.Ship.FlightMode == FlightMode.Evasive ? 0.5f : 1f;
            float engagementMult = _state.Engagement == EngagementRule.Defensive ? 0.8f : 1f;
            float rawDamage = contact.AttackDamage * evasionMult * engagementMult * _damageMultiplier;

            float absorbed = rawDamage * shieldAbsorption;
            float hullDamage = rawDamage - absorbed;

            _state.Ship.HullIntegrity = Math.Max(0, _state.Ship.HullIntegrity - hullDamage);

            if (absorbed > 0)
                _state.Ship.Systems[SystemId.Shields].Heat = Math.Clamp(
                    _state.Ship.Systems[SystemId.Shields].Heat + absorbed * 0.5f, 0, ShipSystem.MaxHeat);

            _state.Mission.Log.Add(new MissionLogEntry
            {
                Timestamp = _state.Mission.ElapsedTime,
                Source = "System",
                Message = $"Treffer von {contact.DisplayName}: {hullDamage:F0} Hüllenschaden" +
                    (absorbed > 0 ? $" ({absorbed:F0} absorbiert)" : "")
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

    private void CheckEndConditions(float delta)
    {
        if (_state.Ship.HullIntegrity <= 0 && !_state.Debug.Invulnerable)
        {
            EndMission(MissionResult.Destroyed);
            return;
        }

        if (_state.Debug.Invulnerable && _state.Ship.HullIntegrity < 1)
            _state.Ship.HullIntegrity = 1;

        // Check recovery window completion
        var recoveryEvent = _state.ActiveEvents.Find(e => e.Id == "recovery_window");
        if (recoveryEvent != null)
        {
            var primaryTarget = _state.Contacts.Find(c => c.Id == "primary_target");
            if (primaryTarget != null)
            {
                float dx = _state.Ship.PositionX - primaryTarget.PositionX;
                float dy = _state.Ship.PositionY - primaryTarget.PositionY;
                float dz = _state.Ship.PositionZ - primaryTarget.PositionZ;
                float dist = MathF.Sqrt(dx * dx + dy * dy + dz * dz);

                if (dist < 50f && primaryTarget.ScanProgress >= 100)
                {
                    _state.Mission.PrimaryObjective = ObjectiveStatus.Completed;
                    EndMission(MissionResult.Success);
                    return;
                }
            }
        }

        // Update active events timers
        for (int i = _state.ActiveEvents.Count - 1; i >= 0; i--)
        {
            var evt = _state.ActiveEvents[i];
            // TimeRemaining is decremented here
            if (evt.TimeRemaining > 0)
                evt.TimeRemaining -= delta;

            if (evt.TimeRemaining <= 0 && evt.IsActive)
            {
                evt.IsActive = false;
                evt.IsResolved = true;
            }
        }

        // Bergungsfenster zu Ende ohne Primärziel — intrinsisch ans Event gekoppelt, kein globales Limit
        var recoveryExpired = _state.ActiveEvents.Find(e => e.Id == "recovery_window");
        if (recoveryExpired is { IsActive: false, IsResolved: true }
            && _state.Mission.PrimaryObjective == ObjectiveStatus.InProgress)
        {
            _state.Mission.PrimaryObjective = ObjectiveStatus.Failed;
            EndMission(MissionResult.Failed);
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
            MissionResult.Success => "Voller Erfolg! Primärziel gesichert.",
            MissionResult.Partial => "Teil-Erfolg. Primärziel teilweise erreicht.",
            MissionResult.Failed => "Fehlschlag. Primärziel nicht erreicht.",
            MissionResult.Timeout => "Zeitüberschreitung. Mission abgebrochen.",
            MissionResult.Destroyed => "Schiff zerstört. Mission gescheitert.",
            _ => "Mission beendet."
        };

        _state.Mission.Log.Add(new MissionLogEntry
        {
            Timestamp = _state.Mission.ElapsedTime,
            Source = "System",
            Message = $"Mission beendet: {displayResult}"
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

    public void SetPhaseManually(MissionPhase phase)
    {
        _useStructuredPhases = true;
        _state.Mission.UseStructuredMissionPhases = true;
        TransitionToPhase(phase);
    }

    public void ResetMission()
    {
        _triggeredEvents.Clear();
        _encounterConfig = null;
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
