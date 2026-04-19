using System;
using System.Collections.Generic;
using System.Linq;
using SpacedOut.Agents;
using SpacedOut.Run;
using SpacedOut.State;

namespace SpacedOut.Mission;

/// <summary>Where in the Anomaly/Side-node lifecycle a <see cref="NodeEvent"/> fires.</summary>
public enum NodeEventTrigger
{
    /// <summary>Overlay dialog on the run map before the sector is built — can <c>SkipSector</c>.</summary>
    PreSector,
    /// <summary>Proximity/time trigger injected into the active mission via <see cref="MissionController"/>.</summary>
    InSector,
}

/// <summary>
/// Authored event definition drawn from the catalog when the player enters an
/// <see cref="RunNodeType.Anomaly"/> or <see cref="RunNodeType.Side"/> node.
/// Eligibility is filtered by <see cref="EligibleTypes"/>, biome, and risk before picking.
/// </summary>
public class NodeEvent
{
    public string Id { get; init; } = "";
    public string Title { get; init; } = "";
    public string Description { get; init; } = "";
    public NodeEventTrigger Trigger { get; init; }

    /// <summary>Mission types this event can fire on — empty = any (see <see cref="NodeEventCatalog.PickForNode"/>).</summary>
    public List<MissionType> EligibleTypes { get; init; } = new();

    /// <summary>Biome ids (e.g. <c>asteroid_field</c>, <c>wreck_zone</c>). Null/empty = any.</summary>
    public List<string>? EligibleBiomes { get; init; }

    public int MinRisk { get; init; } = 0;
    public int MaxRisk { get; init; } = 99;

    /// <summary>
    /// Director Threat-Pool Kosten dieses Events. 0 = reines Flavor-Event, kein Pool-Drain.
    /// Spawn-tragende Events tragen Kosten, die vom <see cref="SpacedOut.Run.IRunDirector.PickEvent"/>
    /// gegen <see cref="SpacedOut.Run.PacingState.ThreatPool"/> geprüft werden.
    /// </summary>
    public int ThreatCost { get; init; }

    /// <summary>For <see cref="NodeEventTrigger.InSector"/>: which mission trigger to use (default proximity to Landmark).</summary>
    public NodeEventInSectorTrigger InSectorTrigger { get; init; } = new();

    /// <summary>
    /// M7: when set, the event is only eligible when <see cref="SpacedOut.Meta.MetaProfile.UnlockedIds"/>
    /// contains this id (e.g. <c>pack_rare_anomalies</c>). Null/empty = always eligible.
    /// </summary>
    public string? RequiresUnlockId { get; init; }

    public List<DecisionOption> Options { get; init; } = new();
}

/// <summary>Describes how an in-sector <see cref="NodeEvent"/> is scheduled inside <see cref="MissionController"/>.</summary>
public class NodeEventInSectorTrigger
{
    /// <summary>Kind of synthetic trigger injected: proximity to a <see cref="TriggerRef"/>, or a mission time.</summary>
    public NodeEventInSectorKind Kind { get; init; } = NodeEventInSectorKind.ProximityToLandmark;
    public TriggerRef ProximityRef { get; init; } = TriggerRef.Landmark;
    public float ProximityRadius { get; init; } = 180f;
    public float TimeSeconds { get; init; } = 90f;
}

public enum NodeEventInSectorKind { ProximityToLandmark, ProximityToMapCenter, AtTime }

/// <summary>
/// Central catalog of <see cref="NodeEvent"/>s — deterministic picker keyed on
/// (<see cref="RunStateData.CampaignSeed"/> XOR node id). Events are one-shot per run
/// via <see cref="RunStateData.FiredEventIds"/>.
/// </summary>
public static class NodeEventCatalog
{
    /// <summary>Chance (0..1) that a node with at least one eligible event actually fires one. Deterministic per node.</summary>
    public const float FiringProbability = 0.85f;

    private static readonly List<NodeEvent> _all = BuildCatalog();

    public static IReadOnlyList<NodeEvent> All => _all;

    public static NodeEvent? GetOrNull(string id) =>
        _all.FirstOrDefault(e => e.Id == id);

    /// <summary>
    /// Event types eligible for a node type. Director-Phase 2 öffnet zusätzlich Hostile/Station, sodass
    /// Patrouillen-Warnungen oder Station-Tipps vor dem Sprung greifen können.
    /// </summary>
    public static bool NodeCarriesEvents(RunNodeType type) =>
        type is RunNodeType.Anomaly or RunNodeType.Side or RunNodeType.Hostile or RunNodeType.Station;

    /// <summary>
    /// Deterministic event pick for a node. Returns null if:
    /// <list type="bullet">
    ///   <item>the node type does not carry events,</item>
    ///   <item>no catalog entry matches (type/biome/risk),</item>
    ///   <item>all matching entries have already fired this run,</item>
    ///   <item>the per-node probability roll does not hit.</item>
    /// </list>
    /// Seed: <paramref name="campaignSeed"/> XOR <c>node.Id.GetHashCode()</c>.
    /// </summary>
    public static NodeEvent? PickForNode(RunNodeData node, string biome, int campaignSeed,
        HashSet<string> alreadyFired, IReadOnlyCollection<string>? unlockedIds = null)
    {
        if (!NodeCarriesEvents(node.Type))
            return null;

        var candidates = FilterCandidates(node, biome, alreadyFired, unlockedIds);
        if (candidates.Count == 0) return null;

        int seed = unchecked(campaignSeed ^ node.Id.GetHashCode());
        var rng = new Random(seed);
        if (rng.NextDouble() > FiringProbability) return null;

        return candidates[rng.Next(candidates.Count)];
    }

    /// <summary>
    /// Public Helper für den Director: liefert alle eligible Kandidaten für einen Knoten ohne
    /// Firing-Probability-Roll und ohne RNG-Pick. Wird vom <see cref="SpacedOut.Run.IRunDirector.PickEvent"/>
    /// genutzt, um danach selbst (mit Pool-Budget + Tags) zu wählen.
    /// </summary>
    public static IReadOnlyList<NodeEvent> GetCandidates(RunNodeData node, string biome,
        HashSet<string> alreadyFired, IReadOnlyCollection<string>? unlockedIds = null,
        NodeEventTrigger? triggerFilter = null)
    {
        if (!NodeCarriesEvents(node.Type))
            return Array.Empty<NodeEvent>();

        var list = FilterCandidates(node, biome, alreadyFired, unlockedIds);
        if (triggerFilter.HasValue)
            list = list.Where(e => e.Trigger == triggerFilter.Value).ToList();
        return list;
    }

