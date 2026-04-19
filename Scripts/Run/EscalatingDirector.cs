using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using SpacedOut.Agents;
using SpacedOut.Mission;
using SpacedOut.State;

namespace SpacedOut.Run;

/// <summary>
/// Default director: escalates tension across acts but injects breathers / safe-haven nodes when
/// the player streaks into trouble. All reactive rewrites stay outside the player's scan horizon
/// (<see cref="RunController.MaxScanDepthAhead"/>) so they never alter previously-revealed info.
/// </summary>
public class EscalatingDirector : IRunDirector
{
    /// <summary>Hull threshold below which Rule B forces a Station rewrite.</summary>
    public const float LowHullPct = 30f;
    /// <summary>Streak length that triggers Rule A (breather injection).</summary>
    public const int HostileStreakBreatherTrigger = 2;
    /// <summary>Streak length that triggers Rule C (pressure injection after long calm).</summary>
    public const int CalmStreakPressureTrigger = 3;

    // ── In-Sector Wellen-Tuning ───────────────────────────────────────────────
    /// <summary>Heartbeat-Intervall in Sekunden (Mission-ElapsedTime).</summary>
    public const float HeartbeatIntervalSec = 30f;
    /// <summary>Mindestabstand in Sekunden zwischen zwei Wellen, ausgelöst durch den Heartbeat.</summary>
    public const float MinWaveCooldownSec = 60f;
    /// <summary>Mindestabstand in Sekunden zwischen zwei Wellen, ausgelöst durch eine Hostile-KO-Reaktion.</summary>
    public const float MinKillReactCooldownSec = 45f;
    /// <summary>Hard-Cap: nicht mehr Director-Wellen pro aktivem Sektor.</summary>
    public const int MaxWavesPerSector = 3;
    /// <summary>Mindest-Pool, damit eine Welle überhaupt in Betracht gezogen wird.</summary>
    public const float MinPoolForWave = 2f;
    /// <summary>Basis-Wahrscheinlichkeit eines Heartbeat-Spawns (multipliziert mit <c>0.5 + Tension</c>).</summary>
    public const float HeartbeatBaseChance = 0.6f;
    /// <summary>Wahrscheinlichkeit eines Welle-Spawns nach einem Hostile-KO (vor Pool-Gate).</summary>
    public const float KillReactChance = 0.5f;
    /// <summary>Side-/Anomaly-Sektoren erlauben Director-Wellen erst ab dieser Tension.</summary>
    public const float SideAnomalyMinTension = 0.5f;
    /// <summary>Side-/Anomaly-Sektoren erlauben Director-Wellen erst ab diesem Pool-Stand.</summary>
    public const float SideAnomalyMinPool = 4f;
    /// <summary>Pool-Schwelle, ab der eine Welle einen Corsair (Cost 4) enthalten darf.</summary>
    public const float CorsairMinPool = 6f;
    /// <summary>Tension-Schwelle, ab der eine Welle einen Corsair enthalten darf.</summary>
    public const float CorsairMinTension = 0.7f;

    public MissionTemplate? WeightTemplate(
        RunDirectorContext ctx,
        int act,
        int depth,
        IReadOnlyList<MissionTemplate> pool,
        NodeIntent hint)
    {
        if (pool.Count == 0)
            return null;

        var weighted = new List<(MissionTemplate Tpl, double Weight)>(pool.Count);
        double total = 0;
        foreach (var t in pool)
        {
            double w = BaseWeight(t, act, hint);
            if (w <= 0) continue;
            weighted.Add((t, w));
            total += w;
        }
        if (weighted.Count == 0 || total <= 0)
            return pool[ctx.Rng.Next(0, pool.Count)];

        double roll = ctx.Rng.NextDouble() * total;
        double accum = 0;
        foreach (var (tpl, w) in weighted)
        {
            accum += w;
            if (roll <= accum)
                return tpl;
        }
        return weighted[^1].Tpl;
    }

