using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Godot;
using SpacedOut.Agents;
using SpacedOut.Meta;
using SpacedOut.Mission;
using SpacedOut.Run;
using SpacedOut.State;

namespace SpacedOut.Orchestration;

public class DebugCommandHandler
{
    private static int _debugAgentSpawnSeq;

    private readonly Dictionary<string, Action> _commands = new();
    private readonly GameState _state;
    private readonly MissionOrchestrator _missionOrch;
    private readonly MissionController _missionController;
    private readonly RunOrchestrator _runOrch;
    private readonly Action? _broadcastStateUpdates;
    private readonly MetaProgressService? _metaProgress;

    public DebugCommandHandler(
        GameState state,
        MissionController missionController,
        MissionOrchestrator missionOrch,
        RunOrchestrator runOrch,
        Action toggleFlyCamera,
        Action<string> onBiomeChanged,
        Action? refreshSkybox = null,
        Action? broadcastStateUpdates = null,
        Action? toggleShipScaleCompare = null,
        MetaProgressService? metaProgress = null)
    {
        _state = state;
        _missionOrch = missionOrch;
        _missionController = missionController;
        _runOrch = runOrch;
        _broadcastStateUpdates = broadcastStateUpdates;
        _metaProgress = metaProgress;
        var dbg = state.Debug;
        var ship = state.Ship;
        var gun = state.Gunner;
        var cs = state.ContactsState;

        // ── Godmode ─────────────────────────────────────────────────
        _commands["godmode_toggle"] = () =>
        {
            dbg.ToggleGodMode();
            if (dbg.GodMode)
            {
                ship.HullIntegrity = 100;
                foreach (var sys in ship.Systems.Values)
                {
                    sys.Status = SystemStatus.Operational;
                    sys.Heat = 0;
                    sys.IsRepairing = false;
                }
                cs.ProbeCharges = ContactsState.MaxProbeCharges;
            }
            GD.Print($"[Debug] GodMode {(dbg.GodMode ? "AN" : "AUS")}");
        };
        _commands["god_invulnerable"] = () => { dbg.Invulnerable = !dbg.Invulnerable; GD.Print($"[Debug] Invulnerable: {dbg.Invulnerable}"); };
        _commands["god_noheat"] = () => { dbg.NoHeat = !dbg.NoHeat; GD.Print($"[Debug] NoHeat: {dbg.NoHeat}"); };
        _commands["god_instantscan"] = () => { dbg.InstantScans = !dbg.InstantScans; GD.Print($"[Debug] InstantScans: {dbg.InstantScans}"); };
        _commands["god_instantlock"] = () => { dbg.InstantLock = !dbg.InstantLock; GD.Print($"[Debug] InstantLock: {dbg.InstantLock}"); };
        _commands["god_infprobes"] = () => { dbg.InfiniteProbes = !dbg.InfiniteProbes; GD.Print($"[Debug] InfiniteProbes: {dbg.InfiniteProbes}"); };
        _commands["god_nocooldown"] = () => { dbg.NoCooldowns = !dbg.NoCooldowns; GD.Print($"[Debug] NoCooldowns: {dbg.NoCooldowns}"); };
        _commands["god_reveal"] = () => { dbg.RevealContacts = !dbg.RevealContacts; GD.Print($"[Debug] RevealContacts: {dbg.RevealContacts}"); };

        // ── Time ────────────────────────────────────────────────────
        _commands["time_1"] = () => SetTimeScale(1f);
        _commands["time_2"] = () => SetTimeScale(2f);
        _commands["time_5"] = () => SetTimeScale(5f);
        _commands["time_10"] = () => SetTimeScale(10f);
        _commands["time_freeze"] = () =>
        {
            dbg.FreezeTime = !dbg.FreezeTime;
            Engine.TimeScale = dbg.FreezeTime ? 0f : 1f;
            GD.Print($"[Debug] Zeit {(dbg.FreezeTime ? "eingefroren" : "läuft")}");
        };

        // ── Mission ─────────────────────────────────────────────────
        _commands["mission_start"] = missionOrch.StartMission;
        _commands["mission_reset"] = missionOrch.ResetMission;
        _commands["mission_pause"] = () =>
        {
            state.IsPaused = !state.IsPaused;
            GD.Print($"[Debug] {(state.IsPaused ? "Pausiert" : "Fortgesetzt")}");
        };
        _commands["mission_end_ok"] = () => missionController.DebugForceEndMission(MissionResult.Success);
        _commands["mission_end_partial"] = () => missionController.DebugForceEndMission(MissionResult.Partial);
        _commands["mission_end_fail"] = () => missionController.DebugForceEndMission(MissionResult.Failed);
        _commands["mission_end_timeout"] = () => missionController.DebugForceEndMission(MissionResult.Timeout);

        // ── Ship & Systems ──────────────────────────────────────────
        _commands["hull_minus20"] = () => ship.HullIntegrity = Math.Max(0, ship.HullIntegrity - 20);
        _commands["hull_plus20"] = () => ship.HullIntegrity = Math.Min(100, ship.HullIntegrity + 20);
        _commands["hull_max"] = () => ship.HullIntegrity = 100;
        _commands["hull_critical"] = () => ship.HullIntegrity = 5;
        _commands["repair_all"] = () =>
        {
            foreach (var sys in ship.Systems.Values)
            {
                sys.Status = SystemStatus.Operational;
                sys.Heat = 0;
                sys.IsRepairing = false;
                sys.RepairProgress = 0;
                sys.CoolantCooldown = 0;
            }
            GD.Print("[Debug] Alle Systeme repariert & gekühlt");
        };
        _commands["break_random"] = () =>
        {
            var ids = Enum.GetValues<SystemId>();
            var target = ids[GD.RandRange(0, ids.Length - 1)];
            ship.Systems[target].Status = SystemStatus.Damaged;
            ship.Systems[target].Heat = 80f;
            GD.Print($"[Debug] System beschädigt: {target}");
        };
        _commands["break_all"] = () =>
        {
            foreach (var sys in ship.Systems.Values)
            {
                sys.Status = SystemStatus.Damaged;
                sys.Heat = 90f;
            }
            GD.Print("[Debug] Alle Systeme beschädigt");
        };
        _commands["reset_heat"] = () =>
        {
            foreach (var sys in ship.Systems.Values)
                sys.Heat = 0;
            GD.Print("[Debug] Hitze zurückgesetzt");
        };
        _commands["max_heat"] = () =>
        {
            foreach (var sys in ship.Systems.Values)
                sys.Heat = 90f;
            GD.Print("[Debug] Hitze auf 90%");
        };
        _commands["energy_balanced"] = () =>
        {
            ship.Energy.Drive = 25; ship.Energy.Shields = 25;
            ship.Energy.Sensors = 25; ship.Energy.Weapons = 25;
        };
        _commands["energy_max_weapons"] = () =>
        {
            ship.Energy.Drive = 10; ship.Energy.Shields = 10;
            ship.Energy.Sensors = 10; ship.Energy.Weapons = 70;
        };
        _commands["energy_max_shields"] = () =>
        {
            ship.Energy.Drive = 10; ship.Energy.Shields = 70;
            ship.Energy.Sensors = 10; ship.Energy.Weapons = 10;
        };

        // ── Contacts & Sensors ──────────────────────────────────────
        _commands["reveal_all"] = () =>
        {
            foreach (var c in state.Contacts)
            {
                if (c.IsDestroyed) continue;
                c.Discovery = DiscoveryState.Scanned;
                c.ScanProgress = 100;
                c.IsVisibleOnMainScreen = true;
                c.ReleasedToNav = true;
                if (c.Id == "sector_exit")
                {
                    // Must match MissionController.ClampHiddenExitUntilRelayUnlock: without this flag,
                    // scripted missions (HideExitUntilScanned) snap the exit back to Hidden every tick.
                    state.Mission.JumpCoordinatesUnlocked = true;
                    c.PreRevealed = true;
                }
            }
            _broadcastStateUpdates?.Invoke();
            GD.Print("[Debug] Alle Kontakte aufgedeckt & gescannt");
        };
        _commands["scan_all"] = () =>
        {
            foreach (var c in state.Contacts)
            {
                if (c.IsDestroyed) continue;
                // Hidden contacts are skipped unless this is the sector exit (often script-locked Hidden).
                if (c.Discovery == DiscoveryState.Hidden && c.Id != "sector_exit") continue;
                c.ScanProgress = 100;
                c.Discovery = DiscoveryState.Scanned;
                c.IsVisibleOnMainScreen = true;
                if (c.Id == "sector_exit")
                {
                    state.Mission.JumpCoordinatesUnlocked = true;
                    c.ReleasedToNav = true;
                    c.PreRevealed = true;
                }
            }
            _broadcastStateUpdates?.Invoke();
            GD.Print("[Debug] Alle sichtbaren Kontakte gescannt");
        };
        _commands["reveal_all_resource_zones"] = () =>
        {
            if (!GameFeatures.ResourceZonesEnabled)
            {
                GD.Print("[Debug] Ressourcenzonen sind deaktiviert (GameFeatures.ResourceZonesEnabled).");
                return;
            }

            var sector = missionOrch.CurrentSector;
            if (sector == null)
            {
                GD.Print("[Debug] Kein Sektor — erst Level/Mission laden.");
                return;
            }

            foreach (var zone in sector.ResourceZones)
                zone.Discovery = DiscoveryState.Scanned;

            foreach (var map in state.ResourceZones)
                map.Discovery = DiscoveryState.Scanned.ToString();

            broadcastStateUpdates?.Invoke();
            GD.Print($"[Debug] {sector.ResourceZones.Count} Ressourcenzonen sondiert (Karte + Clients).");
        };
        _commands["spawn_hostile"] = () =>
        {
            var id = $"debug_hostile_{state.Contacts.Count}";
            state.Contacts.Add(new Contact
            {
                Id = id,
                Type = ContactType.Hostile,
                DisplayName = $"Debug-Feind #{state.Contacts.Count}",
                PositionX = ship.PositionX + GD.RandRange(100, 300),
                PositionY = ship.PositionY + GD.RandRange(100, 300),
                PositionZ = ship.PositionZ,
                ThreatLevel = 4,
                Discovery = DiscoveryState.Scanned,
                IsVisibleOnMainScreen = true,
                ReleasedToNav = true,
                HitPoints = 100,
                MaxHitPoints = 100,
                AttackDamage = 8f,
                AttackInterval = 8f,
                AttackRange = 250f,
            });
            GD.Print($"[Debug] Feindlicher Kontakt gespawnt: {id}");
        };
        _commands["spawn_friendly"] = () =>
        {
            var id = $"debug_friendly_{state.Contacts.Count}";
            state.Contacts.Add(new Contact
            {
                Id = id,
                Type = ContactType.Friendly,
                DisplayName = $"Debug-Verbündeter #{state.Contacts.Count}",
                PositionX = ship.PositionX + GD.RandRange(50, 200),
                PositionY = ship.PositionY + GD.RandRange(50, 200),
                PositionZ = ship.PositionZ,
                ThreatLevel = 0,
                Discovery = DiscoveryState.Scanned,
                IsVisibleOnMainScreen = true,
                ReleasedToNav = true,
                PreRevealed = true,
            });
            GD.Print($"[Debug] Verbündeter Kontakt gespawnt: {id}");
        };
        _commands["kill_all_hostiles"] = () =>
        {
            int count = 0;
            foreach (var c in state.Contacts.Where(c => c.Type == ContactType.Hostile && !c.IsDestroyed))
            {
                c.HitPoints = 0;
                c.ApplyCombatDestruction();
                count++;
            }
            GD.Print($"[Debug] {count} Feinde zerstört");
        };
        _commands["probes_max"] = () =>
        {
            cs.ProbeCharges = ContactsState.MaxProbeCharges;
            GD.Print("[Debug] Sonden aufgefüllt");
        };
        _commands["toggle_active_sensors"] = () =>
        {
            cs.ActiveSensors = !cs.ActiveSensors;
            GD.Print($"[Debug] Aktive Sensoren: {cs.ActiveSensors}");
        };

        // ── Gunner ──────────────────────────────────────────────────
        _commands["gunner_instant_lock"] = () =>
        {
            state.Gunner.TargetLockProgress = 100f;
            GD.Print("[Debug] Target Lock: 100%");
        };
        _commands["gunner_reset_cooldown"] = () =>
        {
            state.Gunner.FireCooldown = 0;
            GD.Print("[Debug] Feuer-Cooldown zurückgesetzt");
        };
        _commands["gunner_kill_target"] = () =>
        {
            if (string.IsNullOrEmpty(state.Gunner.SelectedTargetId)) return;
            var target = state.Contacts.Find(c => c.Id == state.Gunner.SelectedTargetId);
            if (target == null) return;
            target.HitPoints = 0;
            target.ApplyCombatDestruction();
            state.Gunner.SelectedTargetId = null;
            state.Gunner.TargetLockProgress = 0;
            GD.Print($"[Debug] Ziel zerstört: {target.DisplayName}");
        };
        _commands["fx_test_shot"] = () =>
        {
            string? targetId = state.Gunner.SelectedTargetId;
            if (string.IsNullOrEmpty(targetId))
            {
                var hostile = state.Contacts.Find(c =>
                    c.Type == ContactType.Hostile && !c.IsDestroyed);
                targetId = hostile?.Id;
            }
            if (string.IsNullOrEmpty(targetId))
            {
                GD.Print("[Debug] fx_test_shot: kein Ziel (weder gelockt noch feindlich).");
                return;
            }

            var visual = state.Gunner.Mode == WeaponMode.Precision
                ? WeaponVisualKind.LaserBeam
                : WeaponVisualKind.KineticTracer;

            state.CombatFx.PendingShots.Add(new ShotEvent
            {
                ShooterId = "player",
                TargetId = targetId,
                Visual = visual,
                Hit = true,
                TimestampSec = state.Mission.ElapsedTime,
            });
            GD.Print($"[Debug] fx_test_shot -> {targetId} ({visual})");
        };

        // ── Events ──────────────────────────────────────────────────
        _commands["event_sensor"] = () => missionController.TriggerEventManually("sensor_shimmer");
        _commands["event_shield"] = () => missionController.TriggerEventManually("shield_stress");
        _commands["event_contact"] = () => missionController.TriggerEventManually("unknown_approach");
        _commands["event_recovery"] = () => missionController.TriggerEventManually("recovery_window");

        // ── Phases ──────────────────────────────────────────────────
        _commands["phase_anflug"] = () => missionController.SetPhaseManually(MissionPhase.Anflug);
        _commands["phase_stoerung"] = () => missionController.SetPhaseManually(MissionPhase.Stoerung);
        _commands["phase_krise"] = () => missionController.SetPhaseManually(MissionPhase.Krisenfenster);
        _commands["phase_abschluss"] = () => missionController.SetPhaseManually(MissionPhase.Abschluss);

        // ── Level / Biome ───────────────────────────────────────────
        _commands["toggle_fly_camera"] = toggleFlyCamera;
        _commands["regen_level"] = () =>
        {
            missionOrch.RegenerateLevel();
            onBiomeChanged("");
        };
        _commands["biome_asteroid"] = () => { missionOrch.RegenerateBiome("asteroid_field"); onBiomeChanged("asteroid_field"); };
        _commands["biome_wreck"] = () => { missionOrch.RegenerateBiome("wreck_zone"); onBiomeChanged("wreck_zone"); };
        _commands["biome_station"] = () => { missionOrch.RegenerateBiome("station_periphery"); onBiomeChanged("station_periphery"); };
        _commands["skybox_toggle"] = () =>
        {
            GameFeatures.SkyboxEnabled = !GameFeatures.SkyboxEnabled;
            refreshSkybox?.Invoke();
            GD.Print($"[Debug] Skybox {(GameFeatures.SkyboxEnabled ? "AN" : "AUS")}");
        };
        _commands["ship_scale_compare"] = () =>
        {
            if (toggleShipScaleCompare == null)
            {
                GD.PrintErr("[Debug] Schiff-Vergleich: nicht angebunden.");
                return;
            }
            toggleShipScaleCompare();
        };

        // ── Run ─────────────────────────────────────────────────────
        _commands["run_new"] = () => runOrch.StartRun(missionController: missionController);
        _commands["run_seed42"] = () => runOrch.StartRun(42, missionController);
        _commands["run_add_resources"] = () =>
        {
            if (state.ActiveRunState == null) return;
            foreach (var key in new[] { RunResourceIds.SpareParts, RunResourceIds.ScienceData, RunResourceIds.Fuel, RunResourceIds.Credits })
            {
                state.ActiveRunState.Resources.TryGetValue(key, out var v);
                state.ActiveRunState.Resources[key] = v + 5;
            }
            GD.Print("[Debug] +5 auf alle Run-Ressourcen");
        };
        // ── Dock (M5) ───────────────────────────────────────────────
        _commands["dock_force_dock"] = () =>
        {
            if (state.Mission.Dock == null)
            {
                GD.Print("[Debug] Kein Dock in diesem Sektor (nur in Station-Sektoren).");
                return;
            }
            state.Mission.Docked = true;
            state.Mission.DockedContactId = "station_dock";
            GD.Print("[Debug] Dock erzwungen.");
        };
        _commands["dock_repair"] = () =>
        {
            if (state.Mission.Dock == null || !state.Mission.Docked)
            {
                GD.Print("[Debug] Nicht angedockt.");
                return;
            }
            if (state.ActiveRunState == null) return;
            state.ActiveRunState.Resources.TryGetValue(RunResourceIds.SpareParts, out var have);
            if (have < 1) { GD.Print("[Debug] Nicht genug Ersatzteile."); return; }
            state.ActiveRunState.Resources[RunResourceIds.SpareParts] = have - 1;
            ship.HullIntegrity = Math.Min(100f, ship.HullIntegrity + state.Mission.Dock.HullPerPart);
            GD.Print($"[Debug] Reparatur: 1 Teil → Hülle {ship.HullIntegrity:F0}%");
        };
        _commands["dock_buy_fuel"] = () =>
        {
            if (state.Mission.Dock == null || !state.Mission.Docked)
            {
                GD.Print("[Debug] Nicht angedockt.");
                return;
            }
            if (state.ActiveRunState == null) return;
            int price = state.Mission.Dock.FuelPrice;
            state.ActiveRunState.Resources.TryGetValue(RunResourceIds.Credits, out var cr);
            if (cr < price) { GD.Print("[Debug] Nicht genug Credits."); return; }
            state.ActiveRunState.Resources[RunResourceIds.Credits] = cr - price;
            state.ActiveRunState.Resources.TryGetValue(RunResourceIds.Fuel, out var f);
            state.ActiveRunState.Resources[RunResourceIds.Fuel] = f + 1;
            GD.Print($"[Debug] Kauf: 1 Fuel für {price} Credits.");
        };

        _commands["run_reveal_nodes"] = () =>
        {
            dbg.ShowAllRunNodes = !dbg.ShowAllRunNodes;
            if (dbg.ShowAllRunNodes && state.ActiveRunState != null)
            {
                foreach (var rt in state.ActiveRunState.NodeStates.Values)
                    rt.Knowledge = NodeKnowledgeState.Scanned;
            }
            GD.Print($"[Debug] Run-Knoten aufdecken: {dbg.ShowAllRunNodes}");
            broadcastStateUpdates?.Invoke();
            runOrch.BroadcastRunState();
        };
        // ── Meta (M7) ───────────────────────────────────────────────
        _commands["meta_show"] = () =>
        {
            if (_metaProgress == null) { GD.Print("[Debug] MetaProgressService nicht verfügbar."); return; }
            var p = _metaProgress.Profile;
            GD.Print($"[Debug] Meta: Stardust={p.Stardust} Runs={p.RunsCompleted} SelectedPerk={p.SelectedPerkId ?? "(none)"} Unlocks=[{string.Join(", ", p.UnlockedIds)}]");
        };
        _commands["meta_unlock_all"] = () =>
        {
            if (_metaProgress == null) { GD.Print("[Debug] MetaProgressService nicht verfügbar."); return; }
            foreach (var def in UnlockCatalog.All)
                _metaProgress.Profile.UnlockedIds.Add(def.Id);
            _metaProgress.Save();
            GD.Print($"[Debug] Alle Unlocks gewährt ({UnlockCatalog.All.Count}). Sternenstaub unverändert.");
            broadcastStateUpdates?.Invoke();
        };
        _commands["meta_reset"] = () =>
        {
            if (_metaProgress == null) { GD.Print("[Debug] MetaProgressService nicht verfügbar."); return; }
            _metaProgress.ResetProfile();
            broadcastStateUpdates?.Invoke();
        };

        _commands["run_scan_all"] = () =>
        {
            if (state.ActiveRunState == null)
            {
                GD.Print("[Debug] Kein aktiver Run.");
                return;
            }
            int count = 0;
            foreach (var rt in state.ActiveRunState.NodeStates.Values)
            {
                if (rt.Knowledge != NodeKnowledgeState.Scanned)
                {
                    rt.Knowledge = NodeKnowledgeState.Scanned;
                    count++;
                }
            }
            GD.Print($"[Debug] {count} Knoten auf Scanned gesetzt.");
            broadcastStateUpdates?.Invoke();
            runOrch.BroadcastRunState();
        };
    }

