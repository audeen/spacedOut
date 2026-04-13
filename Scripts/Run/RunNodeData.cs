using System.Collections.Generic;

namespace SpacedOut.Run;

public class RunNodeData
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public RunNodeType Type { get; set; }
    public int Depth { get; set; }
    public int RiskRating { get; set; }

    /// <summary>Normalized 0..1 horizontal position for map layout (set by factory).</summary>
    public float LayoutX { get; set; }

    /// <summary>Normalized 0..1 vertical position for map layout (set by factory).</summary>
    public float LayoutY { get; set; }

    public List<string> NextNodeIds { get; set; } = new();

    public List<string> RequiredFlags { get; set; } = new();
    public List<string> ForbiddenFlags { get; set; } = new();

    public List<string> GrantedFlagsOnSuccess { get; set; } = new();
    public List<string> GrantedFlagsOnPartial { get; set; } = new();
    public List<string> GrantedFlagsOnFailure { get; set; } = new();

    public Dictionary<string, int> ResourceChangesOnSuccess { get; set; } = new();
    public Dictionary<string, int> ResourceChangesOnPartial { get; set; } = new();
    public Dictionary<string, int> ResourceChangesOnFailure { get; set; } = new();

    public string? AssignedMissionId { get; set; }
    public string? StoryFunction { get; set; }
}
