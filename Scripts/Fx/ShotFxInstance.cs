using Godot;
using SpacedOut.State;

namespace SpacedOut.Fx;

/// <summary>
/// Short-lived 3D weapon effect for a single <see cref="ShotEvent"/>. Builds its own
/// meshes programmatically (no .tscn dependency) and auto-frees after the shot/impact
/// animation completes. Purely cosmetic — independent from the simulation's hit math.
/// </summary>
public partial class ShotFxInstance : Node3D
{
    private bool _hit;
    private Vector3 _impactPos;
    private WeaponVisualKind _kind;

    public void Configure(Vector3 from, Vector3 to, bool hit, WeaponVisualKind kind)
    {
        _hit = hit;
        _kind = kind;

        // Miss: deflect the endpoint so the shot visibly flies past the target.
        if (!hit)
        {
            var dir = to - from;
            if (dir.LengthSquared() > 1e-3f) dir = dir.Normalized();
            else dir = Vector3.Forward;

            var perp = dir.Cross(Vector3.Up);
            if (perp.LengthSquared() < 1e-4f) perp = Vector3.Right;
            perp = perp.Normalized();

            float sign = GD.Randf() > 0.5f ? 1f : -1f;
            float offset = (float)GD.RandRange(3.0, 6.0) * sign;
            to += perp * offset + Vector3.Up * (float)GD.RandRange(-1.5, 1.5);
        }

        _impactPos = to;

        SpawnMuzzleFlash(from);

        switch (kind)
        {
            case WeaponVisualKind.LaserBeam:
                BuildLaserBeam(from, to);
                break;
            case WeaponVisualKind.PlasmaBolt:
                BuildPlasmaBolt(from, to);
                break;
            case WeaponVisualKind.KineticTracer:
            default:
                BuildKineticTracer(from, to);
                break;
        }
    }

    private Color GetBeamColor() => _kind switch
    {
        WeaponVisualKind.LaserBeam => new Color(0.35f, 1f, 0.55f),
        WeaponVisualKind.PlasmaBolt => new Color(1f, 0.4f, 0.9f),
        WeaponVisualKind.KineticTracer => new Color(1f, 0.85f, 0.3f),
        _ => new Color(1f, 1f, 1f),
    };

    // ── Muzzle / impact helpers ─────────────────────────────────────

    private void SpawnMuzzleFlash(Vector3 pos)
    {
        var light = new OmniLight3D
        {
            LightColor = GetBeamColor(),
            LightEnergy = 3f,
            OmniRange = 10f,
            OmniAttenuation = 2f,
            ShadowEnabled = false,
        };
        AddChild(light);
        light.GlobalPosition = pos;

        var tw = CreateTween();
        tw.TweenProperty(light, "light_energy", 0f, 0.08f);
        tw.TweenCallback(Callable.From(() =>
        {
            if (GodotObject.IsInstanceValid(light)) light.QueueFree();
        }));
    }

    private void SpawnImpactFlash(Vector3 pos)
    {
        var impactColor = new Color(1f, 0.7f, 0.25f);

        var light = new OmniLight3D
        {
            LightColor = impactColor,
            LightEnergy = 4f,
            OmniRange = 14f,
            OmniAttenuation = 2f,
            ShadowEnabled = false,
        };
        AddChild(light);
        light.GlobalPosition = pos;

        var sparkMesh = new SphereMesh
        {
            Radius = 0.8f,
            Height = 1.6f,
            RadialSegments = 10,
            Rings = 5,
        };
        var sparkMat = new StandardMaterial3D
        {
            AlbedoColor = new Color(1f, 0.85f, 0.35f, 1f),
            EmissionEnabled = true,
            Emission = new Color(1f, 0.6f, 0.2f),
            EmissionEnergyMultiplier = 6f,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            BlendMode = BaseMaterial3D.BlendModeEnum.Add,
        };
        var spark = new MeshInstance3D { Mesh = sparkMesh, MaterialOverride = sparkMat };
        AddChild(spark);
        spark.GlobalPosition = pos;

        var tw = CreateTween();
        tw.TweenProperty(light, "light_energy", 0f, 0.22f);
        tw.Parallel().TweenProperty(sparkMat, "albedo_color:a", 0f, 0.22f);
        tw.Parallel().TweenProperty(sparkMat, "emission_energy_multiplier", 0f, 0.22f);
        tw.Parallel().TweenProperty(spark, "scale", Vector3.One * 2.2f, 0.22f);
        tw.TweenCallback(Callable.From(() =>
        {
            if (GodotObject.IsInstanceValid(light)) light.QueueFree();
            if (GodotObject.IsInstanceValid(spark)) spark.QueueFree();
        }));
    }

    // ── Beam / bolt / tracer builders ───────────────────────────────