    public void Execute(string command)
    {
        const string spawnAgentPrefix = "spawn_agent:";
        if (command.StartsWith(spawnAgentPrefix, StringComparison.Ordinal))
        {
            SpawnDebugAgent(command[spawnAgentPrefix.Length..]);
            return;
        }

        const string spawnPoiPrefix = "spawn_poi:";
        if (command.StartsWith(spawnPoiPrefix, StringComparison.Ordinal))
        {
            _missionOrch.DebugSpawnPoiMarker(command[spawnPoiPrefix.Length..]);
            return;
        }

        const string eventTriggerPrefix = "event_trigger:";
        if (command.StartsWith(eventTriggerPrefix, StringComparison.Ordinal))
        {
            DebugTriggerEvent(command[eventTriggerPrefix.Length..], forceNow: false);
            return;
        }

        const string eventFireNowPrefix = "event_fire_now:";
        if (command.StartsWith(eventFireNowPrefix, StringComparison.Ordinal))
        {
            DebugTriggerEvent(command[eventFireNowPrefix.Length..], forceNow: true);
            return;
        }

        if (command == "event_list")
        {
            DebugListEvents();
            return;
        }

        const string metaGrantPrefix = "meta_grant:";
        if (command.StartsWith(metaGrantPrefix, StringComparison.Ordinal))
        {
            if (_metaProgress == null) { GD.Print("[Debug] MetaProgressService nicht verfügbar."); return; }
            if (int.TryParse(command[metaGrantPrefix.Length..], out int amount))
            {
                _metaProgress.GrantStardust(amount);
                GD.Print($"[Debug] +{amount} Sternenstaub (Gesamt {_metaProgress.Profile.Stardust})");
                _broadcastStateUpdates?.Invoke();
            }
            else
            {
                GD.PrintErr($"[Debug] meta_grant:N — N war keine Zahl: '{command[metaGrantPrefix.Length..]}'");
            }
            return;
        }

        if (_commands.TryGetValue(command, out var action))
            action();
        else
            GD.PrintErr($"[Debug] Unbekannter Befehl: {command}");
    }

