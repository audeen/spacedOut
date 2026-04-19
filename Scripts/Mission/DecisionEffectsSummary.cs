using System;
using System.Collections.Generic;
using System.Linq;
using SpacedOut.Run;
using SpacedOut.State;

namespace SpacedOut.Mission;

/// <summary>
/// Builds a short German summary line from <see cref="DecisionEffects"/> for Kommandant toasts.
/// </summary>
public static class DecisionEffectsSummary
{
    public static string FormatGerman(DecisionEffects? e)
    {
        if (e == null) return "";

        var parts = new List<string>();

        if (e.ResourceDeltas != null)
        {
            foreach (var kv in e.ResourceDeltas.OrderBy(x => x.Key, StringComparer.Ordinal))
            {
                if (kv.Value == 0) continue;
                string label = ResourceLabel(kv.Key);
                string sign = kv.Value > 0 ? "+" : "−";
                int mag = Math.Abs(kv.Value);
                parts.Add($"{label} {sign}{mag}");
            }
        }

        if (Math.Abs(e.HullDelta) > 0.001f)
        {
            int h = (int)Math.Round(e.HullDelta);
            string sign = h > 0 ? "+" : "−";
            parts.Add($"Hülle {sign}{Math.Abs(h)}");
        }

        if (e.SkipSector)
            parts.Add("Sektor übersprungen");

        if (e.SystemEffects != null)
        {
            foreach (var fx in e.SystemEffects)
            {
                string sys = SystemLabel(fx.System);
                if (Math.Abs(fx.HeatDelta) > 0.001f)
                {
                    int dh = (int)Math.Round(fx.HeatDelta);
                    string sign = dh >= 0 ? "+" : "−";
                    parts.Add($"{sys} Wärme {sign}{Math.Abs(dh)}");
                }
                if (fx.SetStatus.HasValue)
                    parts.Add($"{sys} → {fx.SetStatus.Value}");
            }
        }

        if (e.SpawnAgents is { Count: > 0 })
        {
            int n = e.SpawnAgents.Count;
            parts.Add(n == 1 ? "Feindkontakt angekündigt" : $"{n} Feindkontakte angekündigt");
        }

        return parts.Count == 0 ? "" : string.Join(" · ", parts);
    }

    private static string ResourceLabel(string id) => id switch
    {
        RunResourceIds.Fuel => "Treibstoff",
        RunResourceIds.ScienceData => "Forschung",
        RunResourceIds.SpareParts => "Ersatzteile",
        RunResourceIds.Credits => "Credits",
        _ => id,
    };

    private static string SystemLabel(SystemId id) => id switch
    {
        SystemId.Drive => "Antrieb",
        SystemId.Shields => "Schilde",
        SystemId.Sensors => "Sensoren",
        SystemId.Weapons => "Waffen",
        _ => id.ToString(),
    };
}