    /// <summary>
    /// Determines whether the per-node firing roll passes (independent of <see cref="PickForNode"/>'s
    /// RNG selection). Director uses this to honor authored firing rates while still picking itself.
    /// </summary>
    public static bool RollFiring(RunNodeData node, int campaignSeed)
    {
        int seed = unchecked(campaignSeed ^ node.Id.GetHashCode());
        var rng = new Random(seed);
        return rng.NextDouble() <= FiringProbability;
    }

    private static List<NodeEvent> FilterCandidates(RunNodeData node, string biome,
        HashSet<string> alreadyFired, IReadOnlyCollection<string>? unlockedIds)
    {
        // Map RunNodeType → MissionType for eligibility filtering.
        MissionType? type = node.Type switch
        {
            RunNodeType.Anomaly => MissionType.Anomaly,
            RunNodeType.Side => MissionType.Salvage,
            RunNodeType.Hostile => MissionType.Hostile,
            RunNodeType.Station => MissionType.Station,
            _ => null,
        };

        var result = new List<NodeEvent>();
        foreach (var evt in _all)
        {
            if (alreadyFired.Contains(evt.Id)) continue;
            if (node.RiskRating < evt.MinRisk || node.RiskRating > evt.MaxRisk) continue;
            if (type.HasValue && evt.EligibleTypes.Count > 0 && !evt.EligibleTypes.Contains(type.Value))
                continue;
            if (evt.EligibleBiomes is { Count: > 0 } && !evt.EligibleBiomes.Contains(biome))
                continue;
            // M7: hide pack-gated events unless the player owns the unlock.
            if (!string.IsNullOrEmpty(evt.RequiresUnlockId) &&
                (unlockedIds == null || !unlockedIds.Contains(evt.RequiresUnlockId)))
                continue;
            result.Add(evt);
        }
        return result;
    }

    // ── Content v1: 10 authored events ──────────────────────────────────────

