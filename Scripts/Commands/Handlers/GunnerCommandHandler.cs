using System;
using System.Text.Json;
using SpacedOut.Poi;
using SpacedOut.Shared;
using SpacedOut.State;

namespace SpacedOut.Commands.Handlers;

public class GunnerCommandHandler
{
    private readonly ICommandContext _ctx;

    public GunnerCommandHandler(ICommandContext ctx)
    {
        _ctx = ctx;
    }

    public bool Handle(string command, StationRole role, JsonElement data)
    {
        return command switch
        {
            CommandNames.SelectTarget => HandleSelectTarget(data),
            CommandNames.Fire => HandleFire(),
            CommandNames.CeaseFire => HandleCeaseFire(),
            CommandNames.SetWeaponMode => HandleSetWeaponMode(data),
            CommandNames.SetDefensiveMode => HandleSetDefensiveMode(data),
            CommandNames.SetAutofire => HandleSetAutofire(data),
            CommandNames.SetToolMode => HandleSetToolMode(data),
            CommandNames.DrillTarget => HandleDrillTarget(data),
            _ => false,
        };
    }

    private bool HandleSelectTarget(JsonElement data)
    {
        string contactId = data.GetProperty("contact_id").GetString() ?? "";
        var contact = _ctx.State.Contacts.Find(c => c.Id == contactId);
        if (contact == null) return false;
        if (contact.Discovery != DiscoveryState.Scanned) return false;
        if (contact.IsDestroyed) return false;

        var gunner = _ctx.State.Gunner;
        if (gunner.Tool == ToolMode.Mining)
        {
            if (!GunnerContactRules.IsDrillablePoiForGunnerList(contact)) return false;
        }
        else if (!GunnerContactRules.IsSelectableForCombat(contact))
        {
            return false;
        }

        if (gunner.SelectedTargetId == contactId) return true;

        gunner.SelectedTargetId = contactId;
        gunner.TargetLockProgress = 0;
        _ctx.AddLog("Gunner", $"Ziel erfasst: {contact.DisplayName}");
        _ctx.EmitStateChanged();
        return true;
    }

    private bool HandleFire()
    {
        if (!GunnerFireAction.TryExecuteFire(_ctx.State))
            return false;

        _ctx.EmitStateChanged();
        return true;
    }

    private bool HandleCeaseFire()
    {
        var gunner = _ctx.State.Gunner;
        gunner.SelectedTargetId = null;
        gunner.TargetLockProgress = 0;
        gunner.IsAutofire = false;
        _ctx.AddLog("Gunner", "Feuer eingestellt");
        _ctx.EmitStateChanged();
        return true;
    }

    private bool HandleSetWeaponMode(JsonElement data)
    {
        string modeStr = data.GetProperty("mode").GetString() ?? "";
        if (!Enum.TryParse<WeaponMode>(modeStr, true, out var mode)) return false;

        _ctx.State.Gunner.Mode = mode;
        _ctx.State.Gunner.TargetLockProgress = 0;
        _ctx.AddLog("Gunner", $"Waffenmodus: {mode}");
        _ctx.EmitStateChanged();
        return true;
    }

    private bool HandleSetDefensiveMode(JsonElement data)
    {
        bool enabled = data.GetProperty("enabled").GetBoolean();
        _ctx.State.Gunner.IsDefensiveMode = enabled;
        _ctx.AddLog("Gunner", enabled ? "Defensivfeuer aktiviert" : "Defensivfeuer deaktiviert");
        _ctx.EmitStateChanged();
        return true;
    }

    private bool HandleSetAutofire(JsonElement data)
    {
        bool enabled = data.GetProperty("enabled").GetBoolean();
        _ctx.State.Gunner.IsAutofire = enabled;
        _ctx.AddLog("Gunner", enabled ? "Autofeuer: AN" : "Autofeuer: AUS");
        _ctx.EmitStateChanged();
        return true;
    }

    private bool HandleSetToolMode(JsonElement data)
    {
        string modeStr = data.GetProperty("mode").GetString() ?? "";
        if (!Enum.TryParse<ToolMode>(modeStr, true, out var mode)) return false;

        var gunner = _ctx.State.Gunner;
        gunner.Tool = mode;

        if (mode == ToolMode.Mining)
        {
            gunner.SelectedTargetId = null;
            gunner.TargetLockProgress = 0;
            gunner.IsAutofire = false;
        }
        else
        {
            StopDrilling(gunner);
        }

        _ctx.AddLog("Gunner", mode == ToolMode.Mining
            ? "Werkzeugmodus: MINING (Bohrer aktiv)"
            : "Werkzeugmodus: COMBAT (Waffen aktiv)");
        _ctx.EmitStateChanged();
        return true;
    }

    private bool HandleDrillTarget(JsonElement data)
    {
        var gunner = _ctx.State.Gunner;
        if (gunner.Tool != ToolMode.Mining) return false;

        string contactId = data.GetProperty("contact_id").GetString() ?? "";
        var contact = _ctx.State.Contacts.Find(c => c.Id == contactId);
        if (contact == null) return false;
        if (string.IsNullOrEmpty(contact.PoiType)) return false;
        if (contact.PoiPhase != PoiPhase.Analyzed) return false;

        var bp = PoiBlueprintCatalog.GetOrNull(contact.PoiType);
        if (bp == null || !bp.RequiresDrill) return false;

        float dx = contact.PositionX - _ctx.State.Ship.PositionX;
        float dy = contact.PositionY - _ctx.State.Ship.PositionY;
        float dist = MathF.Sqrt(dx * dx + dy * dy);
        if (dist > bp.DrillRange) return false;

        StopDrilling(gunner);

        gunner.DrillTargetId = contactId;
        contact.PoiDrilling = true;
        _ctx.AddLog("Gunner", $"Bohrung gestartet: {contact.DisplayName}");
        _ctx.EmitStateChanged();
        return true;
    }

    private void StopDrilling(GunnerState gunner)
    {
        if (string.IsNullOrEmpty(gunner.DrillTargetId)) return;
        var prev = _ctx.State.Contacts.Find(c => c.Id == gunner.DrillTargetId);
        if (prev != null) prev.PoiDrilling = false;
        gunner.DrillTargetId = null;
    }
}