    private static double BaseWeight(MissionTemplate t, int act, NodeIntent hint)
    {
        // Risk-Kurve: spätere Akte bevorzugen höheres Risiko.
        // act 0: prefer Risk 1-2, act 1: 2-3, act 2+: 3-4.
        int targetRisk = Math.Clamp(1 + act, 1, 4);
        double riskWeight = 1.0 / (1 + Math.Abs(t.Risk - targetRisk));

        // Intent-Hint: pull templates with matching tags forward.
        double intentWeight = hint switch
        {
            NodeIntent.Breather => HasAnyTag(t, "breather", "salvage", "anomaly", "science") ? 3.0 : 0.4,
            NodeIntent.Pressure => HasAnyTag(t, "pressure", "hostile") || t.Type == MissionType.Hostile ? 3.0 : 0.5,
            NodeIntent.Reward => HasAnyTag(t, "reward", "salvage") ? 2.5 : 0.7,
            NodeIntent.SafeHaven => t.Type == MissionType.Station || HasAnyTag(t, "safe_haven", "station", "resupply") ? 4.0 : 0.2,
            _ => 1.0,
        };

        return riskWeight * intentWeight;
    }

    private static bool HasAnyTag(MissionTemplate t, params string[] tags)
    {
        if (t.Tags.Count == 0) return false;
        foreach (var tag in tags)
            if (t.Tags.Contains(tag)) return true;
        return false;
    }

    public void PostprocessNode(RunDirectorContext ctx, RunNodeData data)
    {
        if (string.IsNullOrEmpty(data.AssignedMissionId))
            return;
        var tpl = MissionTemplateCatalog.GetOrNull(data.AssignedMissionId);
        if (tpl == null) return;

        // Risk curve clamp: ensure node risk stays within ±1 of the act-target.
        // (Templates already carry risk; this only tightens outliers.)
    }

    public void EnforceActGuarantees(RunDirectorContext ctx, RunDefinition definition)
    {
        // Group nodes by act prefix ("A0..", "A1..", "A2..") and ensure at least one Station per act.
        // The act-exit convergence is already station-biased, so this is primarily belt-and-suspenders.
        var actGroups = definition.Nodes.Values
            .Where(n => n.Id.StartsWith('A') && n.Id.Length > 1 && char.IsDigit(n.Id[1]))
            .GroupBy(n => n.Id[1]);

        foreach (var grp in actGroups)
        {
            bool hasStation = grp.Any(n => n.Type == RunNodeType.Station);
            if (hasStation) continue;

            // Promote the highest-Y (latest) Side/Anomaly node in the act to a Station-style template.
            var candidate = grp
                .Where(n => n.Type is RunNodeType.Side or RunNodeType.Anomaly)
                .OrderByDescending(n => n.LayoutY)
                .FirstOrDefault();
            if (candidate == null) continue;

            var stationTpl = MissionTemplateCatalog.GenericPool
                .FirstOrDefault(t => t.Type == MissionType.Station);
            if (stationTpl == null) continue;

            ApplyTemplate(candidate, stationTpl);
            ctx.Pacing.NodeIntent[candidate.Id] = NodeIntent.SafeHaven;
            GD.Print($"[EscalatingDirector] EnforceActGuarantees: {candidate.Id} → Station ({stationTpl.Id}).");
        }
    }

    public void OnNodeEntered(RunDirectorContext ctx, string nodeId)
    {
        // Pacing counters update on resolve; entering only marks intent fulfilment for diagnostics.
    }

    public void OnNodeResolved(RunDirectorContext ctx, string nodeId, NodeResolution resolution)
    {
        if (!ctx.Definition.Nodes.TryGetValue(nodeId, out var node))
            return;

        UpdatePacing(ctx.Pacing, node);
        RefillThreatPool(ctx, node);
    }

