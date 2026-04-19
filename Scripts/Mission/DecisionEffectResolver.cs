using System;
using System.Collections.Generic;
using Godot;
using SpacedOut.Agents;
using SpacedOut.Orchestration;
using SpacedOut.Run;
using SpacedOut.State;

namespace SpacedOut.Mission;

/// <summary>
/// Applies <see cref="DecisionEffects"/> from a <see cref="DecisionOption"/> to the run and mission state.
/// Keeps effect semantics in one place so Pre-Sector and In-Sector events share identical behaviour.
/// Does <b>not</b> handle <see cref="DecisionEffects.SkipSector"/> — the caller must branch on it.
/// </summary>
public static class DecisionEffectResolver
{
    /// <summary>
    /// Apply <paramref name="effects"/>. Missing dependencies are tolerated
    /// (e.g. no active run ⇒ resource deltas are skipped; no mission controller ⇒ spawns are skipped).
    /// </summary>
    /// <param name="effects">Effects to apply (null-safe).</param>
    /// <param name="state">Global game state (ship systems, mission log).</param>
    /// <param name="runState">Current run state (resources, flags, persistent hull).</param>
    /// <param name="mission">Active mission controller for runtime trigger/spawn injection (may be null for pre-sector).</param>
    /// <param name="addLog">Optional log sink; if null, <see cref="GameState.AddMissionLogEntry"/> is used.</param>
    /// <param name="sourceLabel">Log source label (e.g. <c>"Event"</c>).</param>
    public static void Apply(
        DecisionEffects? effects,
        GameState state,
        RunStateData? runState,
        MissionController? mission,
        Action<string, string>? addLog = null,
        string sourceLabel = "Event",
        MissionOrchestrator? orchestrator = null)
    {
        if (effects == null || state == null) return;

        ApplyResourceDeltas(effects.ResourceDeltas, runState);
        ApplyHullDelta(effects.HullDelta, state, runState);
        ApplyFlags(effects.FlagsToSet, effects.FlagsToClear, runState);
        ApplySystemEffects(effects.SystemEffects, state);
        ApplySpawns(effects.SpawnAgents, mission, state, runState);
        ApplyPoiSpawns(effects.SpawnPois, orchestrator);
        ApplyLog(effects.LogSummary, state, addLog, sourceLabel);
    }

    private static void ApplyResourceDeltas(Dictionary<string, int>? deltas, RunStateData? runState)
    {
        if (deltas == null || deltas.Count == 0 || runState == null) return;
        foreach (var kv in deltas)
        {
            if (string.IsNullOrEmpty(kv.Key) || kv.Value == 0) continue;
            runState.Resources.TryGetValue(kv.Key, out int current);
            int next = Math.Max(0, current + kv.Value);
            runState.Resources[kv.Key] = next;
        }
    }

    private static void ApplyHullDelta(float delta, GameState state, RunStateData? runState)
    {
        if (Math.Abs(delta) < 0.001f) return;

        // M7: respect MaxHullOverride from perks (e.g. perk_armor) as the upper cap.
        float cap = runState?.MaxHullOverride ?? 100f;
        float newHull = Math.Clamp(state.Ship.HullIntegrity + delta, 0f, cap);
        state.Ship.HullIntegrity = newHull;
        if (runState != null)
            runState.CurrentHull = newHull;
    }

    private static void ApplyFlags(List<string>? set, List<string>? clear, RunStateData? runState)
    {
        if (runState == null) return;
        if (set != null)
        {
            foreach (var f in set)
                if (!string.IsNullOrEmpty(f)) runState.Flags.Add(f);
        }
        if (clear != null)
        {
            foreach (var f in clear)
                if (!string.IsNullOrEmpty(f)) runState.Flags.Remove(f);
        }
    }

    private static void ApplySystemEffects(List<SystemEffect>? fx, GameState state)
    {
        if (fx == null || fx.Count == 0) return;
        foreach (var effect in fx)
        {
            if (!state.Ship.Systems.TryGetValue(effect.System, out var sys)) continue;
            sys.Heat = Math.Clamp(sys.Heat + effect.HeatDelta, 0f, ShipSystem.MaxHeat);
            if (effect.SetStatus.HasValue)
                sys.Status = effect.SetStatus.Value;
        }
    }

    private static void ApplySpawns(List<DeferredAgentSpawn>? spawns, MissionController? mission,
        GameState state, RunStateData? runState)
    {
        if (spawns == null || spawns.Count == 0 || mission == null) return;

        int risk = state.Mission.NodeRiskRating;
        var pacing = runState?.Pacing;
        var eligible = new List<DeferredAgentSpawn>(spawns.Count);

        foreach (var s in spawns)
        {
            if (s == null) continue;
            if (s.MinRisk > risk) continue;

            // Director Threat-Pool: drop expensive spawns when pool is empty so authored
            // worst-case fights don't crush the player on a depleted run. Cheap (cost<=0) and
            // non-hostile (cost==1) spawns always pass.
            if (pacing != null)
            {
                int cost = AgentSpawnConfig.GetCost(s.AgentType);
                if (cost >= 2 && pacing.ThreatPool < cost)
                {
                    GD.Print($"[DecisionEffectResolver] Drop spawn {s.AgentType} (cost {cost}, pool {pacing.ThreatPool:F1}).");
                    continue;
                }
                if (cost > 0)
                {
                    pacing.ThreatPool = MathF.Max(0f, pacing.ThreatPool - cost);
                    pacing.LastDrainAmount = cost;
                    pacing.LastDrainReason = $"spawn:{s.AgentType}";
                }
            }
            eligible.Add(s);
        }
        if (eligible.Count == 0) return;

        // PreSector decisions resolve while the upcoming sector hasn't been built yet — firing
        // immediately would either spawn into the stale previous sector or be wiped by the
        // MissionGenerator.PopulateMission Contacts.Clear() during sector build. Route through
        // the controller's sector-entry queue so the spawns materialise once StartMission runs
        // for the new sector. Generic across all current and future PreSector events.
        if (state.Mission.PreSectorEventActive)
        {
            mission.QueueSectorEntrySpawns(eligible);
            return;
        }

        mission.QueueDeferredSpawns(eligible);

        // In-sector path: fire all distinct trigger ids immediately so the spawns resolve now.
        var fired = new HashSet<string>();
        foreach (var s in eligible)
        {
            if (string.IsNullOrEmpty(s.TriggerId)) continue;
            if (!fired.Add(s.TriggerId)) continue;
            mission.FireRuntimeTriggerNow(s.TriggerId);
        }
    }

    private static void ApplyPoiSpawns(List<DeferredPoiSpawn>? pois, MissionOrchestrator? orchestrator)
    {
        if (pois == null || pois.Count == 0) return;
        if (orchestrator == null) return; // pre-sector path: no live sector to spawn into.

        foreach (var p in pois)
        {
            if (p == null) continue;
            orchestrator.SpawnRuntimePoi(p);
        }
    }

    private static void ApplyLog(string? summary, GameState state,
        Action<string, string>? addLog, string source)
    {
        if (string.IsNullOrWhiteSpace(summary)) return;
        if (addLog != null)
        {
            addLog(source, summary);
            return;
        }

        state.AddMissionLogEntry(new MissionLogEntry
        {
            Timestamp = state.Mission.ElapsedTime,
            Source = source,
            Message = summary.Trim(),
        });
    }
}
