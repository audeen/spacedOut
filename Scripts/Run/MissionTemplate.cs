using System.Collections.Generic;

namespace SpacedOut.Run;

/// <summary>Authoring data for a run-node mission slot (no gameplay logic).</summary>
public class MissionTemplate
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public MissionType Type { get; set; }
    public string Description { get; set; } = "";

    /// <summary>story / generic</summary>
    public string Category { get; set; } = "generic";

    public string? StoryFunction { get; set; }
    public int Risk { get; set; }
    public string Reward { get; set; } = "";

    public List<string> PossibleFlags { get; set; } = new();
    public List<string> Objectives { get; set; } = new();
    public string Twist { get; set; } = "";

    /// <summary>
    /// Director hints: free-form labels the <see cref="IRunDirector"/> uses to pick templates by intent
    /// (e.g. <c>"breather"</c>, <c>"pressure"</c>, <c>"reward"</c>, <c>"safe_haven"</c>).
    /// Authoring stays free; unknown tags are ignored.
    /// </summary>
    public List<string> Tags { get; set; } = new();
}
