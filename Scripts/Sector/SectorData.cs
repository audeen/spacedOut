using System.Collections.Generic;
using Godot;
using SpacedOut.Run;

namespace SpacedOut.Sector;

public class SectorData
{
    public int Seed { get; set; }
    public string BiomeId { get; set; } = "";
    public float LevelRadius { get; set; }
    public Vector3 SpawnPoint { get; set; }
    public Vector3 ExitPoint { get; set; }
    public Vector3 LandmarkPosition { get; set; }
    public Vector3 EncounterPosition { get; set; }
    public RunNodeType? NodeType { get; set; }

    public List<SectorEntity> Entities { get; } = new();
    public List<ResourceZone> ResourceZones { get; } = new();
    public List<Vector3> ClusterCenters { get; } = new();
}
