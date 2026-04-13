using System;
using System.Text.Json;
using SpacedOut.Run;
using SpacedOut.State;

namespace SpacedOut.Commands.Handlers;

public class CaptainCommandHandler
{
    private readonly ICommandContext _ctx;

    public CaptainCommandHandler(ICommandContext ctx)
    {
        _ctx = ctx;
    }

    public bool Handle(string command, JsonElement data, StationRole role)
    {
        return command switch
        {
            CommandNames.ApproveOverlay => HandleApproveOverlay(data),
            CommandNames.DismissOverlay => HandleDismissOverlay(data),
            CommandNames.SetMissionPriority => HandleSetMissionPriority(data),
            CommandNames.ResolveDecision => HandleResolveDecision(data),
            CommandNames.RequestStatus => HandleRequestStatus(role, data),
            _ => false,
        };
    }

    private bool HandleApproveOverlay(JsonElement data)
    {
        string overlayId = data.GetProperty("overlay_id").GetString() ?? "";
        var overlay = _ctx.State.Overlays.Find(o => o.Id == overlayId);
        if (overlay == null) return false;

        overlay.ApprovedByCaptain = true;
        _ctx.AddLog("Captain", $"Overlay genehmigt: {overlay.Text}");
        _ctx.EmitStateChanged();
        return true;
    }

    private bool HandleDismissOverlay(JsonElement data)
    {
        string overlayId = data.GetProperty("overlay_id").GetString() ?? "";
        var overlay = _ctx.State.Overlays.Find(o => o.Id == overlayId);
        if (overlay == null) return false;

        overlay.Dismissed = true;
        _ctx.EmitStateChanged();
        return true;
    }

    private bool HandleSetMissionPriority(JsonElement data)
    {
        string priority = data.GetProperty("priority").GetString() ?? "";
        _ctx.AddLog("Captain", $"Missionspriorität gesetzt: {priority}");
        _ctx.EmitStateChanged();
        return true;
    }

    private bool HandleResolveDecision(JsonElement data)
    {
        string decisionId = data.GetProperty("decision_id").GetString() ?? "";
        string optionId = data.GetProperty("option_id").GetString() ?? "";

        var decision = _ctx.State.Mission.PendingDecisions.Find(d => d.Id == decisionId);
        if (decision == null || decision.IsResolved) return false;

        decision.IsResolved = true;
        decision.ChosenOptionId = optionId;
        _ctx.State.Mission.CompletedDecisions.Add(decisionId);

        var option = decision.Options.Find(o => o.Id == optionId);
        _ctx.AddLog("Captain", $"Entscheidung: {decision.Title} → {option?.Label ?? optionId}");
        _ctx.EmitStateChanged();
        return true;
    }

    private bool HandleRequestStatus(StationRole fromRole, JsonElement data)
    {
        string target = data.GetProperty("target").GetString() ?? "";
        _ctx.AddLog(fromRole.ToString(), $"Status angefordert: {target}");
        return true;
    }
}
