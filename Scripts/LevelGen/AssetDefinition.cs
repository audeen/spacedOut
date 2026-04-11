using Godot;

namespace SpacedOut.LevelGen;

public enum AssetCategory
{
    AsteroidLarge,
    AsteroidMedium,
    AsteroidSmall,
    WreckMain,
    WreckMedium,
    DebrisCluster,
    StationCore,
    StationModule,
    CargoCluster,
    UtilityNode,
    ResourceNode,
    PoiMarker,
    LootMarker,
    Beacon,
    EncounterMarker,
    ExitMarker,
}

public enum PlaceholderShape
{
    Sphere,
    Box,
    Capsule,
    Cylinder,
}

public class AssetDefinition
{
    public string Id { get; init; } = "";
    public string DisplayName { get; init; } = "";
    public AssetCategory Category { get; init; }
    public string[] Tags { get; init; } = System.Array.Empty<string>();
    public PlaceholderShape Shape { get; init; } = PlaceholderShape.Sphere;
    public float Radius { get; init; } = 1f;
    public float Weight { get; init; } = 1f;
    public float MinSpacing { get; init; } = 5f;
    public string[] AllowedBiomes { get; init; } = System.Array.Empty<string>();
    public bool IsLandmark { get; init; }
    public bool Clusterable { get; init; }
    public Color DebugColor { get; init; } = Colors.White;
    public string? ScenePath { get; init; }
    public float MinScale { get; init; } = 1f;
    public float MaxScale { get; init; } = 1f;
}
