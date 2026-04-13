using Godot;
using SpacedOut.State;

namespace SpacedOut.Sector;

public class ResourceZone
{
    public string Id { get; set; } = "";
    public ResourceFieldType ResourceType { get; set; }
    public Vector3 Center { get; set; }
    public float Radius { get; set; }
    public float Density { get; set; }
    public Color MapColor { get; set; }
    public DiscoveryState Discovery { get; set; } = DiscoveryState.Hidden;

    // Prepared for future extraction mechanics
    public float TotalAmount { get; set; }
    public float RemainingAmount { get; set; }
}
