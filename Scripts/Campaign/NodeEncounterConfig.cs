using System;
using System.Collections.Generic;
using SpacedOut.LevelGen;
using SpacedOut.State;

namespace SpacedOut.Campaign;

/// <summary>
/// Translates a <see cref="NodeType"/> into concrete mission parameters
/// that control how LevelGenerator and MissionController behave for that node.
/// </summary>
public class NodeEncounterConfig
{
    public string BiomeId { get; set; } = "asteroid_field";
    public string MissionTitle { get; set; } = "";
    public string BriefingText { get; set; } = "";

    // LevelGenerator tuning
    public float LevelRadiusMultiplier { get; set; } = 1f;
    public int ExtraMarkerCount { get; set; }

    // MissionController tuning
    public float TimeLimit { get; set; } = 720f;
    public float DamageMultiplier { get; set; } = 1f;
    public int FuelCost { get; set; } = 1;
    public bool HasHostileEncounter { get; set; }
    public bool HasDistressObjective { get; set; }
    public bool HasScanObjective { get; set; }
    public bool IsStation { get; set; }
    public bool IsBoss { get; set; }

    // Events to inject during the mission
    public List<string> ForcedEvents { get; set; } = new();

    // ── Factory ─────────────────────────────────────────────────────

    public static NodeEncounterConfig FromNode(MapNode node, string sectorBiome)
    {
        string biome = node.BiomeOverride ?? sectorBiome;
        int diff = node.DifficultyRating;

        return node.Type switch
        {
            NodeType.Start => StartConfig(biome),
            NodeType.Navigation => NavigationConfig(biome, diff),
            NodeType.ScanAnomaly => ScanAnomalyConfig(biome, diff),
            NodeType.DebrisField => DebrisFieldConfig(biome, diff),
            NodeType.Encounter => EncounterConfig(biome, diff),
            NodeType.DistressSignal => DistressConfig(biome, diff),
            NodeType.Station => StationConfig(biome),
            NodeType.EliteEncounter => EliteConfig(biome, diff),
            NodeType.Boss => BossConfig(biome, diff),
            _ => NavigationConfig(biome, diff),
        };
    }

    // ── Presets ──────────────────────────────────────────────────────

    private static NodeEncounterConfig StartConfig(string biome) => new()
    {
        BiomeId = biome,
        MissionTitle = "Sektorsprung",
        BriefingText = "Sprungantrieb stabilisiert. Sektor betreten.",
        TimeLimit = 60f,
        FuelCost = 0,
        LevelRadiusMultiplier = 0.5f,
    };

    private static NodeEncounterConfig NavigationConfig(string biome, int diff) => new()
    {
        BiomeId = biome,
        MissionTitle = biome switch
        {
            "asteroid_field" => "Asteroidenfeld-Durchquerung",
            "wreck_zone" => "Trümmerpassage",
            "station_periphery" => "Frachtrouten-Navigation",
            _ => "Routenabschnitt",
        },
        BriefingText = biome switch
        {
            "asteroid_field" =>
                "Durchqueren Sie den Asteroidenfeld-Abschnitt. " +
                "Halten Sie Schilde bereit und navigieren Sie zum Ausgangspunkt.",
            "wreck_zone" =>
                "Navigieren Sie vorsichtig durch die Trümmerzone. " +
                "Sensoren auf maximale Reichweite.",
            "station_periphery" =>
                "Folgen Sie der Frachtroute durch die Stationsperipherie.",
            _ => "Standard-Navigationsauftrag.",
        },
        TimeLimit = 600f - diff * 30f,
        DamageMultiplier = 0.8f + diff * 0.1f,
        FuelCost = 1,
    };

    private static NodeEncounterConfig ScanAnomalyConfig(string biome, int diff) => new()
    {
        BiomeId = biome,
        MissionTitle = "Anomalie-Untersuchung",
        BriefingText =
            "Sensoren haben eine ungewöhnliche Signatur erfasst. " +
            "Nähern Sie sich und scannen Sie das Zielgebiet vollständig. " +
            "Unbekannte Risiken sind möglich.",
        TimeLimit = 540f,
        HasScanObjective = true,
        ExtraMarkerCount = 3 + diff,
        ForcedEvents = { "sensor_shimmer" },
        FuelCost = 1,
    };

