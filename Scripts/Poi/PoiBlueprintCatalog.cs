using System.Collections.Generic;
using SpacedOut.Run;
using SpacedOut.State;

namespace SpacedOut.Poi;

/// <summary>Static registry of all POI blueprints.</summary>
public static class PoiBlueprintCatalog
{
    private static readonly Dictionary<string, PoiBlueprint> Blueprints = new()
    {
        // ── Asteroid-field specific ─────────────────────────────────

        ["navigation_relay"] = new PoiBlueprint
        {
            Id = "navigation_relay",
            DisplayName = "Relais-Signatur",
            AnalyzedName = "Navigationsrelais",
            AnalysisDescription =
                "Datenbus und Peilfunktion zugänglich. Vollständige Sprungkoordinaten liegen im verschlüsselten Kern — "
                + "Extraktion durch den Maschinenraum nötig.",

            RequiresDrill = false,
            RequiresExtraction = true,
            UsesTractorBeam = false,

            AnalyzeRange = 170f,
            ExtractRange = 60f,

            AnalyzeDuration = 9f,
            ExtractDuration = 12f,

            ExtractHeatTarget = SystemId.Drive,
            ExtractHeatRate = 1.1f,

            Rewards = new[]
            {
                new PoiRewardEntry { ResourceId = RunResourceIds.ScienceData, Min = 1, Max = 2 },
            },
        },

        ["rich_vein"] = new PoiBlueprint
        {
            Id = "rich_vein",
            DisplayName = "Mineral-Signatur",
            AnalyzedName = "Erz-Ader",
            AnalysisDescription = "Konzentrierte Metalladern durchziehen die Oberfläche. Bohrung und Traktorstrahl erforderlich.",

            RequiresDrill = true,
            DrillRequiresMiningMode = true,
            RequiresExtraction = true,
            UsesTractorBeam = true,

            AnalyzeRange = 150f,
            DrillRange = 80f,
            ExtractRange = 50f,

            AnalyzeDuration = 10f,
            DrillDuration = 15f,
            ExtractDuration = 8f,

            DrillHeatTarget = SystemId.Weapons,
            DrillHeatRate = 2.5f,
            ExtractHeatTarget = SystemId.Drive,
            ExtractHeatRate = 1.5f,

            Rewards = new[]
            {
                new PoiRewardEntry { ResourceId = RunResourceIds.SpareParts, Min = 2, Max = 4 },
            },
        },

        ["crystal_geode"] = new PoiBlueprint
        {
            Id = "crystal_geode",
            DisplayName = "Kristalline Signatur",
            AnalyzedName = "Kristall-Geode",
            AnalysisDescription = "Freigelegte Geode mit empfindlichen Kristallstrukturen. Präzisions-Extraktion im Nahbereich nötig — Vorsicht vor Überhitzung.",

            RequiresDrill = false,
            RequiresExtraction = true,
            UsesTractorBeam = false,

            AnalyzeRange = 150f,
            ExtractRange = 30f,

            AnalyzeDuration = 12f,
            ExtractDuration = 20f,

            ExtractHeatTarget = SystemId.Sensors,
            ExtractHeatRate = 3.5f,

            Rewards = new[]
            {
                new PoiRewardEntry { ResourceId = RunResourceIds.ScienceData, Min = 2, Max = 4 },
            },
            ReducedRewards = new[]
            {
                new PoiRewardEntry { ResourceId = RunResourceIds.ScienceData, Min = 0, Max = 1 },
            },
        },

        ["fissure_cavity"] = new PoiBlueprint
        {
            Id = "fissure_cavity",
            DisplayName = "Struktur-Anomalie",
            AnalyzedName = "Spalten-Hohlraum",
            AnalysisDescription = "Tiefer Hohlraum mit gemischtem Material. Nur Präzisionsbohrung — Barrage führt zum Kollaps. Nach Öffnung 45 s bis Instabilität.",

            RequiresDrill = true,
            DrillRequiresMiningMode = true,
            BarrageCausesFailure = true,
            RequiresExtraction = true,
            UsesTractorBeam = false,

            AnalyzeRange = 150f,
            DrillRange = 80f,
            ExtractRange = 50f,

            AnalyzeDuration = 10f,
            DrillDuration = 10f,
            ExtractDuration = 12f,
            InstabilityTimer = 45f,

            DrillHeatTarget = SystemId.Weapons,
            DrillHeatRate = 2f,
            ExtractHeatTarget = SystemId.Sensors,
            ExtractHeatRate = 2f,

            FailureHullDelta = -5f,

            Rewards = new[]
            {
                new PoiRewardEntry { ResourceId = RunResourceIds.SpareParts, Min = 1, Max = 3 },
                new PoiRewardEntry { ResourceId = RunResourceIds.ScienceData, Min = 0, Max = 1 },
            },
        },

        // ── Cross-biome wildcards ───────────────────────────────────

        ["drifting_pod"] = new PoiBlueprint
        {
            Id = "drifting_pod",
            DisplayName = "Treibendes Objekt",
            AnalyzedName = "Treibende Kapsel",
            AnalysisDescription = "Versiegelte Kapsel — Inhalt nach Analyse sichtbar.",

            RequiresDrill = false,
            RequiresExtraction = true,
            UsesTractorBeam = true,

            AnalyzeRange = 150f,
            ExtractRange = 50f,

            AnalyzeDuration = 8f,
            ExtractDuration = 6f,

            ExtractHeatTarget = SystemId.Drive,
            ExtractHeatRate = 1f,

            TrapChance = 0.15f,
            TrapDamage = 8f,

            RewardProfiles = new[]
            {
                new PoiRewardProfile
                {
                    ProfileId = "rescue",
                    Label = "Rettungskapsel (Lebenszeichen)",
                    Weight = 1f,
                    Rewards = System.Array.Empty<PoiRewardEntry>(),
                    HullDelta = 5f,
                },
                new PoiRewardProfile
                {
                    ProfileId = "cargo_parts",
                    Label = "Frachtcontainer (Ersatzteile)",
                    Weight = 1f,
                    Rewards = new[]
                    {
                        new PoiRewardEntry { ResourceId = RunResourceIds.SpareParts, Min = 1, Max = 3 },
                    },
                },
                new PoiRewardProfile
                {
                    ProfileId = "cargo_data",
                    Label = "Frachtcontainer (Forschungsdaten)",
                    Weight = 0.8f,
                    Rewards = new[]
                    {
                        new PoiRewardEntry { ResourceId = RunResourceIds.ScienceData, Min = 1, Max = 2 },
                    },
                },
                new PoiRewardProfile
                {
                    ProfileId = "trap",
                    Label = "Sprengfalle!",
                    Weight = 0f,
                    IsTrap = true,
                    HullDelta = -8f,
                },
            },
        },

        ["anomaly_source"] = new PoiBlueprint
        {
            Id = "anomaly_source",
            DisplayName = "Energiefluktuation",
            AnalyzedName = "Anomalie-Signatur",
            AnalysisDescription = "Lokalisierte Energieanomalie. Ertrag steigt mit Nähe — Interferenz beeinträchtigt Sensoren.",

            RequiresDrill = false,
            RequiresExtraction = false,

            AnalyzeRange = 200f,

            AnalyzeDuration = 25f,

            RewardScalesWithDistance = true,
            FullRewardRange = 60f,
            HalfRewardRange = 150f,

            SensorRangePenalty = 0.4f,
            SensorPenaltyDuration = 30f,
            CloseApproachHeatSpike = 15f,

            Rewards = new[]
            {
                new PoiRewardEntry { ResourceId = RunResourceIds.ScienceData, Min = 3, Max = 5 },
            },
            ReducedRewards = new[]
            {
                new PoiRewardEntry { ResourceId = RunResourceIds.ScienceData, Min = 0, Max = 1 },
            },
        },

        ["argos_blackbox"] = new PoiBlueprint
        {
            Id = "argos_blackbox",
            DisplayName = "Wracksignatur",
            AnalyzedName = "ARGOS-2 — Frachtkern",
            AnalysisDescription =
                "Hülle aufgerissen, keine bergbare Hauptfracht mehr. Datenkern und vereinzelte Module sind noch extrahierbar — Traktor empfohlen.",

            RequiresDrill = false,
            RequiresExtraction = true,
            UsesTractorBeam = true,

            AnalyzeRange = 160f,
            ExtractRange = 55f,

            AnalyzeDuration = 7f,
            ExtractDuration = 8f,

            ExtractHeatTarget = SystemId.Drive,
            ExtractHeatRate = 1.2f,

            RewardProfiles =
            [
                new PoiRewardProfile
                {
                    ProfileId = "flight_data",
                    Label = "Flugschreiber & Routendaten",
                    Weight = 1f,
                    Rewards =
                    [
                        new PoiRewardEntry { ResourceId = RunResourceIds.ScienceData, Min = 2, Max = 4 },
                    ],
                },
                new PoiRewardProfile
                {
                    ProfileId = "spare_cargo",
                    Label = "Bergbare Ersatzmodule",
                    Weight = 1f,
                    Rewards =
                    [
                        new PoiRewardEntry { ResourceId = RunResourceIds.SpareParts, Min = 1, Max = 3 },
                        new PoiRewardEntry { ResourceId = RunResourceIds.ScienceData, Min = 0, Max = 1 },
                    ],
                },
            ],
        },

        ["wreck_fragment"] = new PoiBlueprint
        {
            Id = "wreck_fragment",
            DisplayName = "Wracksignal",
            AnalyzedName = "Wracksegment",
            AnalysisDescription = "Zerstörte Schiffshülle — möglicherweise bergbare Komponenten. Vorsicht: Sprengfallen möglich.",

            RequiresDrill = true,
            DrillRequiresMiningMode = true,
            RequiresExtraction = true,
            UsesTractorBeam = false,

            AnalyzeRange = 150f,
            DrillRange = 60f,
            ExtractRange = 50f,

            AnalyzeDuration = 10f,
            DrillDuration = 8f,
            ExtractDuration = 10f,

            DrillHeatTarget = SystemId.Weapons,
            DrillHeatRate = 2f,
            ExtractHeatTarget = SystemId.Sensors,
            ExtractHeatRate = 1.5f,

            TrapChance = 0.10f,
            TrapDamage = 10f,

            Rewards = new[]
            {
                new PoiRewardEntry { ResourceId = RunResourceIds.SpareParts, Min = 2, Max = 3 },
                new PoiRewardEntry { ResourceId = RunResourceIds.ScienceData, Min = 1, Max = 2 },
            },
        },
    };

    public static PoiBlueprint? GetOrNull(string id) =>
        Blueprints.TryGetValue(id, out var bp) ? bp : null;

    public static IReadOnlyDictionary<string, PoiBlueprint> GetAll() => Blueprints;
}