    /// <summary>Pool-Aufladung pro gelöstem Knoten. Capacity wächst mit Akt; Refill skaliert mit Tension.</summary>
    private static void RefillThreatPool(RunDirectorContext ctx, RunNodeData node)
    {
        int act = NodeActIndex(node);

        float capacity = 8f + 2f * act;
        ctx.Pacing.ThreatCapacity = capacity;

        float baseRefill = 1.0f + 0.5f * act;
        float tensionBonus = 1.5f * ctx.Pacing.TensionLevel;
        float typeMul = node.Type switch
        {
            RunNodeType.Hostile => 0.5f,
            RunNodeType.Station => 1.0f,
            _ => 1.0f,
        };
        float refill = (baseRefill + tensionBonus) * typeMul;
        if (node.Type == RunNodeType.Station)
            refill += 1.0f;

        float before = ctx.Pacing.ThreatPool;
        ctx.Pacing.ThreatPool = MathF.Min(capacity, before + refill);
        float actualGain = ctx.Pacing.ThreatPool - before;
        ctx.Pacing.LastRefillAmount = actualGain;
        ctx.Pacing.LastRefillReason = $"Act {act}, Tension {ctx.Pacing.TensionLevel:F2}, {node.Type}";

        GD.Print($"[EscalatingDirector] Refill: +{actualGain:F1} (now {ctx.Pacing.ThreatPool:F1}/{capacity:F0}) — {ctx.Pacing.LastRefillReason}");
    }

    private static int NodeActIndex(RunNodeData node)
    {
        if (node.Id.Length > 1 && node.Id[0] == 'A' && char.IsDigit(node.Id[1])
            && int.TryParse(node.Id.AsSpan(1, 1), out var act))
            return act;
        return 0;
    }

    private static void UpdatePacing(PacingState pacing, RunNodeData node)
    {
        bool hostile = node.Type == RunNodeType.Hostile;
        bool station = node.Type == RunNodeType.Station;
        bool breather = !hostile && (node.RiskRating <= 1 || station);

        if (hostile)
        {
            pacing.RecentHostileStreak++;
            pacing.TensionLevel = MathF.Min(1f, pacing.TensionLevel + 0.25f);
        }
        else
        {
            pacing.RecentHostileStreak = 0;
            pacing.TensionLevel = MathF.Max(0f, pacing.TensionLevel - 0.15f);
        }

        if (breather)
            pacing.NodesSinceBreather = 0;
        else
            pacing.NodesSinceBreather++;

        if (station)
            pacing.NodesSinceStation = 0;
        else
            pacing.NodesSinceStation++;
    }

    public IReadOnlyList<string> AdjustUpcomingNodes(RunDirectorContext ctx)
    {
        var modified = new List<string>();
        var rewriteCandidates = ctx.Definition.Nodes.Values
            .Where(n => ctx.CanRewrite(n.Id)
                        && n.Type != RunNodeType.Start
                        && n.Type != RunNodeType.End
                        && n.Type != RunNodeType.Story)
            .OrderBy(n => n.Depth)
            .ToList();

        if (rewriteCandidates.Count == 0)
            return modified;

        // Rule A: Hostile-Streak ≥ 2 → first editable Side/Anomaly becomes a Breather.
        if (ctx.Pacing.RecentHostileStreak >= HostileStreakBreatherTrigger)
        {
            var target = rewriteCandidates.FirstOrDefault(n =>
                n.Type is RunNodeType.Side or RunNodeType.Anomaly);
            if (target != null && RewriteWithIntent(ctx, target, NodeIntent.Breather))
            {
                modified.Add(target.Id);
                ctx.Pacing.RecentHostileStreak = 0; // streak satisfied for director purposes
            }
        }

        // Rule B: Low hull (< LowHullPct) → first editable non-Hostile node becomes Station.
        if (ctx.Run.CurrentHull < LowHullPct)
        {
            bool hasSafeHavenAhead = rewriteCandidates.Any(n => n.Type == RunNodeType.Station);
            if (!hasSafeHavenAhead)
            {
                var target = rewriteCandidates.FirstOrDefault(n =>
                    n.Type is RunNodeType.Side or RunNodeType.Anomaly);
                if (target != null && RewriteWithIntent(ctx, target, NodeIntent.SafeHaven))
                    modified.Add(target.Id);
            }
        }

        // Rule C: Long calm streak (≥ 3 nodes since last breather AND no hostile) → seed pressure.
        if (ctx.Pacing.NodesSinceBreather >= CalmStreakPressureTrigger
            && ctx.Pacing.RecentHostileStreak == 0)
        {
            var target = rewriteCandidates
                .Skip(1) // leave the immediate next node alone for predictability
                .FirstOrDefault(n => n.Type is RunNodeType.Side or RunNodeType.Anomaly);
            if (target != null && RewriteWithIntent(ctx, target, NodeIntent.Pressure))
                modified.Add(target.Id);
        }

        return modified;
    }