    private static NodeEncounterConfig DebrisFieldConfig(string biome, int diff) => new()
    {
        BiomeId = biome,
        MissionTitle = "Trümmerfeld-Passage",
        BriefingText =
            "Ein dichtes Trümmerfeld liegt auf Ihrem Kurs. " +
            "Hullschäden sind wahrscheinlich. " +
            "Navigator und Ingenieur müssen eng zusammenarbeiten.",
        TimeLimit = 480f,
        DamageMultiplier = 1.5f + diff * 0.2f,
        LevelRadiusMultiplier = 0.8f,
        ForcedEvents = { "shield_stress" },
        FuelCost = 1,
    };

    private static NodeEncounterConfig EncounterConfig(string biome, int diff) => new()
    {
        BiomeId = biome,
        MissionTitle = "Feindkontakt",
        BriefingText =
            "Ein unbekanntes Schiff wurde im Sektor erkannt. " +
            "Vorbereitung auf möglichen Kontakt. " +
            "Taktik-Station soll Kontakt scannen und klassifizieren.",
        TimeLimit = 600f,
        HasHostileEncounter = true,
        DamageMultiplier = 1.0f + diff * 0.3f,
        ForcedEvents = { "unknown_approach" },
        FuelCost = 1,
    };

    private static NodeEncounterConfig DistressConfig(string biome, int diff) => new()
    {
        BiomeId = biome,
        MissionTitle = "Rettungsmission",
        BriefingText =
            "Ein automatisches Notsignal wurde empfangen. " +
            "Lokalisieren Sie die Quelle und leiten Sie Bergungsmaßnahmen ein. " +
            "Zeitfenster ist begrenzt.",
        TimeLimit = 480f,
        HasDistressObjective = true,
        ExtraMarkerCount = 2,
        ForcedEvents = { "recovery_window" },
        FuelCost = 1,
    };

    private static NodeEncounterConfig StationConfig(string biome) => new()
    {
        BiomeId = biome,
        MissionTitle = "Stationsandockung",
        BriefingText =
            "Eine Versorgungsstation ist in Reichweite. " +
            "Docken Sie an, um Reparaturen durchzuführen und Vorräte aufzufüllen.",
        TimeLimit = 300f,
        IsStation = true,
        DamageMultiplier = 0f,
        FuelCost = 0,
        LevelRadiusMultiplier = 0.6f,
    };

    private static NodeEncounterConfig EliteConfig(string biome, int diff) => new()
    {
        BiomeId = biome,
        MissionTitle = "Schwerer Kontakt",
        BriefingText =
            "Ein stark bewaffnetes Schiff operiert in diesem Sektor. " +
            "Erwarten Sie aggressive Manöver und schweren Beschuss. " +
            "Alle Stationen in Alarmbereitschaft!",
        TimeLimit = 660f,
        HasHostileEncounter = true,
        DamageMultiplier = 1.8f + diff * 0.3f,
        ForcedEvents = { "shield_stress", "unknown_approach" },
        FuelCost = 1,
    };

    private static NodeEncounterConfig BossConfig(string biome, int diff) => new()
    {
        BiomeId = biome,
        MissionTitle = biome switch
        {
            "asteroid_field" => "Gravitationskern-Durchbruch",
            "wreck_zone" => "Flaggschiff-Konfrontation",
            "station_periphery" => "Andockmanöver unter Feuer",
            _ => "Sektor-Finale",
        },
        BriefingText = biome switch
        {
            "asteroid_field" =>
                "Ein gewaltiger Asteroid mit starkem Gravitationsfeld blockiert den Sektorausgang. " +
                "Navigieren Sie unter extremen Bedingungen durch das Gravitationsfeld.",
            "wreck_zone" =>
                "Das Wrack eines Flaggschiffs blockiert den Weg. " +
                "Teile des Schiffes sind noch aktiv und feuern automatisch.",
            "station_periphery" =>
                "Die Stationskontrolle verweigert die Freigabe. " +
                "Erzwingen Sie die Durchfahrt unter Beschuss der Stationsverteidigung.",
            _ => "Die finale Herausforderung dieses Sektors steht bevor.",
        },
        TimeLimit = 720f,
        IsBoss = true,
        HasHostileEncounter = true,
        DamageMultiplier = 2.0f + diff * 0.4f,
        ForcedEvents = { "sensor_shimmer", "shield_stress", "unknown_approach" },
        FuelCost = 2,
    };
}
