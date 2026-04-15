using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Godot;
using SpacedOut.Agents;
using SpacedOut.Mission;
using SpacedOut.Run;
using SpacedOut.State;

namespace SpacedOut.Orchestration;

public class DebugCommandHandler
{
    private static int _debugAgentSpawnSeq;

    private readonly Dictionary<string, Action> _commands = new();
    private readonly GameState _state;

    public DebugCommandHandler(
        GameState state,
        MissionController missionController,
        MissionOrchestrator missionOrch,
        RunOrchestrator runOrch,
        Action toggleFlyCamera,
        Action<string> onBiomeChanged,
        Action? broadcastStateUpdates = null)
    {
        _state = state;
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
            }
            GD.Print("[Debug] Alle Kontakte aufgedeckt & gescannt");
        };
        _commands["scan_all"] = () =>
        {
            foreach (var c in state.Contacts)
            {
                if (c.Discovery == DiscoveryState.Hidden || c.IsDestroyed) continue;
                c.ScanProgress = 100;
                c.Discovery = DiscoveryState.Scanned;
                c.IsVisibleOnMainScreen = true;
            }
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
                c.IsDestroyed = true;
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
            target.IsDestroyed = true;
            state.Gunner.SelectedTargetId = null;
            state.Gunner.TargetLockProgress = 0;
            GD.Print($"[Debug] Ziel zerstört: {target.DisplayName}");
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

        // ── Run ─────────────────────────────────────────────────────
        _commands["run_new"] = () => runOrch.StartRun();
        _commands["run_seed42"] = () => runOrch.StartRun(42);
        _commands["run_add_resources"] = () =>
        {
            if (state.ActiveRunState == null) return;
            foreach (var key in new[] { RunResourceIds.Hull, RunResourceIds.SpareParts, RunResourceIds.ScienceData, RunResourceIds.Ammo })
            {
                state.ActiveRunState.Resources.TryGetValue(key, out var v);
                state.ActiveRunState.Resources[key] = v + 5;
            }
            GD.Print("[Debug] +5 auf alle Run-Ressourcen");
        };
        _commands["run_reveal_nodes"] = () =>
        {
            dbg.ShowAllRunNodes = !dbg.ShowAllRunNodes;
            if (dbg.ShowAllRunNodes && state.ActiveRunState != null)
            {
                foreach (var rt in state.ActiveRunState.NodeStates.Values)
                    rt.Knowledge = NodeKnowledgeState.Identified;
            }
            GD.Print($"[Debug] Run-Knoten aufdecken: {dbg.ShowAllRunNodes}");
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

        if (_commands.TryGetValue(command, out var action))
            action();
        else
            GD.PrintErr($"[Debug] Unbekannter Befehl: {command}");
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
            OrbitAngle = px * 0.1f,
            PhaseOffset = py * 0.07f,
        };

        state.Contacts.Add(contact);
        GD.Print($"[Debug] NPC-Agent gespawnt: {def.DisplayName} ({agentTypeId})");
    }

    private static void SetTimeScale(float scale)
    {
        Engine.TimeScale = scale;
        GD.Print($"[Debug] Engine.TimeScale = {scale}x");
    }
}
