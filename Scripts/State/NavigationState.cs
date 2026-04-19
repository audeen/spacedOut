namespace SpacedOut.State;

/// <summary>Groups route and navigation-related state.</summary>
public class NavigationState
{
    public RouteState Route { get; set; } = new();
    public TargetTrackingState TargetTracking { get; set; } = new();
}

/// <summary>EVE-style persistent steering relative to a contact (Follow / Orbit / Keep at range).</summary>
public class TargetTrackingState
{
    public TargetTrackingMode Mode { get; set; } = TargetTrackingMode.None;
    public string? TrackedContactId { get; set; }
    public float Range { get; set; } = 200f;
    public float OrbitAngle { get; set; }
    public bool OrbitClockwise { get; set; } = true;

    public const float MinRange = 50f;
    public const float MaxRange = 400f;
    public const float KeepAtRangeDeadband = 15f;

    public void Clear()
    {
        Mode = TargetTrackingMode.None;
        TrackedContactId = null;
    }
}