    private static List<NodeEvent> BuildCatalog()
    {
        var list = new List<NodeEvent>();

        // ── PRE-SECTOR (5) ──────────────────────────────────────────────────
        list.Add(new NodeEvent
        {
            Id = "pre_distress_beacon",
            Title = "Notsignal empfangen",
            Description = "Ein schwaches Notsignal dringt durch das Trümmerfeld. Eine ältere Fracht-Signatur — kein Begleitschutz erkennbar.",
            Trigger = NodeEventTrigger.PreSector,
            EligibleTypes = { MissionType.Salvage, MissionType.Distress, MissionType.Anomaly, MissionType.Hostile },
            MinRisk = 1,
            Options =
            {
                new DecisionOption
                {
                    Id = "approach",
                    Label = "Signal ansteuern",
                    Description = "Kurs halten, Sektor anfliegen und die Quelle lokalisieren.",
                    FlavorHint = "Sicherer Weg in den Sektor — Lage vor Ort erkunden.",
                    ResolutionNarrative =
                        "Die Brücke hält den Kurs. Das Notsignal bleibt auf allen Bändern — wir gehen in den Sektor und klären die Lage vor Ort.",
                    Effects = new DecisionEffects
                    {
                        LogSummary = "Captain: Wir fliegen das Notsignal an.",
                    },
                },
                new DecisionOption
                {
                    Id = "ignore",
                    Label = "Ignorieren und weiter",
                    Description = "Sektor überspringen. Treibstoff bleibt, aber der Funkspruch verhallt.",
                    FlavorHint = "Kein Risiko — aber auch keine Bergung.",
                    ResolutionNarrative =
                        "Der Kapitän lässt das Signal verhallen. Ohne Einsatz fliegen wir weiter — der Sprung spart Treibstoff, doch die Stimme im Äther verstummt.",
                    Effects = new DecisionEffects
                    {
                        SkipSector = true,
                        ResourceDeltas = { [RunResourceIds.Fuel] = 1 },
                        LogSummary = "Captain: Wir lassen das Notsignal hinter uns.",
                    },
                },
                new DecisionOption
                {
                    Id = "quickscan",
                    Label = "Ferner Scan vor Eintritt",
                    Description = "Kurze Sondierung, dann entscheiden. Kostet Forschungsdaten.",
                    FlavorHint = "Erkenntnis gegen einen Sensor-Datensatz.",
                    ResolutionNarrative =
                        "Tactical feuert einen schnellen Ferner-Scan ab. Die Datenbank frisst einen Forschungsblock — dafür wissen wir vor dem Eintritt mehr, als das Nackte Auge je könnte.",
                    Effects = new DecisionEffects
                    {
                        ResourceDeltas = { [RunResourceIds.ScienceData] = -1 },
                        FlagsToSet = { "event.distress_prescan" },
                        LogSummary = "Captain: Ferner Scan abgeschlossen — wir fliegen an.",
                    },
                },
            },
        });

        list.Add(new NodeEvent
        {
            Id = "pre_gas_cloud",
            Title = "Ionisierte Gaswolke",
            Description = "Auf dem Anflug kreuzt eine ionisierte Wolke den Kurs. Umflug kostet Treibstoff — Durchflug belastet die Systeme.",
            Trigger = NodeEventTrigger.PreSector,
            EligibleTypes = { MissionType.Anomaly, MissionType.Hazard, MissionType.Salvage },
            Options =
            {
                new DecisionOption
                {
                    Id = "fly_through",
                    Label = "Durchflug",
                    Description = "Kurs halten, Sensor- und Schildwärme steigt.",
                    FlavorHint = "Spürbare Systembelastung, aber kein Umweg.",
                    ResolutionNarrative =
                        "Wir durchqueren die Wolke auf direktem Kurs. Ionen kitzeln Schilde und Sensoren — die Hitzeanzeigen springen, aber der Weg ist kurz.",
                    Effects = new DecisionEffects
                    {
                        SystemEffects =
                        {
                            new SystemEffect { System = SystemId.Sensors, HeatDelta = 25f },
                            new SystemEffect { System = SystemId.Shields, HeatDelta = 20f },
                        },
                        LogSummary = "Captain: Durch die Wolke — Wärme auf Sensoren und Schilden.",
                    },
                },
                new DecisionOption
                {
                    Id = "detour",
                    Label = "Umfliegen",
                    Description = "Längerer Anflug, Mehrverbrauch.",
                    FlavorHint = "Sicher, aber kostet Treibstoff.",
                    ResolutionNarrative =
                        "Der Plot wird weich gezogen. Die Wolke bleibt links außen — mehr Treibstoff auf der Uhr, dafür bleiben die Systeme kühl.",
                    Effects = new DecisionEffects
                    {
                        ResourceDeltas = { [RunResourceIds.Fuel] = -1 },
                        LogSummary = "Captain: Wir umfliegen die Wolke.",
                    },
                },
                new DecisionOption
                {
                    Id = "probe",
                    Label = "Sondieren",
                    Description = "Forschungsdaten sammeln, dann direkt weiter.",
                    FlavorHint = "Wissen zum Preis eines Datensatzes.",
                    ResolutionNarrative =
                        "Ein Sondenpaket schnellt hinaus und sammelt, was die Wolke preisgibt. Die Datenbank wächst — und wir fliegen weiter, ohne das Risiko einer Vollquerung.",
                    Effects = new DecisionEffects
                    {
                        ResourceDeltas = { [RunResourceIds.ScienceData] = 1 },
                        FlagsToSet = { "event.gas_cloud_probed" },
                        LogSummary = "Captain: Wolken-Daten erfasst.",
                    },
                },
            },
        });

        list.Add(new NodeEvent
        {
            Id = "pre_derelict_tip",
            Title = "Gerücht vom Wrack",
            Description = "Ein Handelsposten hat einen Hinweis auf verlassenes Gerät im nächsten Sektor verkauft.",
            Trigger = NodeEventTrigger.PreSector,
            EligibleTypes = { MissionType.Salvage, MissionType.Distress, MissionType.Station },
            Options =
            {
                new DecisionOption
                {
                    Id = "buy_tip",
                    Label = "Tipp kaufen",
                    Description = "Credits investieren — Ersatzteile wahrscheinlicher vor Ort.",
                    FlavorHint = "Zahlt sich aus, wenn wir den Sektor durchstehen.",
                    ResolutionNarrative =
                        "Credits wechseln den Besitzer. Der Händler flüstert Koordinaten und Wink — ob das Wrack wirklich wartet, sehen wir erst drinnen.",
                    Effects = new DecisionEffects
                    {
                        ResourceDeltas = { [RunResourceIds.Credits] = -10 },
                        FlagsToSet = { "event.derelict_tip_bought" },
                        LogSummary = "Captain: Wrack-Tipp gekauft.",
                    },
                },
                new DecisionOption
                {
                    Id = "skip_tip",
                    Label = "Nicht zahlen",
                    Description = "Sektor regulär anfliegen, keine Zusatzinfo.",
                    FlavorHint = "Kein Risiko, keine Extra-Chance.",
                    ResolutionNarrative =
                        "Kein Tipp, kein Vorspiel. Wir fliegen den Sektor blind an — ohne Extra-Kosten und ohne die Gerüchte des Basars im Ohr.",
                    Effects = new DecisionEffects
                    {
                        LogSummary = "Captain: Kein Tipp-Kauf.",
                    },
                },
            },
        });

        list.Add(new NodeEvent
        {
            Id = "pre_patrol_warning",
            Title = "Patrouillenmeldung",
            Description = "Ein befreundeter Schlepper meldet eine Patrouille am Sektorrand. Durchqueren riskant, Umgehen teuer.",
            Trigger = NodeEventTrigger.PreSector,
            EligibleTypes = { MissionType.Hostile, MissionType.Salvage, MissionType.Anomaly },
            MinRisk = 2,
            ThreatCost = 2,
            Options =
            {
                new DecisionOption
                {
                    Id = "press_on",
                    Label = "Durchstoßen",
                    Description = "Sektor wie geplant anfliegen — Gegner vor Ort möglich.",
                    FlavorHint = "Höhere Feindpräsenz im Sektor.",
                    ResolutionNarrative =
                        "Wir drücken durch. Sensoren zeichnen später mehr Bewegung am Rand — die Patrouille hat uns wahrgenommen, und etwas Dreht sich auf Kollisionskurs.",
                    Effects = new DecisionEffects
                    {
                        FlagsToSet = { "event.patrol_alerted" },
                        SpawnAgents =
                        {
                            new DeferredAgentSpawn
                            {
                                AgentType = "pirate_raider",
                                TriggerId = "event_patrol_alerted",
                                Origin = SpawnOrigin.EdgeRandom,
                                InitialMode = AgentBehaviorMode.Intercept,
                            },
                        },
                        LogSummary = "Captain: Wir fliegen durch — Patrouille erwartet.",
                    },
                },
                new DecisionOption
                {
                    Id = "detour_fuel",
                    Label = "Umweg fliegen",
                    Description = "Sicheren Vektor nehmen — Sektor ist weniger feindlich, aber Mehrverbrauch.",
                    FlavorHint = "Weniger Gefechte, mehr Tank.",
                    ResolutionNarrative =
                        "Kurs wird verworfen. Ein längerer Bogen kostet Treibstoff, aber die heißen Vektoren der Patrouille verfehlen uns — hoffentlich.",
                    Effects = new DecisionEffects
                    {
                        ResourceDeltas = { [RunResourceIds.Fuel] = -2 },
                        LogSummary = "Captain: Umweg bestätigt.",
                    },
                },
                new DecisionOption
                {
                    Id = "skip",
                    Label = "Sektor auslassen",
                    Description = "Auf Bergung verzichten und weiterziehen.",
                    FlavorHint = "Schnell weg, keine Ausbeute.",
                    ResolutionNarrative =
                        "Der Sektor fällt aus dem Plan. Wir springen weiter, ohne die Konfrontation — kein Kampf, keine Beute, nur der leere Platz auf der Karte.",
                    Effects = new DecisionEffects
                    {
                        SkipSector = true,
                        LogSummary = "Captain: Patrouille ausgewichen, Sektor übersprungen.",
                    },
                },
            },
        });

        list.Add(new NodeEvent
        {
            Id = "pre_drifting_pod",
            Title = "Treibende Kapsel",
            Description = "Kurz vor Eintritt tauchen schwache Lebenszeichen auf: eine kleine Rettungskapsel treibt in der Nähe.",
            Trigger = NodeEventTrigger.PreSector,
            EligibleTypes = { MissionType.Distress, MissionType.Salvage, MissionType.Anomaly },
            Options =
            {
                new DecisionOption
                {
                    Id = "rescue",
                    Label = "Kapsel bergen",
                    Description = "Bergen, dann in den Sektor eintreten.",
                    FlavorHint = "Kostet Ersatzteile — bringt Moral und Credits.",
                    ResolutionNarrative =
                        "Ein Fangstrahl, ein kurzer Ruck — die Kapsel klirrt im Frachtschacht. Ein Atemzug weniger in den Ersatzteilen, dafür Leben an Bord und ein paar Credits auf dem Konto.",
                    Effects = new DecisionEffects
                    {
                        ResourceDeltas =
                        {
                            [RunResourceIds.SpareParts] = -1,
                            [RunResourceIds.Credits] = 8,
                        },
                        FlagsToSet = { "event.pod_rescued" },
                        LogSummary = "Captain: Kapsel geborgen, Insasse an Bord.",
                    },
                },
                new DecisionOption
                {
                    Id = "ignore",
                    Label = "Weiterfliegen",
                    Description = "Sektor betreten, Kapsel ihrem Schicksal überlassen.",
                    FlavorHint = "Keine Zeit verlieren — keine Bergung.",
                    ResolutionNarrative =
                        "Die Kapsel wird ein blinkender Punkt im Rückspiegel. Der Sektor wartet — und die Rettungslichtsignatur verblasst im Rauschen.",
                    Effects = new DecisionEffects
                    {
                        LogSummary = "Captain: Wir lassen die Kapsel zurück.",
                    },
                },
            },
        });

        // ── IN-SECTOR (5) ───────────────────────────────────────────────────
        list.Add(new NodeEvent
        {
            Id = "in_mineral_find",
            Title = "Reiche Ader",
            Description = "Sensoren melden eine reiche Mineralader nahe des Hauptziels.",
            Trigger = NodeEventTrigger.InSector,
            EligibleTypes = { MissionType.Salvage, MissionType.Anomaly },
            InSectorTrigger = new NodeEventInSectorTrigger
            {
                Kind = NodeEventInSectorKind.ProximityToLandmark,
                ProximityRef = TriggerRef.Landmark,
                ProximityRadius = 220f,
            },
            Options =
            {
                new DecisionOption
                {
                    Id = "mine",
                    Label = "Anflug auf Ader",
                    Description = "Bohrkurs legen — Mineral-Signatur als Ziel markieren.",
                    FlavorHint = "Neuer Kontakt auf der Karte.",
                    ResolutionNarrative =
                        "Sensoren markieren die Ader als eigenen Kontakt — ein Bohrkurs liegt an. Wir müssen dranbleiben: scannen, anbohren, extrahieren wie immer.",
                    Effects = new DecisionEffects
                    {
                        SpawnPois =
                        {
                            new DeferredPoiSpawn
                            {
                                AssetId = "poi_rich_vein",
                                AnchorRef = TriggerRef.Landmark,
                                DistanceFromAnchor = 110f,
                                Discovery = DiscoveryState.Detected,
                            },
                        },
                        LogSummary = "Engineer: Bohrkurs gelegt — Mineral-Signatur als Ziel markiert.",
                    },
                },
                new DecisionOption
                {
                    Id = "skip",
                    Label = "Nicht anhalten",
                    Description = "Hauptziel hat Priorität.",
                    FlavorHint = "Planmäßig weiter.",
                    ResolutionNarrative =
                        "Kein Halt. Die Ader bleibt eine Fußnote auf dem Plot — das Hauptziel zieht uns weiter.",
                    Effects = new DecisionEffects
                    {
                        LogSummary = "Captain: Ader ignoriert.",
                    },
                },
            },
        });

        list.Add(new NodeEvent
        {
            Id = "in_ghost_signal",
            Title = "Geistersignal",
            Description = "Ein sich wiederholendes Signal blinkt auf dem Taktik-Display — verzerrte Koordinaten.",
            Trigger = NodeEventTrigger.InSector,
            EligibleTypes = { MissionType.Anomaly },
            InSectorTrigger = new NodeEventInSectorTrigger
            {
                Kind = NodeEventInSectorKind.AtTime,
                TimeSeconds = 80f,
            },
            Options =
            {
                new DecisionOption
                {
                    Id = "decode",
                    Label = "Dekodieren",
                    Description = "Sensor-Last, aber wertvolle Daten.",
                    FlavorHint = "Wärme gegen Wissen.",
                    ResolutionNarrative =
                        "Die Muster lösen sich auf — ein Rauschpaket wird zu Koordinaten und Hypothesen. Die Sensorbank glüht, aber die Datenbank füllt sich.",
                    Effects = new DecisionEffects
                    {
                        ResourceDeltas = { [RunResourceIds.ScienceData] = 2 },
                        SystemEffects =
                        {
                            new SystemEffect { System = SystemId.Sensors, HeatDelta = 30f },
                        },
                        LogSummary = "Tactical: Signal dekodiert.",
                    },
                },
                new DecisionOption
                {
                    Id = "jam",
                    Label = "Überstrahlen",
                    Description = "Signal stören, keine Daten, aber keine Spur.",
                    FlavorHint = "Sicher, leer.",
                    ResolutionNarrative =
                        "Ein Breitband-Störschwall frisst das Geistersignal. Keine Antwort aus dem Rauschen — und keine neue Spur, der wir folgen könnten.",
                    Effects = new DecisionEffects
                    {
                        FlagsToSet = { "event.ghost_jammed" },
                        LogSummary = "Tactical: Signal überstrahlt.",
                    },
                },
                new DecisionOption
                {
                    Id = "ignore",
                    Label = "Ignorieren",
                    Description = "Weiter mit Hauptziel.",
                    FlavorHint = "Planmäßig.",
                    ResolutionNarrative =
                        "Das Display blinkt, dann nicht. Wir lassen das Echo sterben und halten den Kurs — Routine siegt über Neugier.",
                    Effects = new DecisionEffects
                    {
                        LogSummary = "Captain: Signal ignoriert.",
                    },
                },
            },
        });

        list.Add(new NodeEvent
        {
            Id = "in_ambush",
            Title = "Hinterhalt",
            Description = "Zwei Kontakte lösen sich aus dem Schatten eines Asteroiden.",
            Trigger = NodeEventTrigger.InSector,
            EligibleTypes = { MissionType.Salvage, MissionType.Hostile },
            MinRisk = 2,
            ThreatCost = 4,
            InSectorTrigger = new NodeEventInSectorTrigger
            {
                Kind = NodeEventInSectorKind.ProximityToLandmark,
                ProximityRef = TriggerRef.Landmark,
                ProximityRadius = 160f,
            },
            Options =
            {
                new DecisionOption
                {
                    Id = "fight",
                    Label = "Stellen",
                    Description = "Kampfbereit machen — Angreifer kommen.",
                    FlavorHint = "Gefecht beginnt sofort.",
                    ResolutionNarrative =
                        "Zwei Jäger lösen sich aus dem Schatten — Treibstoff voll, Waffen scharf. Bei hohem Risiko schiebt sich ein schwerer Korsair nach; die Sensoren schreien Kontakt.",
                    Effects = new DecisionEffects
                    {
                        SpawnAgents =
                        {
                            new DeferredAgentSpawn
                            {
                                AgentType = "pirate_raider",
                                TriggerId = "event_in_ambush",
                                Origin = SpawnOrigin.NearLandmark,
                                AnchorRef = TriggerRef.Landmark,
                                InitialMode = AgentBehaviorMode.Intercept,
                            },
                            new DeferredAgentSpawn
                            {
                                AgentType = "pirate_raider",
                                TriggerId = "event_in_ambush",
                                Origin = SpawnOrigin.NearLandmark,
                                AnchorRef = TriggerRef.Landmark,
                                InitialMode = AgentBehaviorMode.Intercept,
                            },
                            new DeferredAgentSpawn
                            {
                                AgentType = "pirate_corsair",
                                TriggerId = "event_in_ambush",
                                Origin = SpawnOrigin.NearLandmark,
                                AnchorRef = TriggerRef.Landmark,
                                InitialMode = AgentBehaviorMode.Intercept,
                                MinRisk = 3,
                            },
                        },
                        FlagsToSet = { "event.ambush_triggered" },
                        LogSummary = "Captain: Gefecht eingeleitet.",
                    },
                },
                new DecisionOption
                {
                    Id = "retreat",
                    Label = "Rückzug",
                    Description = "Hülle kann etwas Schaden nehmen beim Abdrehen.",
                    FlavorHint = "Schneller Rückzug, etwas Blech.",
                    ResolutionNarrative =
                        "Volle Kraft zurück — Treibwerke heulen, etwas Blech knirscht beim engen Wendemanöver. Wir kaufen Abstand mit Hüllenlack und Nerven.",
                    Effects = new DecisionEffects
                    {
                        HullDelta = -8f,
                        LogSummary = "Captain: Rückzug befohlen.",
                    },
                },
            },
        });

        list.Add(new NodeEvent
        {
            Id = "in_reactor_spike",
            Title = "Reaktor-Spike",
            Description = "Der Hauptreaktor liefert einen unerwarteten Überschuss — kurz, gefährlich, nutzbar.",
            Trigger = NodeEventTrigger.InSector,
            EligibleTypes = { MissionType.Salvage, MissionType.Anomaly, MissionType.Hostile },
            InSectorTrigger = new NodeEventInSectorTrigger
            {
                Kind = NodeEventInSectorKind.AtTime,
                TimeSeconds = 120f,
            },
            Options =
            {
                new DecisionOption
                {
                    Id = "dump",
                    Label = "Entlasten",
                    Description = "Wärme abführen, sicher weiter.",
                    FlavorHint = "Kühle Systeme — nichts Besonderes.",
                    ResolutionNarrative =
                        "Überschuss wird in Kühlkreisläufe und Felder gejagt. Die Anzeigen sinken — langweilig, sicher, überlebbar.",
                    Effects = new DecisionEffects
                    {
                        SystemEffects =
                        {
                            new SystemEffect { System = SystemId.Drive, HeatDelta = -15f },
                            new SystemEffect { System = SystemId.Shields, HeatDelta = -15f },
                        },
                        LogSummary = "Engineer: Reaktor entlastet.",
                    },
                },
                new DecisionOption
                {
                    Id = "channel",
                    Label = "Umleiten",
                    Description = "Daten gewinnen, aber Waffensysteme heiß.",
                    FlavorHint = "Heiße Waffen, frische Daten.",
                    ResolutionNarrative =
                        "Der Spike wird in die Forschungsbänder geschlagen. Daten fluten ein — und die Waffenleitung wird kurz zur Heizung.",
                    Effects = new DecisionEffects
                    {
                        ResourceDeltas = { [RunResourceIds.ScienceData] = 2 },
                        SystemEffects =
                        {
                            new SystemEffect { System = SystemId.Weapons, HeatDelta = 35f },
                        },
                        LogSummary = "Engineer: Spike in Forschungsdaten umgeleitet.",
                    },
                },
            },
        });

        list.Add(new NodeEvent
        {
            Id = "in_drifting_cache",
            Title = "Treibender Cache",
            Description = "Ein verschlossener Container treibt durch den Sektor. Könnte wertvoll sein — oder fallengestellt.",
            Trigger = NodeEventTrigger.InSector,
            EligibleTypes = { MissionType.Salvage, MissionType.Anomaly, MissionType.Distress },
            InSectorTrigger = new NodeEventInSectorTrigger
            {
                Kind = NodeEventInSectorKind.ProximityToMapCenter,
                ProximityRef = TriggerRef.MapCenter,
                ProximityRadius = 200f,
            },
            Options =
            {
                new DecisionOption
                {
                    Id = "crack",
                    Label = "Anflug bestätigen",
                    Description = "Container als Kontakt markieren — Bergung vor Ort.",
                    FlavorHint = "Beutechance gegen etwas Lärm.",
                    ResolutionNarrative =
                        "Sensoren markieren den treibenden Container als Kontakt. Bergung läuft wie immer: scannen, andocken, öffnen — was drinsteckt, sehen wir vor Ort.",
                    Effects = new DecisionEffects
                    {
                        SpawnPois =
                        {
                            new DeferredPoiSpawn
                            {
                                AssetId = "poi_drifting_pod",
                                AnchorRef = TriggerRef.MapCenter,
                                DistanceFromAnchor = 90f,
                                Discovery = DiscoveryState.Detected,
                                RadarShowDetectedInFullRange = true,
                                PersistDetectedBeyondSensorRange = true,
                            },
                        },
                        LogSummary = "Engineer: Container als Bergungsziel markiert.",
                    },
                },
                new DecisionOption
                {
                    Id = "scan_only",
                    Label = "Nur scannen",
                    Description = "Keine Bergung, nur Informationsgewinn.",
                    FlavorHint = "Saubere Daten, keine Bergung.",
                    ResolutionNarrative =
                        "Kein Berühren — nur passive Aufnahmen. Das Profil des Containers wird gespeichert; die Fracht bleibt drin, der Fingerabdruck in der Datenbank.",
                    Effects = new DecisionEffects
                    {
                        ResourceDeltas = { [RunResourceIds.ScienceData] = 1 },
                        LogSummary = "Tactical: Cache gescannt.",
                    },
                },
                new DecisionOption
                {
                    Id = "leave",
                    Label = "Unberührt lassen",
                    Description = "Weiterziehen.",
                    FlavorHint = "Kein Risiko.",
                    ResolutionNarrative =
                        "Der Container driftet vorbei wie ein leeres Versprechen. Kein Risiko, keine Beute — nur ein kurzer Schatten auf dem Plot.",
                    Effects = new DecisionEffects
                    {
                        LogSummary = "Captain: Cache ignoriert.",
                    },
                },
            },
        });

        // ── PACK: pack_rare_anomalies (3 Events) ────────────────────────────
        list.Add(new NodeEvent
        {
            Id = "pack_anom_singularity_echo",
            Title = "Singularitäts-Echo",
            Description = "Ein winziges Gravitationsecho zeichnet sich auf den Sensoren ab. Die Datenbank hat noch nie etwas Vergleichbares gespeichert.",
            Trigger = NodeEventTrigger.PreSector,
            EligibleTypes = { MissionType.Anomaly },
            RequiresUnlockId = "pack_rare_anomalies",
            Options =
            {
                new DecisionOption
                {
                    Id = "study",
                    Label = "Lange Messung",
                    Description = "Datensätze sammeln, ein Spike ist zu erwarten.",
                    FlavorHint = "Viel Wissen — heiße Sensoren.",
                    ResolutionNarrative =
                        "Wir hängen länger im Echo. Die Bänke fangen ein Datenpaket, das so noch nie jemand gesehen hat — bezahlt mit kochenden Sensoren.",
                    Effects = new DecisionEffects
                    {
                        ResourceDeltas = { [RunResourceIds.ScienceData] = 3 },
                        SystemEffects =
                        {
                            new SystemEffect { System = SystemId.Sensors, HeatDelta = 35f },
                        },
                        FlagsToSet = { "event.singularity_studied" },
                        LogSummary = "Tactical: Singularitäts-Echo eingelesen.",
                    },
                },
                new DecisionOption
                {
                    Id = "skip",
                    Label = "Nichts riskieren",
                    Description = "Sektor regulär anfliegen.",
                    FlavorHint = "Keine Daten, keine Hitze.",
                    ResolutionNarrative =
                        "Das Echo verstummt, sobald wir den Vektor verlassen. Eine Spur bleibt im Plot — nicht in der Datenbank.",
                    Effects = new DecisionEffects
                    {
                        LogSummary = "Captain: Echo unbeobachtet.",
                    },
                },
            },
        });

        list.Add(new NodeEvent
        {
            Id = "pack_anom_chrono_drift",
            Title = "Chrono-Drift",
            Description = "Die Borduhren laufen plötzlich auseinander. Etwas im nächsten Sektor verbiegt die Zeit.",
            Trigger = NodeEventTrigger.InSector,
            EligibleTypes = { MissionType.Anomaly },
            RequiresUnlockId = "pack_rare_anomalies",
            InSectorTrigger = new NodeEventInSectorTrigger
            {
                Kind = NodeEventInSectorKind.AtTime,
                TimeSeconds = 60f,
            },
            Options =
            {
                new DecisionOption
                {
                    Id = "ride",
                    Label = "Welle reiten",
                    Description = "Treibstoff sparen — Antrieb läuft auf Anomaliefluss.",
                    FlavorHint = "Geschenkter Treibstoff, kalte Schilde brennen.",
                    ResolutionNarrative =
                        "Der Antrieb saugt sich an die Drift, der Tank zeigt unwahrscheinlich satte Werte — und die Schildgeneratoren beklagen sich lautstark.",
                    Effects = new DecisionEffects
                    {
                        ResourceDeltas = { [RunResourceIds.Fuel] = 2 },
                        SystemEffects =
                        {
                            new SystemEffect { System = SystemId.Shields, HeatDelta = 30f },
                        },
                        LogSummary = "Engineer: Chrono-Drift als Antriebsfluss genutzt.",
                    },
                },
                new DecisionOption
                {
                    Id = "stabilize",
                    Label = "Stabilisieren",
                    Description = "Schilde richten — Daten gewinnen.",
                    FlavorHint = "Daten ohne Treibstoff.",
                    ResolutionNarrative =
                        "Die Schilde gleichen die Welle aus. Die Sensoren protokollieren, was eigentlich nicht protokolliert werden sollte.",
                    Effects = new DecisionEffects
                    {
                        ResourceDeltas = { [RunResourceIds.ScienceData] = 2 },
                        LogSummary = "Tactical: Drift stabilisiert, Daten gespeichert.",
                    },
                },
                new DecisionOption
                {
                    Id = "ignore",
                    Label = "Ignorieren",
                    Description = "Kurs halten.",
                    FlavorHint = "Keine Verzerrung mehr — und nichts dazu.",
                    ResolutionNarrative =
                        "Wir drücken durch. Die Anzeigen fangen sich, der Sektor scrollt vorbei wie immer — oder fast wie immer.",
                    Effects = new DecisionEffects { LogSummary = "Captain: Drift ignoriert." },
                },
            },
        });

        list.Add(new NodeEvent
        {
            Id = "pack_anom_dark_relay",
            Title = "Dunkles Relais",
            Description = "Sensorgeister kreisen um einen alten Relais-Kern, der eigentlich nicht mehr funktionieren sollte.",
            Trigger = NodeEventTrigger.InSector,
            EligibleTypes = { MissionType.Anomaly, MissionType.Salvage },
            RequiresUnlockId = "pack_rare_anomalies",
            InSectorTrigger = new NodeEventInSectorTrigger
            {
                Kind = NodeEventInSectorKind.ProximityToLandmark,
                ProximityRef = TriggerRef.Landmark,
                ProximityRadius = 200f,
            },
            Options =
            {
                new DecisionOption
                {
                    Id = "tap",
                    Label = "Anzapfen",
                    Description = "Daten aus dem Relais ziehen — riskant.",
                    FlavorHint = "Wertvoll und unangenehm.",
                    ResolutionNarrative =
                        "Wir koppeln uns an den Kern. Das Relais antwortet mit einer Datenflut — und einem hässlichen Rückstoß auf die Schilde.",
                    Effects = new DecisionEffects
                    {
                        ResourceDeltas = { [RunResourceIds.ScienceData] = 2, [RunResourceIds.Credits] = 5 },
                        HullDelta = -4f,
                        FlagsToSet = { "event.dark_relay_tapped" },
                        LogSummary = "Engineer: Relais angezapft, leichter Rückstoß.",
                    },
                },
                new DecisionOption
                {
                    Id = "scan_only",
                    Label = "Aus der Distanz scannen",
                    Description = "Sicherer Datenpunkt.",
                    FlavorHint = "Klein, sauber.",
                    ResolutionNarrative =
                        "Sensoren tasten den Kern aus sicherer Distanz ab. Ein Datenpunkt, kein Risiko.",
                    Effects = new DecisionEffects
                    {
                        ResourceDeltas = { [RunResourceIds.ScienceData] = 1 },
                        LogSummary = "Tactical: Relais gescannt.",
                    },
                },
            },
        });

        // ── PACK: pack_pirate_drama (3 Events) ──────────────────────────────
        list.Add(new NodeEvent
        {
            Id = "pack_pirate_ransom",
            Title = "Lösegeldforderung",
            Description = "Eine grobe Stimme im Funk verlangt Tribut für freie Passage.",
            Trigger = NodeEventTrigger.PreSector,
            EligibleTypes = { MissionType.Distress, MissionType.Salvage, MissionType.Hostile },
            MinRisk = 1,
            RequiresUnlockId = "pack_pirate_drama",
            ThreatCost = 3,
            Options =
            {
                new DecisionOption
                {
                    Id = "pay",
                    Label = "Zahlen",
                    Description = "Credits raus, dann durch.",
                    FlavorHint = "Sicheres Durchkommen, leichter Beutel.",
                    ResolutionNarrative =
                        "Credits wandern ins Nichts. Im Funk lacht jemand — und die Sensoren melden ein freies Fenster.",
                    Effects = new DecisionEffects
                    {
                        ResourceDeltas = { [RunResourceIds.Credits] = -20 },
                        FlagsToSet = { "event.ransom_paid" },
                        LogSummary = "Captain: Lösegeld bezahlt.",
                    },
                },
                new DecisionOption
                {
                    Id = "refuse",
                    Label = "Verweigern",
                    Description = "Kontakt blockieren — sie kommen mit.",
                    FlavorHint = "Gefecht garantiert.",
                    ResolutionNarrative =
                        "Wir schalten den Kanal stumm. In den Sensoren glitzern bereits zwei Vektoren auf Kollisionskurs.",
                    Effects = new DecisionEffects
                    {
                        SpawnAgents =
                        {
                            new DeferredAgentSpawn
                            {
                                AgentType = "pirate_raider",
                                TriggerId = "event_ransom_refused",
                                Origin = SpawnOrigin.EdgeRandom,
                                InitialMode = AgentBehaviorMode.Intercept,
                            },
                            new DeferredAgentSpawn
                            {
                                AgentType = "pirate_raider",
                                TriggerId = "event_ransom_refused",
                                Origin = SpawnOrigin.EdgeRandom,
                                InitialMode = AgentBehaviorMode.Intercept,
                            },
                        },
                        FlagsToSet = { "event.ransom_refused" },
                        LogSummary = "Captain: Lösegeld verweigert.",
                    },
                },
                new DecisionOption
                {
                    Id = "bluff",
                    Label = "Bluffen",
                    Description = "Mit gefälschter Patrouillen-Kennung antworten.",
                    FlavorHint = "Risikoreich — wenn es klappt.",
                    ResolutionNarrative =
                        "Tactical schickt eine gefälschte Patrouillen-Signatur. Im Funk bricht das Lachen ab; eine kurze Stille, dann ein knappes \u201eWir sehen uns\u201c.",
                    Effects = new DecisionEffects
                    {
                        FlagsToSet = { "event.ransom_bluffed" },
                        SystemEffects =
                        {
                            new SystemEffect { System = SystemId.Sensors, HeatDelta = 20f },
                        },
                        LogSummary = "Tactical: Bluff durchgezogen.",
                    },
                },
            },
        });

        list.Add(new NodeEvent
        {
            Id = "pack_pirate_distress_trap",
            Title = "Falle im Notruf",
            Description = "Ein tränenreicher Notruf weht durch die Bänder. Tactical wittert einen Köder.",
            Trigger = NodeEventTrigger.InSector,
            EligibleTypes = { MissionType.Distress, MissionType.Salvage },
            RequiresUnlockId = "pack_pirate_drama",
            MinRisk = 1,
            ThreatCost = 2,
            InSectorTrigger = new NodeEventInSectorTrigger
            {
                Kind = NodeEventInSectorKind.ProximityToLandmark,
                ProximityRef = TriggerRef.Landmark,
                ProximityRadius = 180f,
            },
            Options =
            {
                new DecisionOption
                {
                    Id = "spring",
                    Label = "Falle springen lassen",
                    Description = "Köder annehmen — vorbereitet auf Beschuss.",
                    FlavorHint = "Kontrolliertes Risiko, Bergung danach.",
                    ResolutionNarrative =
                        "Wir nehmen den Köder. Aus den Schatten bricht ein Jäger — direkt in unsere Schildlinie. Beute wartet, wenn wir ihn aufmachen.",
                    Effects = new DecisionEffects
                    {
                        SpawnAgents =
                        {
                            new DeferredAgentSpawn
                            {
                                AgentType = "pirate_raider",
                                TriggerId = "event_distress_trap",
                                Origin = SpawnOrigin.NearLandmark,
                                AnchorRef = TriggerRef.Landmark,
                                InitialMode = AgentBehaviorMode.Intercept,
                            },
                        },
                        ResourceDeltas = { [RunResourceIds.Credits] = 10 },
                        FlagsToSet = { "event.distress_trap_sprung" },
                        LogSummary = "Captain: Falle ausgelöst — Beute steht in Aussicht.",
                    },
                },
                new DecisionOption
                {
                    Id = "abort",
                    Label = "Kursabbruch",
                    Description = "Sensoren still, Schub hoch — wir machen einen Bogen.",
                    FlavorHint = "Sicher — und leer.",
                    ResolutionNarrative =
                        "Der Notruf verhallt im Rückspiegel. Tank etwas leerer, Karte unverändert.",
                    Effects = new DecisionEffects
                    {
                        ResourceDeltas = { [RunResourceIds.Fuel] = -1 },
                        LogSummary = "Captain: Notruf-Köder vermieden.",
                    },
                },
            },
        });

        list.Add(new NodeEvent
        {
            Id = "pack_pirate_defector",
            Title = "Überläufer",
            Description = "Ein Korsair-Pilot ruft auf einer alten Frequenz: er will aussteigen — gegen Geld und Begleitschutz.",
            Trigger = NodeEventTrigger.PreSector,
            EligibleTypes = { MissionType.Distress, MissionType.Salvage, MissionType.Hostile },
            RequiresUnlockId = "pack_pirate_drama",
            Options =
            {
                new DecisionOption
                {
                    Id = "buy_him",
                    Label = "Frei kaufen",
                    Description = "Credits raus, Daten und Ersatzteile zurück.",
                    FlavorHint = "Kostet jetzt, zahlt sich aus.",
                    ResolutionNarrative =
                        "Wir zahlen den Preis. Der Überläufer dockt kurz an, lädt Daten und ein paar geklaute Ersatzteile aus und verschwindet ins Rauschen.",
                    Effects = new DecisionEffects
                    {
                        ResourceDeltas =
                        {
                            [RunResourceIds.Credits] = -15,
                            [RunResourceIds.SpareParts] = 1,
                            [RunResourceIds.ScienceData] = 1,
                        },
                        FlagsToSet = { "event.defector_helped" },
                        LogSummary = "Captain: Überläufer aufgenommen.",
                    },
                },
                new DecisionOption
                {
                    Id = "report",
                    Label = "Anzeigen",
                    Description = "Patrouille informieren — kein Risiko, keine Beute.",
                    FlavorHint = "Sauberer Akt, dünner Beutel.",
                    ResolutionNarrative =
                        "Wir senden die Koordinaten an die nächste Patrouille. Das Funkgerät schweigt — der Überläufer wird nie wieder schreien.",
                    Effects = new DecisionEffects
                    {
                        FlagsToSet = { "event.defector_reported" },
                        LogSummary = "Captain: Überläufer den Behörden gemeldet.",
                    },
                },
                new DecisionOption
                {
                    Id = "ignore",
                    Label = "Stille",
                    Description = "Nicht reagieren.",
                    FlavorHint = "Nichts passiert — und doch ein Gewicht.",
                    ResolutionNarrative =
                        "Niemand antwortet. Der Frequenzkanal löst sich auf. Was später aus dem Piloten wurde, steht in keiner Datenbank.",
                    Effects = new DecisionEffects { LogSummary = "Captain: Funkruf ignoriert." },
                },
            },
        });

        // ── DIRECTOR-Pool: Hostile/Station Erweiterungen ────────────────────
        list.Add(new NodeEvent
        {
            Id = "pre_station_resupply_tip",
            Title = "Hafenflüstern",
            Description = "Ein Hafenmeister flüstert von einem Sonderposten frischer Ersatzteile — wenn man rechtzeitig dockt.",
            Trigger = NodeEventTrigger.PreSector,
            EligibleTypes = { MissionType.Station },
            ThreatCost = 0,
            Options =
            {
                new DecisionOption
                {
                    Id = "tip",
                    Label = "Tipp annehmen",
                    Description = "Ein paar Credits raus — Ersatzteil-Bonus beim Andocken.",
                    FlavorHint = "Kleiner Vorteil im Hafen.",
                    ResolutionNarrative =
                        "Wir lassen die Credits über den Tisch wandern. Der Hafenmeister nickt und tippt zwei Koordinaten — wenn der Tipp stimmt, bringt das Spareparts an Bord.",
                    Effects = new DecisionEffects
                    {
                        ResourceDeltas = { [RunResourceIds.Credits] = -5, [RunResourceIds.SpareParts] = 1 },
                        FlagsToSet = { "event.station_resupply_tip" },
                        LogSummary = "Captain: Hafenflüstern bezahlt.",
                    },
                },
                new DecisionOption
                {
                    Id = "ignore",
                    Label = "Ignorieren",
                    Description = "Standard-Andockprozedur, keine Extras.",
                    FlavorHint = "Routine.",
                    ResolutionNarrative =
                        "Wir winken ab. Der Hafenmeister verschwindet im Lärm — und mit ihm der Tipp.",
                    Effects = new DecisionEffects
                    {
                        LogSummary = "Captain: Hafenflüstern ignoriert.",
                    },
                },
            },
        });

        list.Add(new NodeEvent
        {
            Id = "pre_hostile_recon_drone",
            Title = "Aufklärungsdrohne",
            Description = "Eine kleine Drohne taucht am Sektorrand auf. Spähend, nicht angreifend — noch nicht.",
            Trigger = NodeEventTrigger.PreSector,
            EligibleTypes = { MissionType.Hostile },
            MinRisk = 2,
            ThreatCost = 1,
            Options =
            {
                new DecisionOption
                {
                    Id = "shoot",
                    Label = "Drohne abschießen",
                    Description = "Die Drohne stilllegen — der Sektor bleibt 'sauber', aber jemand wird sie vermissen.",
                    FlavorHint = "Kein Späher, keine Verstärkung.",
                    ResolutionNarrative =
                        "Ein kurzer Schuss, dann Funkstille. Die Drohne treibt zerlegt im Vakuum — die Patrouille im Sektor weiß nichts. Vorerst.",
                    Effects = new DecisionEffects
                    {
                        SystemEffects =
                        {
                            new SystemEffect { System = SystemId.Weapons, HeatDelta = 10f },
                        },
                        FlagsToSet = { "event.recon_drone_shot" },
                        LogSummary = "Tactical: Drohne ausgeschaltet.",
                    },
                },
                new DecisionOption
                {
                    Id = "let_pass",
                    Label = "Vorbeiziehen lassen",
                    Description = "Sensoren still — aber jemand könnte uns schon im Bild haben.",
                    FlavorHint = "Mehr Druck im Sektor.",
                    ResolutionNarrative =
                        "Die Drohne zieht ihre Bahn. Wer auch immer sie steuert, weiß nun, dass wir hier sind — Sensoren zeichnen später einen zusätzlichen Vektor.",
                    Effects = new DecisionEffects
                    {
                        FlagsToSet = { "event.recon_drone_passed" },
                        SpawnAgents =
                        {
                            new DeferredAgentSpawn
                            {
                                AgentType = "pirate_raider",
                                TriggerId = "event_recon_drone_alert",
                                Origin = SpawnOrigin.EdgeRandom,
                                InitialMode = AgentBehaviorMode.Intercept,
                                MinRisk = 2,
                            },
                        },
                        LogSummary = "Captain: Drohne vorbeigelassen.",
                    },
                },
            },
        });

        return list;
    }
}
