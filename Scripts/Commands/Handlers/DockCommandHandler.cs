using System;
using System.Text.Json;
using SpacedOut.Run;
using SpacedOut.State;

namespace SpacedOut.Commands.Handlers;

/// <summary>
/// M5: Station dock services — hull repair (SpareParts → Hull, Engineer) and
/// resource purchases and sales (Credits ↔ Fuel/SpareParts/ScienceData, Captain/Navigator/Engineer).
/// All actions require <see cref="MissionState.Docked"/> and a populated
/// <see cref="MissionState.Dock"/>.
/// </summary>
public class DockCommandHandler
{
    private readonly ICommandContext _ctx;

    public DockCommandHandler(ICommandContext ctx)
    {
        _ctx = ctx;
    }

    public bool Handle(string command, StationRole role, JsonElement data)
    {
        return command switch
        {
            CommandNames.DockRepairHull => HandleRepairHull(role, data),
            CommandNames.DockBuyResource => HandleBuyResource(role, data),
            CommandNames.DockSellResource => HandleSellResource(role, data),
            _ => false,
        };
    }

    private bool HandleRepairHull(StationRole role, JsonElement data)
    {
        if (role != StationRole.Engineer && role != StationRole.CaptainNav)
            return false;

        var m = _ctx.State.Mission;
        if (!m.Docked || m.Dock == null) return false;

        var run = _ctx.State.ActiveRunState;
        if (run == null) return false;

        int parts = data.TryGetProperty("parts", out var p) && p.TryGetInt32(out var pv) ? pv : 0;
        if (parts <= 0) return false;

        var ship = _ctx.State.Ship;
        // M7: respect MaxHullOverride from perks (e.g. perk_armor) as the repair cap.
        float hullCap = run.MaxHullOverride ?? 100f;
        if (ship.HullIntegrity >= hullCap) return false;

        run.Resources.TryGetValue(RunResourceIds.SpareParts, out int have);
        if (have < parts) return false;

        int hullPerPart = m.Dock.HullPerPart;
        float before = ship.HullIntegrity;
        float after = MathF.Min(hullCap, before + parts * hullPerPart);
        ship.HullIntegrity = after;
        run.Resources[RunResourceIds.SpareParts] = have - parts;

        _ctx.AddLog("Engineer",
            $"Reparatur: {parts} Ersatzteil{(parts == 1 ? "" : "e")} → +{after - before:F0} Hülle ({after:F0}%)");
        _ctx.EmitStateChanged();
        return true;
    }

    private bool HandleBuyResource(StationRole role, JsonElement data)
    {
        if (role != StationRole.CaptainNav && role != StationRole.Engineer)
            return false;

        var m = _ctx.State.Mission;
        if (!m.Docked || m.Dock == null) return false;

        var run = _ctx.State.ActiveRunState;
        if (run == null) return false;

        string resource = data.TryGetProperty("resource", out var r) ? r.GetString() ?? "" : "";
        int qty = data.TryGetProperty("qty", out var q) && q.TryGetInt32(out var qv) ? qv : 0;
        if (qty <= 0) return false;
        if (!m.Dock.IsBuyable(resource)) return false;

        int unitPrice = m.Dock.PriceFor(resource);
        if (unitPrice <= 0) return false;
        int cost = unitPrice * qty;

        run.Resources.TryGetValue(RunResourceIds.Credits, out int credits);
        if (credits < cost) return false;

        run.Resources[RunResourceIds.Credits] = credits - cost;
        run.Resources.TryGetValue(resource, out int have);
        run.Resources[resource] = have + qty;

        _ctx.AddLog(role.ToString(),
            $"Kauf: {qty}× {LocalizeResource(resource)} für {cost} Credits");
        _ctx.EmitStateChanged();
        return true;
    }

    private bool HandleSellResource(StationRole role, JsonElement data)
    {
        if (role != StationRole.CaptainNav && role != StationRole.Engineer)
            return false;

        var m = _ctx.State.Mission;
        if (!m.Docked || m.Dock == null) return false;

        var run = _ctx.State.ActiveRunState;
        if (run == null) return false;

        string resource = data.TryGetProperty("resource", out var r) ? r.GetString() ?? "" : "";
        int qty = data.TryGetProperty("qty", out var q) && q.TryGetInt32(out var qv) ? qv : 0;
        if (qty <= 0) return false;
        if (!m.Dock.IsSellable(resource)) return false;

        int unitPrice = m.Dock.SellPriceFor(resource);
        if (unitPrice <= 0) return false;
        int payout = unitPrice * qty;

        run.Resources.TryGetValue(resource, out int have);
        if (have < qty) return false;

        run.Resources[resource] = have - qty;
        run.Resources.TryGetValue(RunResourceIds.Credits, out int credits);
        run.Resources[RunResourceIds.Credits] = credits + payout;

        _ctx.AddLog(role.ToString(),
            $"Verkauf: {qty}× {LocalizeResource(resource)} für {payout} Credits");
        _ctx.EmitStateChanged();
        return true;
    }

    private static string LocalizeResource(string id) => id switch
    {
        RunResourceIds.Fuel => "Treibstoff",
        RunResourceIds.SpareParts => "Ersatzteile",
        RunResourceIds.ScienceData => "Forschungsdaten",
        _ => id,
    };
}
