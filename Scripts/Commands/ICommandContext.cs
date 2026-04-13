using System;
using SpacedOut.Sector;
using SpacedOut.State;

namespace SpacedOut.Commands;

/// <summary>
/// Shared operations available to all command handlers.
/// Implemented by <see cref="CommandProcessor"/> to bridge handler logic with Godot signals.
/// </summary>
public interface ICommandContext
{
    GameState State { get; }
    SectorData? CurrentSector { get; }
    void AddLog(string source, string message);
    void EmitStateChanged();

    Action<string>? OnNodeSelected { get; set; }
    Action<string, string>? OnRunResolveRequested { get; set; }
}
