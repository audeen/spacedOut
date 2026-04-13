namespace SpacedOut.State;

/// <summary>Groups route and navigation-related state.</summary>
public class NavigationState
{
    public RouteState Route { get; set; } = new();
}
