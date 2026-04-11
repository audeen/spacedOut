using Godot;

namespace SpacedOut.LevelGen;

/// <summary>
/// Wraps every spawned placeholder or real asset node.
/// Carries the metadata the generator and the later AssetReplacer need.
/// </summary>
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
}
