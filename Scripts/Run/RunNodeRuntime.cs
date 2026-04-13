namespace SpacedOut.Run;

public class RunNodeRuntime
{
    public string NodeId { get; set; } = "";
    public RunNodeState State { get; set; }
    public NodeResolution Resolution { get; set; }
    public bool WasVisited { get; set; }
    public NodeKnowledgeState Knowledge { get; set; }
}
