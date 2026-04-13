using SpacedOut.Run;

namespace SpacedOut.State;

/// <summary>Groups run/campaign state synced from RunController.</summary>
public class RunStateSnapshot
{
    public bool IsActive { get; set; }
    public bool ShowRunMap { get; set; }
    public RunStateData? State { get; set; }
    public RunDefinition? Definition { get; set; }
}
