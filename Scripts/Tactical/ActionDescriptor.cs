using System.Collections.Generic;

namespace SpacedOut.Tactical;

public class ActionDescriptor
{
    public string Id { get; init; } = "";
    public string Command { get; init; } = "";
    public string Label { get; init; } = "";
    /// <summary>Optional HTML title / native tooltip for web tactical buttons.</summary>
    public string Tooltip { get; init; } = "";
    public string Style { get; init; } = "primary";
    public string Type { get; init; } = "button";
    public bool Disabled { get; init; }
    public bool Active { get; init; }
    public float? Progress { get; init; }
    public string Group { get; init; } = "default";
    public bool Confirm { get; init; }
    public Dictionary<string, object>? Data { get; init; }
}