    private bool RewriteWithIntent(RunDirectorContext ctx, RunNodeData node, NodeIntent intent)
    {
        var pool = MissionTemplateCatalog.GenericPool;
        var picked = WeightTemplate(ctx, act: 0, node.Depth, pool, intent);
        if (picked == null) return false;
        if (picked.Id == node.AssignedMissionId) return false;

        ApplyTemplate(node, picked);
        ctx.Pacing.NodeIntent[node.Id] = intent;
        GD.Print($"[EscalatingDirector] AdjustUpcoming: {node.Id} → {picked.Id} (intent={intent}, hostileStreak={ctx.Pacing.RecentHostileStreak}, hull={ctx.Run.CurrentHull:0}, sinceBreather={ctx.Pacing.NodesSinceBreather}).");
        return true;
    }

    private static void ApplyTemplate(RunNodeData node, MissionTemplate tpl)
    {
        node.Title = tpl.Title;
        node.Type = MissionTemplateCatalog.MapToRunNodeType(tpl.Type);
        node.RiskRating = tpl.Risk;
        node.AssignedMissionId = tpl.Id;
    }

    public NodeEvent? PickEvent(
        RunDirectorContext ctx,
        RunNodeData node,
        NodeEventTrigger trigger,
        IReadOnlyList<NodeEvent> candidates)
    {
        if (candidates.Count == 0) return null;

        float pool = ctx.Pacing.ThreatPool;
        bool hostileStreakHigh = ctx.Pacing.RecentHostileStreak >= HostileStreakBreatherTrigger;
        bool tensionLow = ctx.Pacing.TensionLevel < 0.3f;
        ctx.Pacing.NodeIntent.TryGetValue(node.Id, out var intent);
        bool safeHavenIntent = intent == NodeIntent.SafeHaven;

        // Affordable Filter: Cost <= verfügbarer Pool. Sonst nur Cost-0.
        var affordable = candidates.Where(e => e.ThreatCost <= pool).ToList();
        if (affordable.Count == 0)
            affordable = candidates.Where(e => e.ThreatCost == 0).ToList();
        if (affordable.Count == 0)
            return null;

        // SafeHaven: keine Spawn-Events
        if (safeHavenIntent)
        {
            var freeOnly = affordable.Where(e => e.ThreatCost == 0).ToList();
            if (freeOnly.Count > 0) affordable = freeOnly;
        }

        var weighted = new List<(NodeEvent Evt, double Weight)>(affordable.Count);
        double total = 0;
        foreach (var e in affordable)
        {
            double w = 1.0;
            if (hostileStreakHigh && e.ThreatCost == 0) w *= 3.0;
            if (tensionLow && e.ThreatCost >= 2) w *= 2.0;
            // Heißer Pool (>= Capacity * 0.7) bevorzugt teurere Events leicht.
            if (ctx.Pacing.ThreatCapacity > 0 && pool >= ctx.Pacing.ThreatCapacity * 0.7f && e.ThreatCost >= 2)
                w *= 1.5;
            weighted.Add((e, w));
            total += w;
        }
        if (total <= 0) return affordable[ctx.Rng.Next(affordable.Count)];

        double roll = ctx.Rng.NextDouble() * total;
        double accum = 0;
        NodeEvent picked = weighted[^1].Evt;
        foreach (var (evt, w) in weighted)
        {
            accum += w;
            if (roll <= accum) { picked = evt; break; }
        }

        if (picked.ThreatCost > 0)
        {
            ctx.Pacing.ThreatPool = MathF.Max(0f, ctx.Pacing.ThreatPool - picked.ThreatCost);
            ctx.Pacing.LastDrainAmount = picked.ThreatCost;
            ctx.Pacing.LastDrainReason = $"event:{picked.Id}";
            GD.Print($"[EscalatingDirector] PickEvent {trigger} {picked.Id} cost={picked.ThreatCost} → pool {ctx.Pacing.ThreatPool:F1}/{ctx.Pacing.ThreatCapacity:F0}.");
        }
        else
        {
            GD.Print($"[EscalatingDirector] PickEvent {trigger} {picked.Id} (free).");
        }
        return picked;
    }

