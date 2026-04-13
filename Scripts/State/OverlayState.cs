using System.Collections.Generic;

namespace SpacedOut.State;

/// <summary>Groups overlay and main-screen display flags.</summary>
public class OverlayState
{
    public List<OverlayRequest> Items { get; set; } = new();
    public bool ShowStarMap { get; set; }
    public bool ShowTactical { get; set; }
}
