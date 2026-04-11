using Godot;

namespace SpacedOut.Player;

/// <summary>
/// Free-fly debug camera. Starts inactive – call <see cref="Activate"/>
/// from the controlling code (LevelGenerator in standalone mode,
/// GameManager via the debug panel in integrated mode).
/// </summary>
public partial class FlyCamera : Camera3D
{
    private float _moveSpeed = 30f;
    private const float FastMultiplier = 3f;
    private const float Sensitivity = 0.003f;

    private float _pitch;
    private float _yaw;
    private bool _captured;
    private bool _active;

    public bool IsActive => _active;

    public override void _Input(InputEvent @event)
    {
        if (!_active) return;

        if (@event is InputEventKey { Pressed: true, Echo: false } key
            && key.Keycode == Key.Tab)
        {
            _captured = !_captured;
            Input.MouseMode = _captured
                ? Input.MouseModeEnum.Captured
                : Input.MouseModeEnum.Visible;
            GetViewport().SetInputAsHandled();
        }
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (!_active) return;

        if (@event is InputEventMouseMotion motion && _captured)
        {
            _yaw -= motion.Relative.X * Sensitivity;
            _pitch -= motion.Relative.Y * Sensitivity;
            _pitch = Mathf.Clamp(_pitch, -Mathf.Pi / 2f + 0.05f, Mathf.Pi / 2f - 0.05f);
            Rotation = new Vector3(_pitch, _yaw, 0);
        }

        if (@event is InputEventMouseButton { Pressed: true } mb)
        {
            if (mb.ButtonIndex == MouseButton.WheelUp)
                _moveSpeed = Mathf.Min(_moveSpeed * 1.15f, 500f);
            else if (mb.ButtonIndex == MouseButton.WheelDown)
                _moveSpeed = Mathf.Max(_moveSpeed * 0.85f, 5f);
        }
    }

    public override void _Process(double delta)
    {
        if (!_active) return;

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

        if (velocity.LengthSquared() > 0)
            velocity = velocity.Normalized() * speed;

        Position += velocity * (float)delta;
    }

    public void Activate()
    {
        _active = true;
        Current = true;
        _captured = true;
        Input.MouseMode = Input.MouseModeEnum.Captured;
        _pitch = Rotation.X;
        _yaw = Rotation.Y;
    }

    public void Deactivate()
    {
        _active = false;
        Current = false;
        if (_captured)
        {
            _captured = false;
            Input.MouseMode = Input.MouseModeEnum.Visible;
        }
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
}
