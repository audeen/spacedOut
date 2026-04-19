using System;
using SpacedOut.Run;
using SpacedOut.State;

namespace SpacedOut.Meta;

/// <summary>
/// M7 v1 Sternenstaub-Formel: <c>Tiefe × Outcome-Multiplikator</c>.
/// Bewusst einfach gehalten — Boss-/Meilenstein-Boni kommen erst mit M8-Content.
/// </summary>
public static class StardustCalculator
{
    /// <summary>Punkte pro erreichter Tiefe (Basis vor Outcome-Multiplier).</summary>
    public const int PointsPerDepth = 5;

    public const float VictoryMultiplier = 2.0f;
    public const float DefeatMultiplier = 0.7f;
    public const float StrandedMultiplier = 0.5f;

    /// <summary>Returns 0 for <see cref="RunOutcome.Ongoing"/>; otherwise <c>round(depth × points × multiplier)</c>.</summary>
    public static int Calculate(RunStateData run, RunOutcome outcome, bool strandedDefeat)
    {
        if (outcome == RunOutcome.Ongoing) return 0;

        int reachedDepth = Math.Max(1, run.CurrentDepth);
        int basePts = reachedDepth * PointsPerDepth;
        float mult = outcome switch
        {
            RunOutcome.Victory => VictoryMultiplier,
            RunOutcome.Defeat when strandedDefeat => StrandedMultiplier,
            RunOutcome.Defeat => DefeatMultiplier,
            _ => 0f,
        };

        return (int)Math.Round(basePts * mult);
    }
}
