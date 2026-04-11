using System.Collections.Generic;
using System.Linq;

namespace SpacedOut.LevelGen;

public class BiomeDefinition
{
    public string Id { get; init; } = "";
    public string DisplayName { get; init; } = "";
    public float LevelRadius { get; init; } = 400f;

    // Landmark tier
    public string[] LandmarkAssets { get; init; } = System.Array.Empty<string>();
    public int LandmarkCount { get; init; } = 1;

    // Mid-scale tier
    public string[] MidScaleAssets { get; init; } = System.Array.Empty<string>();
    public int MidScaleMin { get; init; } = 10;
    public int MidScaleMax { get; init; } = 20;

    // Small / scatter tier
    public string[] SmallAssets { get; init; } = System.Array.Empty<string>();
    public int SmallMin { get; init; } = 30;
    public int SmallMax { get; init; } = 60;

    // Scatter (loose objects outside clusters)
    public int ScatterMin { get; init; } = 10;
    public int ScatterMax { get; init; } = 30;

    // Markers (POI, loot, beacons)
    public string[] MarkerAssets { get; init; } = System.Array.Empty<string>();
    public int MarkerCount { get; init; } = 2;

    // Cluster settings
    public int ClusterCountMin { get; init; } = 3;
    public int ClusterCountMax { get; init; } = 5;
    public float ClusterRadius { get; init; } = 55f;
    public float ClusterSpacing { get; init; } = 80f;

    // Layout settings
    public float SpawnSafeRadius { get; init; } = 45f;
    public float LandmarkCenterOffset { get; init; } = 0.25f;
    public float CorridorWidth { get; init; } = 25f;

    // ─── Static biome registry ──────────────────────────────────────

    private static readonly Dictionary<string, BiomeDefinition> Biomes = new()
    {
        ["asteroid_field"] = new BiomeDefinition
        {
            Id = "asteroid_field",
            DisplayName = "Asteroidenfeld",
            LevelRadius = 1200f,

            LandmarkAssets = new[] { "asteroid_large" },
            LandmarkCount = 1,

            MidScaleAssets = new[] { "asteroid_medium" },
            MidScaleMin = 40,
            MidScaleMax = 65,

            SmallAssets = new[] { "asteroid_small" },
            SmallMin = 90,
            SmallMax = 160,

            ScatterMin = 40,
            ScatterMax = 80,

            MarkerAssets = new[] { "resource_node", "poi_marker" },
            MarkerCount = 8,

            ClusterCountMin = 6,
            ClusterCountMax = 10,
            ClusterRadius = 100f,
            ClusterSpacing = 150f,

            SpawnSafeRadius = 80f,
            LandmarkCenterOffset = 0.35f,
            CorridorWidth = 50f,
        },

        ["wreck_zone"] = new BiomeDefinition
        {
            Id = "wreck_zone",
            DisplayName = "Wrackzone",
            LevelRadius = 1000f,

            LandmarkAssets = new[] { "wreck_main" },
            LandmarkCount = 1,

            MidScaleAssets = new[] { "wreck_medium" },
            MidScaleMin = 35,
            MidScaleMax = 55,

            SmallAssets = new[] { "debris_cluster" },
            SmallMin = 80,
            SmallMax = 140,

            ScatterMin = 30,
            ScatterMax = 60,

            MarkerAssets = new[] { "beacon", "loot_marker", "poi_marker" },
            MarkerCount = 10,

            ClusterCountMin = 8,
            ClusterCountMax = 12,
            ClusterRadius = 80f,
            ClusterSpacing = 130f,

            SpawnSafeRadius = 70f,
            LandmarkCenterOffset = 0.3f,
            CorridorWidth = 40f,
        },

        ["station_periphery"] = new BiomeDefinition
        {
            Id = "station_periphery",
            DisplayName = "Stationsperipherie",
            LevelRadius = 950f,

            LandmarkAssets = new[] { "station_core" },
            LandmarkCount = 1,

            MidScaleAssets = new[] { "station_module" },
            MidScaleMin = 25,
            MidScaleMax = 45,

            SmallAssets = new[] { "cargo_cluster" },
            SmallMin = 65,
            SmallMax = 110,

            ScatterMin = 20,
            ScatterMax = 45,

            MarkerAssets = new[] { "utility_node", "poi_marker" },
            MarkerCount = 7,

            ClusterCountMin = 5,
            ClusterCountMax = 9,
            ClusterRadius = 75f,
            ClusterSpacing = 120f,

            SpawnSafeRadius = 80f,
            LandmarkCenterOffset = 0.3f,
            CorridorWidth = 48f,
        },
    };

    public static BiomeDefinition Get(string id) => Biomes[id];
    public static string[] GetAllIds() => Biomes.Keys.ToArray();
    public static IReadOnlyDictionary<string, BiomeDefinition> GetAll() => Biomes;
}