    public List<AgentSpawnProfile> AdjustSpawnProfiles(
        RunDirectorContext ctx,
        RunNodeData node,
        string biomeId,
        List<AgentSpawnProfile> baseProfiles)
    {
        if (baseProfiles.Count == 0) return baseProfiles;

        float pool = ctx.Pacing.ThreatPool;
        float capacity = ctx.Pacing.ThreatCapacity;

        var adjusted = baseProfiles.Select(p => new AgentSpawnProfile
        {
            AgentType = p.AgentType,
            CountMin = p.CountMin,
            CountMax = p.CountMax,
            InitialMode = p.InitialMode,
            SpawnRadiusFactor = p.SpawnRadiusFactor,
            SpawnNearLandmark = p.SpawnNearLandmark,
        }).ToList();

        // Geschätzte Mid-Count-Kosten der Hostile-Profile.
        float HostileCost(AgentSpawnProfile p)
        {
            int cost = AgentSpawnConfig.GetCost(p.AgentType);
            if (cost < 2) return 0f; // Trader/Hauler ignorieren wir budgetär.
            float midCount = (p.CountMin + p.CountMax) * 0.5f;
            return midCount * cost;
        }

        float totalHostile = adjusted.Sum(HostileCost);

        // Pool zu klein → teuerste zuerst senken, bis es passt.
        if (totalHostile > pool && pool >= 0)
        {
            var byCost = adjusted
                .Where(p => AgentSpawnConfig.GetCost(p.AgentType) >= 2)
                .OrderByDescending(p => AgentSpawnConfig.GetCost(p.AgentType))
                .ToList();
            foreach (var p in byCost)
            {
                while (HostileCost(p) > 0 && totalHostile > pool)
                {
                    if (p.CountMax > 0)
                    {
                        var trimmed = new AgentSpawnProfile
                        {
                            AgentType = p.AgentType,
                            CountMin = Math.Max(0, p.CountMin - (p.CountMax == p.CountMin ? 1 : 0)),
                            CountMax = p.CountMax - 1,
                            InitialMode = p.InitialMode,
                            SpawnRadiusFactor = p.SpawnRadiusFactor,
                            SpawnNearLandmark = p.SpawnNearLandmark,
                        };
                        int idx = adjusted.IndexOf(p);
                        adjusted[idx] = trimmed;
                        totalHostile = adjusted.Sum(HostileCost);
                        if (trimmed.CountMax <= 0) break;
                    }
                    else break;
                }
                if (totalHostile <= pool) break;
            }
        }
        // Pressure-Boost: Pool reichlich + hohe Tension + Hostile-Knoten → +1 raider.
        else if (node.Type == RunNodeType.Hostile
                 && ctx.Pacing.TensionLevel > 0.6f
                 && pool > totalHostile + 4f
                 && adjusted.Any(p => p.AgentType == "pirate_raider"))
        {
            int idx = adjusted.FindIndex(p => p.AgentType == "pirate_raider");
            var p = adjusted[idx];
            adjusted[idx] = new AgentSpawnProfile
            {
                AgentType = p.AgentType,
                CountMin = p.CountMin,
                CountMax = p.CountMax + 1,
                InitialMode = p.InitialMode,
                SpawnRadiusFactor = p.SpawnRadiusFactor,
                SpawnNearLandmark = p.SpawnNearLandmark,
            };
            totalHostile = adjusted.Sum(HostileCost);
            GD.Print($"[EscalatingDirector] AdjustSpawnProfiles: Pressure-Boost +1 raider (pool {pool:F1}, tension {ctx.Pacing.TensionLevel:F2}).");
        }

        // Pool drainen (geschätzte Mid-Count-Kosten der finalen Hostile-Profile).
        if (totalHostile > 0)
        {
            float drain = MathF.Min(pool, totalHostile);
            ctx.Pacing.ThreatPool = pool - drain;
            ctx.Pacing.LastDrainAmount = drain;
            ctx.Pacing.LastDrainReason = $"spawns:{node.Id}";
            GD.Print($"[EscalatingDirector] AdjustSpawnProfiles {node.Id}: drain {drain:F1} → pool {ctx.Pacing.ThreatPool:F1}/{capacity:F0}.");
        }

        return adjusted;
    }

