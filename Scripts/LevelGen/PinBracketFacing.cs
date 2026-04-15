using Godot;

namespace SpacedOut.LevelGen;

/// <summary>
/// Orients the Godot <see cref="TorusMesh"/> so the camera looks through the hole (ring reads as an “O”).
/// In Godot the major ring lies in the XZ plane and the hole follows local <b>+Y</b>; <see cref="LookAt"/>
/// aligns -Z to the camera and leaves the ring edge-on for a horizontal view.
/// Here local <b>Y</b> is set to the direction object → camera so the screen shows a full ring around the target.
/// </summary>
public partial class PinBracketFacing : Node3D
{
    public override void _Process(double delta)
    {
        var cam = GetViewport()?.GetCamera3D();
        if (cam == null) return;

        Vector3 pos = GlobalPosition;
        Vector3 toCam = cam.GlobalPosition - pos;
        if (toCam.LengthSquared() < 1e-10f)
            return;
        toCam = toCam.Normalized();

        // Torus hole axis = basis Y; must match view axis for a circular silhouette.
        Vector3 y = toCam;
        Vector3 x = Vector3.Up.Cross(y);
        if (x.LengthSquared() < 1e-12f)
            x = Vector3.Forward.Cross(y);
        if (x.LengthSquared() < 1e-12f)
            x = Vector3.Right;
        x = x.Normalized();
        Vector3 z = x.Cross(y).Normalized();

        GlobalTransform = new Transform3D(new Basis(x, y, z), pos);
    }
}
