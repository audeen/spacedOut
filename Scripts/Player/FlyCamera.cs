using Godot;

namespace SpacedOut.Player;

/// <summary>
/// Free-fly debug camera.
///
/// Controls once activated:
///   WASD         – move forward/back/left/right
///   Q / E        – down / up (Space also moves up)
///   Shift        – speed boost (6x)
///   Ctrl         – precision (0.2x) for fine framing
///   RMB (hold)   – mouse look; releasing the button frees the cursor again,
///                  so HUD buttons stay clickable without toggling modes.
///   Mouse wheel  – adjust base move speed (clamped to [5 .. 2000] m/s).
///
/// Starts inactive – call <see cref="Activate"/> from the controlling code
/// (LevelGenerator in standalone mode, GameManager via the debug panel in
/// integrated mode).
/// </summary>
public partial class FlyCamera : Camera3D
{
    // Base is calibrated for sector-scale travel (levels are hundreds of metres wide).
    private const float BaseMoveSpeedDefault = 80f;
    private const float FastMultiplier = 6f;
    private const float SlowMultiplier = 0.2f;
    private const float Sensitivity = 0.003f;
    private const float MinBaseSpeed = 5f;
    private const float MaxBaseSpeed = 2000f;

    private float _moveSpeed = BaseMoveSpeedDefault;
    private float _pitch;
    private float _yaw;
    private bool _looking;
    private bool _active;

    public bool IsActive => _active;
    public float CurrentBaseSpeed => _moveSpeed;

    /// <summary>
    /// Uses <c>_Input</c> (not <c>_UnhandledInput</c>) because the HUD overlay
    /// and debug panel consume mouse events at the GUI layer before they ever
    /// reach unhandled input. We only react to RMB / mouse motion / wheel and
    /// mark those events handled, so left-click on HUD buttons still works.
    /// </summary>
    public override void _Input(InputEvent @event)
    {
        if (!_active) return;

        if (@event is InputEventMouseButton mb)
        {
            if (mb.ButtonIndex == MouseButton.Right)
            {
                SetLooking(mb.Pressed);
                GetViewport().SetInputAsHandled();
                return;
            }

            if (mb.Pressed && mb.ButtonIndex == MouseButton.WheelUp)
            {
                _moveSpeed = Mathf.Min(_moveSpeed * 1.2f, MaxBaseSpeed);
                GetViewport().SetInputAsHandled();
                return;
            }

            if (mb.Pressed && mb.ButtonIndex == MouseButton.WheelDown)
            {
                _moveSpeed = Mathf.Max(_moveSpeed * 0.8f, MinBaseSpeed);
                GetViewport().SetInputAsHandled();
                return;
            }
        }

        if (@event is InputEventMouseMotion motion && _looking)
        {
            _yaw -= motion.Relative.X * Sensitivity;
            _pitch -= motion.Relative.Y * Sensitivity;
            _pitch = Mathf.Clamp(_pitch, -Mathf.Pi / 2f + 0.05f, Mathf.Pi / 2f - 0.05f);
            Rotation = new Vector3(_pitch, _yaw, 0);
            GetViewport().SetInputAsHandled();
        }
    }

    public override void _Process(double delta)
    {
        if (!_active) return;

        // Don't steal WASD while typing in any text input.
        var focusOwner = GetViewport().GuiGetFocusOwner();
        if (focusOwner is LineEdit or TextEdit) return;

        var velocity = Vector3.Zero;
        var basis = GlobalTransform.Basis;

        if (Input.IsKeyPressed(Key.W)) velocity -= basis.Z;
        if (Input.IsKeyPressed(Key.S)) velocity += basis.Z;
        if (Input.IsKeyPressed(Key.A)) velocity -= basis.X;
        if (Input.IsKeyPressed(Key.D)) velocity += basis.X;
        if (Input.IsKeyPressed(Key.E) || Input.IsKeyPressed(Key.Space)) velocity += Vector3.Up;
        if (Input.IsKeyPressed(Key.Q)) velocity += Vector3.Down;

        float speed = _moveSpeed;
        if (Input.IsKeyPressed(Key.Shift)) speed *= FastMultiplier;
        if (Input.IsKeyPressed(Key.Ctrl))  speed *= SlowMultiplier;

        if (velocity.LengthSquared() > 0)
            velocity = velocity.Normalized() * speed;

        Position += velocity * (float)delta;
    }

    public void Activate()
    {
        _active = true;
        Current = true;
        _pitch = Rotation.X;
        _yaw = Rotation.Y;
        // Start with free cursor so the HUD / debug panel stays usable.
        SetLooking(false);
    }

    public void Deactivate()
    {
        _active = false;
        Current = false;
        SetLooking(false);
    }

    public void Teleport(Vector3 position, Vector3 lookTarget)
    {
        Position = position;
        var dir = lookTarget - position;
        if (dir.LengthSquared() < 0.01f)
        {
            _yaw = 0;
            _pitch = 0;
        }
        else
        {
            dir = dir.Normalized();
            _yaw = Mathf.Atan2(-dir.X, -dir.Z);
            _pitch = Mathf.Asin(Mathf.Clamp(dir.Y, -0.99f, 0.99f));
        }

        Rotation = new Vector3(_pitch, _yaw, 0);
    }

    // ── internals ───────────────────────────────────────────────────

    private void SetLooking(bool looking)
    {
        _looking = looking;
        Input.MouseMode = looking
            ? Input.MouseModeEnum.Captured
            : Input.MouseModeEnum.Visible;
    }
}
