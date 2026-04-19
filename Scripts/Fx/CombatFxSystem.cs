using Godot;
using SpacedOut.Orchestration;
using SpacedOut.Shared;
using SpacedOut.State;

namespace SpacedOut.Fx;

/// <summary>
/// Consumes <see cref="CombatFxState.PendingShots"/> each frame and spawns a
/// <see cref="ShotFxInstance"/> child per shot. Resolves shooter/target ids
/// to world positions via <see cref="CoordinateMapper"/> (or the
/// <c>PlayerShipAnchor</c> when provided). Pure cosmetic — never mutates
/// gameplay state.
/// </summary>
public partial class CombatFxSystem : Node3D
{
    private GameState _state = null!;
    private MissionOrchestrator _orch = null!;
    private Node3D? _playerAnchor;
    private bool _initialized;

    public void Initialize(GameState state, MissionOrchestrator orch, Node3D? playerAnchor)
    {
        _state = state;
        _orch = orch;
        _playerAnchor = playerAnchor;
        _initialized = true;
    }

    public override void _Process(double delta)
    {
        if (!_initialized) return;

        var pending = _state.CombatFx.PendingShots;
        if (pending.Count == 0) return;

        foreach (var shot in pending)
        {
            if (!TryResolveWorldPos(shot.ShooterId, out var from)) continue;
            if (!TryResolveWorldPos(shot.TargetId, out var to)) continue;

            var fx = new ShotFxInstance { Name = $"Shot_{shot.ShooterId}_to_{shot.TargetId}" };
            AddChild(fx);
            fx.Configure(from, to, shot.Hit, shot.Visual);
        }

        pending.Clear();
    }

    private bool TryResolveWorldPos(string id, out Vector3 pos)
    {
        if (id == "player")
        {
            if (_playerAnchor != null && GodotObject.IsInstanceValid(_playerAnchor))
            {
                pos = _playerAnchor.GlobalPosition;
                return true;
            }
            pos = CoordinateMapper.MapToWorld(
                _state.Ship.PositionX, _state.Ship.PositionY, _state.Ship.PositionZ,
                _orch.LevelRadius);
            return true;
        }

        var c = _state.Contacts.Find(x => x.Id == id);
        if (c == null) { pos = default; return false; }

        pos = CoordinateMapper.MapToWorld(
            c.PositionX, c.PositionY, c.PositionZ, _orch.LevelRadius);
        return true;
    }
}
