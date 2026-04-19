using System;
using System.Text.Json;
using SpacedOut.Run;
using SpacedOut.State;

namespace SpacedOut.Commands.Handlers;

public class CommonCommandHandler
{
    private readonly ICommandContext _ctx;

    public CommonCommandHandler(ICommandContext ctx)
    {
        _ctx = ctx;
    }

    public bool Handle(string command, StationRole role, JsonElement data)
    {
        return command switch
        {
            CommandNames.SelectNode => HandleSelectNode(role, data),
            CommandNames.ResolveRunNode => HandleResolveRunNode(role, data),
            CommandNames.ScanRunNode => HandleScanRunNode(role, data),
            _ => false,
        };
    }

    private bool HandleSelectNode(StationRole role, JsonElement data)
    {
        if (role != StationRole.CaptainNav) return false;
        string nodeId = data.GetProperty("node_id").GetString() ?? "";
        if (string.IsNullOrEmpty(nodeId)) return false;

        _ctx.OnNodeSelected?.Invoke(nodeId);
        return true;
    }

    private bool HandleResolveRunNode(StationRole role, JsonElement data)
    {
        if (role != StationRole.CaptainNav) return false;
        string nodeId = data.GetProperty("node_id").GetString() ?? "";
        string resolution = data.GetProperty("resolution").GetString() ?? "";
        if (string.IsNullOrEmpty(nodeId) || string.IsNullOrEmpty(resolution)) return false;
        if (!Enum.TryParse<NodeResolution>(resolution, true, out _)) return false;

        _ctx.OnRunResolveRequested?.Invoke(nodeId, resolution);
        return true;
    }

    private bool HandleScanRunNode(StationRole role, JsonElement data)
    {
        if (role != StationRole.CaptainNav) return false;
        string nodeId = data.GetProperty("node_id").GetString() ?? "";
        if (string.IsNullOrEmpty(nodeId)) return false;

        _ctx.OnScanRunNodeRequested?.Invoke(nodeId);
        return true;
    }
}
