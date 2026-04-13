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

    /// <summary>Links back to the SectorEntity.Id this node represents.</summary>
    public string SectorEntityId { get; set; } = "";
}