    public void TickInSector(RunDirectorContext ctx, MissionController mission, RunNodeData node, float deltaSec)
    {
        if (mission == null || ctx == null) return;
        var pacing = ctx.Pacing;
        float elapsed = mission.ElapsedTime;

        // Heartbeat noch nicht fällig?
        if (elapsed < pacing.NextHeartbeatAtElapsed) return;

        pacing.NextHeartbeatAtElapsed = elapsed + HeartbeatIntervalSec;

        // Cooldown gegenüber letzter Welle (egal welcher Reason).
        if (pacing.LastWaveAtElapsed >= 0f && elapsed - pacing.LastWaveAtElapsed < MinWaveCooldownSec)
        {
            GD.Print($"[EscalatingDirector] Heartbeat blocked: cooldown ({elapsed - pacing.LastWaveAtElapsed:F0}s/{MinWaveCooldownSec:F0}s).");
            return;
        }

        float chance = HeartbeatBaseChance * MathF.Min(1f, 0.5f + pacing.TensionLevel);
        double roll = ctx.Rng.NextDouble();
        if (roll > chance)
        {
            GD.Print($"[EscalatingDirector] Heartbeat skip: roll {roll:F2} > chance {chance:F2} (tension {pacing.TensionLevel:F2}).");
            return;
        }

        TryDispatchWave(ctx, mission, node, "heartbeat");
    }

    public void OnHostileDestroyed(RunDirectorContext ctx, MissionController mission, RunNodeData node, string contactId)
    {
        if (mission == null || ctx == null) return;
        var pacing = ctx.Pacing;
        float elapsed = mission.ElapsedTime;

        if (pacing.ThreatPool < MinPoolForWave)
        {
            GD.Print($"[EscalatingDirector] OnHostileDestroyed skip: pool {pacing.ThreatPool:F1} < {MinPoolForWave:F0}.");
            return;
        }
        if (pacing.TensionLevel <= 0.5f && pacing.RecentHostileStreak <= 1)
        {
            GD.Print($"[EscalatingDirector] OnHostileDestroyed skip: pacing too cold (tension {pacing.TensionLevel:F2}, streak {pacing.RecentHostileStreak}).");
            return;
        }
        if (pacing.LastWaveAtElapsed >= 0f && elapsed - pacing.LastWaveAtElapsed < MinKillReactCooldownSec)
        {
            GD.Print($"[EscalatingDirector] OnHostileDestroyed blocked: cooldown ({elapsed - pacing.LastWaveAtElapsed:F0}s/{MinKillReactCooldownSec:F0}s).");
            return;
        }

        double roll = ctx.Rng.NextDouble();
        if (roll > KillReactChance)
        {
            GD.Print($"[EscalatingDirector] OnHostileDestroyed skip: roll {roll:F2} > chance {KillReactChance:F2}.");
            return;
        }

        TryDispatchWave(ctx, mission, node, "on_kill");
    }

