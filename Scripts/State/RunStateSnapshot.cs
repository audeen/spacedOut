using System.Text.Json.Serialization;
using SpacedOut.Run;

namespace SpacedOut.State;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum RunOutcome { Ongoing, Victory, Defeat }

/// <summary>Groups run/campaign state synced from RunController.</summary>
public class RunStateSnapshot
{
    public bool IsActive { get; set; }
    public bool ShowRunMap { get; set; }
    public RunStateData? State { get; set; }
    public RunDefinition? Definition { get; set; }

    /// <summary>Current run result state. Flips to Victory/Defeat when the run ends
    /// (END-node success / Hull 0). Used by HUD to render the run-end overlay.</summary>
    public RunOutcome Outcome { get; set; } = RunOutcome.Ongoing;

    /// <summary>Session-local counter. Run 1 boots with the tutorial at START; subsequent
    /// runs in the same session replace START with a procedural template.</summary>
    [JsonIgnore]
    public int RunsStartedThisSession { get; set; }

    /// <summary>True while the main menu overlay is visible and no run should be active.</summary>
    public bool ShowMainMenu { get; set; }

    /// <summary>When <see cref="Outcome"/> is Defeat: true = stranded (no affordable node), false = hull destroyed.</summary>
    public bool StrandedDefeat { get; set; }
}
