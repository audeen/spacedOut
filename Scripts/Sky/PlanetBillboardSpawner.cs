using System;
using Godot;

namespace SpacedOut.Sky;

/// <summary>
/// Spawns 0–3 planet billboards as children of the <see cref="SpaceSkyController"/>.
/// Because the controller node follows the active camera (see
/// <c>GameManager.UpdateBridgeCamera</c>), planets placed here appear to live
/// at a fixed direction from the camera – i.e. in the distant sky.
///
/// Each planet is a QuadMesh facing the controller's origin (which coincides
/// with the camera), rendered with <c>Assets/shaders/sky/planet_billboard.gdshader</c>.
/// </summary>
public partial class PlanetBillboardSpawner : Node3D
{
    private const string ShaderPath = "res://Assets/shaders/sky/planet_billboard.gdshader";

    /// <summary>
    /// Fake distance at which planets sit relative to the camera. Must stay
    /// below <see cref="Camera3D.Far"/> (see Scenes/Main.tscn and LevelGenScene.tscn).
    /// Quad size is derived from <see cref="PlanetPalette.AngularSizeMin"/> so
    /// apparent size stays correct at any distance.
    /// </summary>
    private const float PlanetDistance = 8000f;

    private Shader? _shader;

    public override void _Ready()
    {
        _shader = ResourceLoader.Exists(ShaderPath) ? GD.Load<Shader>(ShaderPath) : null;
        if (_shader == null)
            GD.PrintErr($"[Sky] PlanetBillboardSpawner: shader not found at {ShaderPath}.");
    }

    public void Apply(int seed, SkyboxProfile profile)
    {
        foreach (var child in GetChildren())
            child.QueueFree();

        if (_shader == null) return;
        if (profile.PlanetPalettes.Length == 0) return;
        if (profile.PlanetCountMax <= 0) return;

        // Use a seed branch distinct from the sky shader's so identical seeds
        // don't align planet directions with nebula hotspots by accident.
        var rng = new Random(unchecked(seed * 397) ^ 0x5EED_F00D);

        int min = Math.Max(0, profile.PlanetCountMin);
        int max = Math.Max(min, profile.PlanetCountMax);
        int count = rng.Next(min, max + 1);

        // Pick a primary sun direction for lighting. If the biome has no suns,
        // fall back to a neutral "top-right" lamp so planets aren't black discs.
        Vector3 sunDir = PickSunDirection(seed, profile);

        for (int i = 0; i < count; i++)
        {
            var palette = profile.PlanetPalettes[rng.Next(profile.PlanetPalettes.Length)];
            SpawnPlanet(rng, palette, sunDir);
        }
    }

    private void SpawnPlanet(Random rng, PlanetPalette palette, Vector3 sunDir)
    {
        Vector3 dir = SpaceSkyController.RandomUnit(rng);

        float angularSize = Mathf.Lerp(
            palette.AngularSizeMin,
            palette.AngularSizeMax,
            (float)rng.NextDouble());
        // Quad diameter = 2 * distance * tan(angularSize/2).
        float size = 2f * PlanetDistance * Mathf.Tan(angularSize * 0.5f);

        var mesh = new QuadMesh { Size = new Vector2(size, size) };

        var material = new ShaderMaterial { Shader = _shader };
        material.SetShaderParameter("surface_color_a", palette.SurfaceA);
        material.SetShaderParameter("surface_color_b", palette.SurfaceB);
        material.SetShaderParameter("atmosphere_color", palette.Atmosphere);
        material.SetShaderParameter("atmosphere_strength", palette.AtmosphereStrength);
        material.SetShaderParameter("sun_direction_world", sunDir);
        material.SetShaderParameter("detail_seed", (float)(rng.NextDouble() * 100.0));
        material.SetShaderParameter("detail_scale", 1.8f + (float)(rng.NextDouble() * 1.5));
        material.SetShaderParameter("detail_contrast", 1.3f + (float)(rng.NextDouble() * 0.9));

        var mi = new MeshInstance3D
        {
            Name = $"Planet_{GetChildCount()}",
            Mesh = mesh,
            MaterialOverride = material,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            ExtraCullMargin = size,
        };

        AddChild(mi);

        mi.GlobalPosition = GlobalPosition + dir * PlanetDistance;
        // Orient the quad so its +Z (mesh normal) points back at the camera
        // position (= our own origin while we follow the camera).
        mi.LookAt(GlobalPosition, ChoosePlanetUp(dir));
    }

    private static Vector3 ChoosePlanetUp(Vector3 forward)
    {
        // LookAt needs an up that isn't parallel to the forward direction.
        return Mathf.Abs(forward.Dot(Vector3.Up)) > 0.95f
            ? Vector3.Forward
            : Vector3.Up;
    }

    private static Vector3 PickSunDirection(int seed, SkyboxProfile profile)
    {
        // Keep sun direction deterministic from the sector seed but off-axis
        // from the default so planets have a visible terminator.
        var rng = new Random(unchecked(seed ^ 0x5A11_B0B5));
        if (profile.Suns.Length > 0)
        {
            // Share the first sky-sun's direction (re-rolled here because the
            // sky shader rolls its own seed; we just want a plausible direction).
            return SpaceSkyController.RandomUnit(rng);
        }
        return new Vector3(0.6f, 0.5f, 0.4f).Normalized();
    }
}