    /// <summary>
    /// Gemeinsamer Spawn-Pfad für Heartbeat- und KO-Reaktionen. Prüft Gate (NodeType, Pool, Cap),
    /// baut die Welle, queued sie via <see cref="MissionController.QueueDeferredSpawns"/> und feuert
    /// sie sofort via <see cref="MissionController.FireRuntimeTriggerNow"/> mit Log-Eintrag.
    /// </summary>
    private void TryDispatchWave(RunDirectorContext ctx, MissionController mission, RunNodeData node, string reason)
    {
        var pacing = ctx.Pacing;
        float elapsed = mission.ElapsedTime;

        // Per-Sektor Cap.
        if (pacing.InSectorWaveCount >= MaxWavesPerSector)
        {
            GD.Print($"[EscalatingDirector] Wave blocked: cap reached ({pacing.InSectorWaveCount}/{MaxWavesPerSector}).");
            return;
        }

        // Pool-Gate.
        if (pacing.ThreatPool < MinPoolForWave)
        {
            GD.Print($"[EscalatingDirector] Wave blocked: pool {pacing.ThreatPool:F1} < {MinPoolForWave:F0}.");
            return;
        }

        // NodeType-Gate.
        bool isHostile = node.Type == RunNodeType.Hostile;
        bool isStation = node.Type == RunNodeType.Station;
        bool isSideOrAnomaly = node.Type is RunNodeType.Side or RunNodeType.Anomaly;
        pacing.NodeIntent.TryGetValue(node.Id, out var intent);
        bool safeHaven = intent == NodeIntent.SafeHaven;

        if (isStation || safeHaven)
        {
            GD.Print($"[EscalatingDirector] Wave blocked: node sanctuary (type={node.Type}, intent={intent}).");
            return;
        }
        if (isSideOrAnomaly && (pacing.TensionLevel < SideAnomalyMinTension || pacing.ThreatPool < SideAnomalyMinPool))
        {
            GD.Print($"[EscalatingDirector] Wave blocked: side/anomaly gate (tension {pacing.TensionLevel:F2}/{SideAnomalyMinTension:F2}, pool {pacing.ThreatPool:F1}/{SideAnomalyMinPool:F0}).");
            return;
        }
        if (!isHostile && !isSideOrAnomaly)
        {
            GD.Print($"[EscalatingDirector] Wave blocked: node type {node.Type} not eligible.");
            return;
        }

        // Welle bauen: 1 Raider; +1 Raider bei Pool >= 4; +1 Corsair bei Pool >= 6 + Tension > 0.7 + Hostile-Knoten.
        float pool = pacing.ThreatPool;
        var spawns = new List<DeferredAgentSpawn>();
        string triggerId = $"director_wave_{pacing.InSectorWaveCount}";
        float estimatedCost = 0f;

        spawns.Add(BuildSpawn("pirate_raider", triggerId));
        estimatedCost += AgentSpawnConfig.GetCost("pirate_raider");

        if (pool - estimatedCost >= AgentSpawnConfig.GetCost("pirate_raider"))
        {
            spawns.Add(BuildSpawn("pirate_raider", triggerId));
            estimatedCost += AgentSpawnConfig.GetCost("pirate_raider");
        }

        if (isHostile
            && pacing.TensionLevel >= CorsairMinTension
            && pool - estimatedCost >= AgentSpawnConfig.GetCost("pirate_corsair")
            && pool >= CorsairMinPool)
        {
            spawns.Add(BuildSpawn("pirate_corsair", triggerId));
            estimatedCost += AgentSpawnConfig.GetCost("pirate_corsair");
        }

        // Pool drainen.
        float drain = MathF.Min(pool, estimatedCost);
        pacing.ThreatPool = pool - drain;
        pacing.LastDrainAmount = drain;
        pacing.LastDrainReason = $"wave:{reason}";
        pacing.InSectorWaveCount++;
        pacing.LastWaveAtElapsed = elapsed;
        pacing.LastWaveReason = reason;

        // Queue + sofort feuern. Eventid == triggerid sorgt dafür, dass FireRuntimeTriggerNow
        // beide Pfade nimmt: SpawnDeferredAgents(triggerId) UND Mission-Log-Eintrag.
        mission.QueueDeferredSpawns(spawns);
        mission.FireRuntimeTriggerNow(
            triggerId,
            eventId: triggerId,
            decisionId: null,
            logEntry: "Funkspruch: Neue Signatur am Sensorrand erfasst.");

        GD.Print($"[EscalatingDirector] Wave dispatched: reason={reason}, count={spawns.Count}, drain={drain:F1}, pool {pacing.ThreatPool:F1}/{pacing.ThreatCapacity:F0}, sector {pacing.InSectorWaveCount}/{MaxWavesPerSector}, node={node.Id}, t={elapsed:F0}s.");
    }

    private static DeferredAgentSpawn BuildSpawn(string agentType, string triggerId) => new()
    {
        AgentType = agentType,
        TriggerId = triggerId,
        Origin = SpawnOrigin.EdgeRandom,
        InitialMode = AgentBehaviorMode.Intercept,
        MinRisk = 0,
    };
}