    /// <summary>Continuous beam that flashes instantly and fades over 0.12 s.</summary>
    private void BuildLaserBeam(Vector3 from, Vector3 to)
    {
        float len = from.DistanceTo(to);
        if (len < 0.01f) { ScheduleFinalCleanup(0.05f); return; }

        var box = new BoxMesh { Size = new Vector3(0.45f, 0.45f, len) };
        var mat = new StandardMaterial3D
        {
            AlbedoColor = GetBeamColor() with { A = 0.9f },
            EmissionEnabled = true,
            Emission = GetBeamColor(),
            EmissionEnergyMultiplier = 7f,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            BlendMode = BaseMaterial3D.BlendModeEnum.Add,
        };
        var mi = new MeshInstance3D { Mesh = box, MaterialOverride = mat };
        AddChild(mi);

        var mid = (from + to) * 0.5f;
        mi.GlobalPosition = mid;
        mi.LookAt(to, Vector3.Up);

        if (_hit) SpawnImpactFlash(_impactPos);

        var tw = CreateTween();
        tw.TweenProperty(mat, "albedo_color:a", 0f, 0.12f);
        tw.Parallel().TweenProperty(mat, "emission_energy_multiplier", 0f, 0.12f);
        tw.TweenCallback(Callable.From(() =>
        {
            if (GodotObject.IsInstanceValid(mi)) mi.QueueFree();
        }));

        ScheduleFinalCleanup(0.5f);
    }

    /// <summary>Glowing sphere that travels from muzzle to target over 0.35 s.</summary>
    private void BuildPlasmaBolt(Vector3 from, Vector3 to)
    {
        var mesh = new SphereMesh
        {
            Radius = 0.9f,
            Height = 1.8f,
            RadialSegments = 14,
            Rings = 8,
        };
        var mat = new StandardMaterial3D
        {
            AlbedoColor = GetBeamColor() with { A = 1f },
            EmissionEnabled = true,
            Emission = GetBeamColor(),
            EmissionEnergyMultiplier = 5f,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            BlendMode = BaseMaterial3D.BlendModeEnum.Add,
        };
        var mi = new MeshInstance3D { Mesh = mesh, MaterialOverride = mat };
        AddChild(mi);

        var light = new OmniLight3D
        {
            LightColor = GetBeamColor(),
            LightEnergy = 2.5f,
            OmniRange = 10f,
            ShadowEnabled = false,
        };
        AddChild(light);

        mi.GlobalPosition = from;
        light.GlobalPosition = from;

        const float flight = 0.35f;
        var tw = CreateTween();
        tw.TweenProperty(mi, "global_position", to, flight);
        tw.Parallel().TweenProperty(light, "global_position", to, flight);
        tw.TweenCallback(Callable.From(() =>
        {
            if (_hit) SpawnImpactFlash(_impactPos);
            if (GodotObject.IsInstanceValid(mi)) mi.QueueFree();
            if (GodotObject.IsInstanceValid(light)) light.QueueFree();
        }));

        ScheduleFinalCleanup(flight + 0.5f);
    }

    /// <summary>Small streak that flies from muzzle to target in 0.25 s.</summary>
    private void BuildKineticTracer(Vector3 from, Vector3 to)
    {
        var dir = to - from;
        if (dir.LengthSquared() < 1e-3f) { ScheduleFinalCleanup(0.05f); return; }
        dir = dir.Normalized();

        const float tracerLen = 3.5f;
        var box = new BoxMesh { Size = new Vector3(0.15f, 0.15f, tracerLen) };
        var mat = new StandardMaterial3D
        {
            AlbedoColor = GetBeamColor() with { A = 0.95f },
            EmissionEnabled = true,
            Emission = GetBeamColor(),
            EmissionEnergyMultiplier = 4f,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            BlendMode = BaseMaterial3D.BlendModeEnum.Add,
        };
        var mi = new MeshInstance3D { Mesh = box, MaterialOverride = mat };
        AddChild(mi);

        var startPos = from + dir * (tracerLen * 0.5f);
        var endPos = to - dir * (tracerLen * 0.5f);
        mi.GlobalPosition = startPos;
        mi.LookAt(to, Vector3.Up);

        const float flight = 0.25f;
        var tw = CreateTween();
        tw.TweenProperty(mi, "global_position", endPos, flight);
        tw.TweenCallback(Callable.From(() =>
        {
            if (_hit) SpawnImpactFlash(_impactPos);
            if (GodotObject.IsInstanceValid(mi)) mi.QueueFree();
        }));

        ScheduleFinalCleanup(flight + 0.5f);
    }

    /// <summary>
    /// Delayed <see cref="Node.QueueFree"/> so impact sparks + muzzle light have
    /// time to finish their own tweens before the parent is removed.
    /// </summary>
    private void ScheduleFinalCleanup(float seconds)
    {
        var tw = CreateTween();
        tw.TweenInterval(seconds);
        tw.TweenCallback(Callable.From(() =>
        {
            if (GodotObject.IsInstanceValid(this)) QueueFree();
        }));
    }
}
