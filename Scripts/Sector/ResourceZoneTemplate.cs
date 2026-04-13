using System;
using Godot;

namespace SpacedOut.Sector;

public class ResourceZoneTemplate
{
    public ResourceFieldType ResourceType { get; init; }
    public int CountMin { get; init; } = 1;
    public int CountMax { get; init; } = 2;
    public float RadiusMin { get; init; } = 80f;
    public float RadiusMax { get; init; } = 200f;
    public float DensityMin { get; init; } = 0.3f;
    public float DensityMax { get; init; } = 0.9f;
    public Color MapColor { get; init; } = Colors.White;

    /// <summary>Asset IDs to spawn inside this zone (e.g. "asteroid_small", "debris_cluster").</summary>
    public string[] FillAssets { get; init; } = Array.Empty<string>();

    /// <summary>Base fill count per zone, scaled by density. Actual count = BaseFillCount * density.</summary>
    public int BaseFillCount { get; init; } = 25;
}
