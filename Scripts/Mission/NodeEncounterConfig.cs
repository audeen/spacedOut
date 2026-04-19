using System.Collections.Generic;
using SpacedOut.Run;

namespace SpacedOut.Mission;

/// <summary>
/// Mission encounter parameters for LevelGenerator and MissionController.
/// </summary>
public class NodeEncounterConfig
{
    public string BiomeId { get; set; } = "asteroid_field";
    public string MissionTitle { get; set; } = "";
    public string BriefingText { get; set; } = "";

    public float LevelRadiusMultiplier { get; set; } = 1f;
    public int ExtraMarkerCount { get; set; }

    /// <summary>
    /// When true, the classic timed phase sequence (Anflug → …) runs and phase UI is shown.
    /// Default false: sandbox-style mission in a single operational phase (opt in for scripted time-window missions).
    /// </summary>
    public bool UseStructuredPhases { get; set; }

    public float DamageMultiplier { get; set; } = 1f;
    public int FuelCost { get; set; } = 1;
    public bool HasHostileEncounter { get; set; }
    public bool HasDistressObjective { get; set; }
    public bool HasScanObjective { get; set; }
    public bool IsStation { get; set; }
    public bool IsBoss { get; set; }

    public List<string> ForcedEvents { get; set; } = new();

    /// <summary>Fuel charged when entering a run-map node of this type (see <see cref="FromRunNodeType"/>).</summary>
    public static int GetFuelCostFor(RunNodeType type) => type switch
    {
        RunNodeType.Start => 0,
        RunNodeType.Station => 0,
        RunNodeType.End => 0,
        _ => 1,
    };

    /// <summary>Default encounter for debug missions (no campaign map node).</summary>
    public static NodeEncounterConfig DefaultForBiome(string biomeId) =>
        new()
        {
            BiomeId = biomeId,
            MissionTitle = "Testeinsatz",
            BriefingText = "Debug-Mission im gewählten Biom.",
            FuelCost = 1,
        };

    /// <summary>Maps run node types to neutral mission presets (no mission names wired to specific story nodes).</summary>
    public static NodeEncounterConfig FromRunNodeType(RunNodeType type, string biomeId, int difficulty = 2)
    {
        return type switch
        {
            RunNodeType.Start => new NodeEncounterConfig
            {
                BiomeId = biomeId,
                MissionTitle = "Sektorsprung",
                BriefingText = "Sprungantrieb stabilisiert.",
                FuelCost = GetFuelCostFor(RunNodeType.Start),
                LevelRadiusMultiplier = 5f,
            },
            RunNodeType.Story => new NodeEncounterConfig
            {
                BiomeId = biomeId,
                MissionTitle = "Erzählsegment",
                BriefingText = "Narrativer Abschnitt (Platzhalter).",
                HasScanObjective = true,
                ExtraMarkerCount = 2 + difficulty,
                FuelCost = GetFuelCostFor(RunNodeType.Story),
                LevelRadiusMultiplier = 5f,
            },
            RunNodeType.Side or RunNodeType.Anomaly => new NodeEncounterConfig
            {
                BiomeId = biomeId,
                MissionTitle = "Nebenauftrag",
                BriefingText = "Generischer Auftrag (Platzhalter).",
                ExtraMarkerCount = 2,
                FuelCost = GetFuelCostFor(RunNodeType.Side),
                LevelRadiusMultiplier = 5f,
            },
            RunNodeType.Station => new NodeEncounterConfig
            {
                BiomeId = biomeId,
                MissionTitle = "Station",
                BriefingText = "Stationsumgebung (Platzhalter).",
                IsStation = true,
                DamageMultiplier = 0f,
                FuelCost = GetFuelCostFor(RunNodeType.Station),
                LevelRadiusMultiplier = 0.6f,
            },
            RunNodeType.Hostile => new NodeEncounterConfig
            {
                BiomeId = biomeId,
                MissionTitle = "Kontakt",
                BriefingText = "Mögliche Bedrohung (Platzhalter).",
                HasHostileEncounter = true,
                DamageMultiplier = 1.0f + difficulty * 0.15f,
                ForcedEvents = { "unknown_approach" },
                FuelCost = GetFuelCostFor(RunNodeType.Hostile),
                LevelRadiusMultiplier = 5f,
            },
            RunNodeType.End => new NodeEncounterConfig
            {
                BiomeId = biomeId,
                MissionTitle = "Abschluss",
                BriefingText = "Sektorabschluss (Platzhalter).",
                FuelCost = GetFuelCostFor(RunNodeType.End),
                LevelRadiusMultiplier = 5f,
            },
            _ => DefaultForBiome(biomeId),
        };
    }
}