    /// <summary>Lists all <see cref="NodeEventCatalog"/> entries with fire-status for the active run.</summary>
    private void DebugListEvents()
    {
        var fired = _state.ActiveRunState?.FiredEventIds ?? new HashSet<string>();
        GD.Print($"[Debug] NodeEventCatalog ({NodeEventCatalog.All.Count} Events):");
        foreach (var evt in NodeEventCatalog.All)
        {
            string marker = fired.Contains(evt.Id) ? " [fired]" : "";
            GD.Print($"  {evt.Id,-28} {evt.Trigger,-10} {evt.Title}{marker}");
        }
    }

    /// <summary>
    /// Forces a catalog event to fire. PreSector ⇒ stages a decision on the run map.
    /// InSector ⇒ registers a synthetic event/decision and fires it immediately via
    /// <see cref="MissionController.FireRuntimeTriggerNow"/>. <paramref name="forceNow"/> ignores the
    /// one-shot guard in <see cref="RunStateData.FiredEventIds"/>.
    /// </summary>
    private void DebugTriggerEvent(string eventId, bool forceNow)
    {
        var evt = NodeEventCatalog.GetOrNull(eventId);
        if (evt == null)
        {
            GD.PrintErr($"[Debug] Event {eventId} nicht im Katalog.");
            return;
        }

        var run = _state.ActiveRunState;
        if (run != null && !forceNow)
            run.FiredEventIds.Add(eventId);

        if (evt.Trigger == NodeEventTrigger.PreSector)
        {
            _state.Mission.PreSectorEventActive = true;
            _state.Mission.PendingPreSectorEventId = evt.Id;
            _state.Mission.PendingPreSectorEventTitle = evt.Title;
            _state.Mission.PendingPreSectorNodeId = run?.CurrentNodeId ?? "";
            _state.AddPendingDecision(new MissionDecision
            {
                Id = Commands.Handlers.CaptainNavCommandHandler.PreSectorDecisionId(evt.Id),
                Title = evt.Title,
                Description = evt.Description,
                Options = evt.Options,
            });
            GD.Print($"[Debug] Pre-Sector Event forciert: {evt.Id}");
            _broadcastStateUpdates?.Invoke();
            return;
        }

        string decisionId = $"event:{evt.Id}";
        string synthEventId = $"event_banner:{evt.Id}";

        _missionController.RegisterRuntimeDecision(decisionId, new ScriptedDecision
        {
            Title = evt.Title,
            Description = evt.Description,
            Options = evt.Options,
        });
        _missionController.RegisterRuntimeEvent(synthEventId, new ScriptedEvent
        {
            Title = evt.Title,
            Description = evt.Description,
            Duration = 30f,
            DecisionId = decisionId,
            LogEntry = $"Funkspruch: {evt.Title}",
        });

        if (forceNow || !_state.MissionStarted)
            _missionController.FireRuntimeTriggerNow(synthEventId, eventId: synthEventId, decisionId: decisionId);
        else
            _missionController.AddRuntimeTimeTrigger(new TimeTrigger
            {
                EventId = synthEventId,
                Time = _state.Mission.ElapsedTime + 0.1f,
                Once = true,
            });

        GD.Print($"[Debug] In-Sector Event forciert: {evt.Id}");
        _broadcastStateUpdates?.Invoke();
    }

