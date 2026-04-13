using Godot;
using SpacedOut.State;

namespace SpacedOut.Sector;

public class SectorEntity
{
    public string Id { get; set; } = "";
    public SectorEntityType Type { get; set; }
    public string AssetId { get; set; } = "";
    public Vector3 WorldPosition { get; set; }
    public Vector3 Rotation { get; set; }
    public float Scale { get; set; } = 1f;
    public float Radius { get; set; }
    public string[] Tags { get; set; } = System.Array.Empty<string>();

    public MapPresence MapPresence { get; set; } = MapPresence.None;
    public bool IsMissionRelevant { get; set; }
    public bool IsLandmark { get; set; }

    // Runtime state (mutated during gameplay)
    public DiscoveryState Discovery { get; set; } = DiscoveryState.Hidden;
    public float ScanProgress { get; set; }
    public string DisplayName { get; set; } = "";
    public ContactType ContactType { get; set; } = ContactType.Unknown;
    public float ThreatLevel { get; set; }

    // Pre-revealed entities skip the discovery pipeline
    public bool PreRevealed { get; set; }

    // Dynamic objects
    public Vector3 Velocity { get; set; }
    public bool IsMovable { get; set; }
}
