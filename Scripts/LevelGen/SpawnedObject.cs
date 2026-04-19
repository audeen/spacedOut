using Godot;

namespace SpacedOut.LevelGen;

public partial class SpawnedObject : Node3D
{
    public string InstanceId { get; set; } = "";
    public string AssetId { get; set; } = "";
    public AssetCategory Category { get; set; }
    public string BiomeType { get; set; } = "";
    public float ObjectRadius { get; set; }
    public string[] Tags { get; set; } = System.Array.Empty<string>();
    public bool IsLandmark { get; set; }
    public string? RealScenePath { get; set; }
    public bool IsPlaceholder { get; set; } = true;

    /// <summary>
    /// True when this entry represents an asteroid rendered via the shared
    /// <see cref="InstancedAsteroidPool"/> MultiMesh. In that case the node
    /// is kept out of the SceneTree (bookkeeping only) and all visual data
    /// lives inside the pool.
    /// </summary>
    public bool IsInstanced { get; set; }

    /// <summary>Links back to the SectorEntity.Id this node represents.</summary>
    public string SectorEntityId { get; set; } = "";
}