    private void SpawnDebugAgent(string agentTypeId)
    {
        if (!AgentDefinition.TryGet(agentTypeId, out var def))
        {
            GD.PrintErr($"[Debug] Unbekannter Agent-Typ: {agentTypeId}");
            return;
        }

        var state = _state;
        var ship = state.Ship;

        float px = ship.PositionX + GD.RandRange(80, 280);
        float py = ship.PositionY + GD.RandRange(80, 280);
        float pz = ship.PositionZ;

        int serial = Interlocked.Increment(ref _debugAgentSpawnSeq);
        var id = $"debug_agent_{agentTypeId}_{serial}";

        var contact = new Contact
        {
            Id = id,
            Type = def.ContactType,
            DisplayName = $"{def.DisplayName} ({serial})",
            PositionX = px,
            PositionY = py,
            PositionZ = pz,
            ThreatLevel = def.ThreatLevel,
            Discovery = DiscoveryState.Scanned,
            IsVisibleOnMainScreen = true,
            ReleasedToNav = true,
            HitPoints = def.HitPoints,
            MaxHitPoints = def.HitPoints,
            AttackDamage = def.AttackDamage,
            AttackInterval = def.AttackInterval,
            AttackRange = def.AttackRange,
        };

        if (def.ContactType is ContactType.Friendly or ContactType.Neutral)
            contact.PreRevealed = true;

        float anchorX = px;
        float anchorY = py;
        float anchorZ = pz;
        float destX = px;
        float destY = py;

        if (def.InitialMode == AgentBehaviorMode.Transit)
        {
            float angle = GD.Randf() * Mathf.Tau;
            destX = 500f + MathF.Cos(angle) * 420f;
            destY = 500f + MathF.Sin(angle) * 420f;
        }

        contact.Agent = new AgentState
        {
            AgentType = agentTypeId,
            Mode = def.InitialMode,
            AnchorX = anchorX,
            AnchorY = anchorY,
            AnchorZ = anchorZ,
            DestinationX = destX,
            DestinationY = destY,
            DetectionRadius = def.DetectionRadius,
            FleeThreshold = def.FleeThreshold,
            BaseSpeed = def.BaseSpeed,
            WeaponAccuracy = def.WeaponAccuracy,
            ShieldAbsorption = def.ShieldAbsorption,
        };
        AgentSpawnPersonality.Apply(contact.Agent, contact.Id, contact.PositionX, contact.PositionY, def.BaseSpeed);

        state.Contacts.Add(contact);
        GD.Print($"[Debug] NPC-Agent gespawnt: {def.DisplayName} ({agentTypeId})");
    }

    private static void SetTimeScale(float scale)
    {
        Engine.TimeScale = scale;
        GD.Print($"[Debug] Engine.TimeScale = {scale}x");
    }
}
