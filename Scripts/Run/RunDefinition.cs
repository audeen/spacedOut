using System.Collections.Generic;

namespace SpacedOut.Run;

public class RunDefinition
{
    public string Id { get; set; } = "";
    public Dictionary<string, RunNodeData> Nodes { get; set; } = new();
    public string StartNodeId { get; set; } = "";
}
