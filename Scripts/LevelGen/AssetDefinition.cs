using Godot;
using SpacedOut.Sector;

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
    MissionStructure,
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

    /// <summary>
    /// Pool of PackedScene paths. When non-empty and at least one path exists,
    /// <see cref="PlaceholderFactory"/> picks one deterministically via
    /// <see cref="AssetVariantPicker"/> and instantiates the scene instead of
    /// building a primitive. Empty pool => primitive fallback.
    /// </summary>
    public string[] ScenePaths { get; init; } = System.Array.Empty<string>();

    public float MinScale { get; init; } = 1f;
    public float MaxScale { get; init; } = 1f;

    /// <summary>
    /// Additional uniform scale applied on top of the per-instance <c>Scale</c>
    /// to match real mesh size to the logical <see cref="Radius"/>.
    /// </summary>
    public float VisualScale { get; init; } = 1f;

    /// <summary>
    /// When true (default), a deterministic random yaw (Y-axis rotation) is
    /// applied per instance so mesh variants don't look repetitive.
    /// </summary>
    public bool MeshYawRandomize { get; init; } = true;

    /// <summary>How this asset appears on 2D maps. Overrides automatic category-based resolution.</summary>
    public MapPresence DefaultMapPresence { get; init; } = MapPresence.None;

    /// <summary>Map icon color override (defaults to DebugColor if null).</summary>
    public Color? MapColor { get; init; }

    /// <summary>3D marker style for entities that have no physical mesh (signals, contacts).</summary>
    public MarkerVisualType MarkerVisual { get; init; } = MarkerVisualType.None;
}
