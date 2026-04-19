using System;
using Godot;
using SpacedOut.LevelGen;
using SpacedOut.Sector;
using SpacedOut.State;

namespace SpacedOut.Sky;

/// <summary>
/// Owns the procedural space sky: finds the active <see cref="WorldEnvironment"/>,
/// wires a <see cref="ShaderMaterial"/> (see <c>Assets/shaders/sky/space_sky.gdshader</c>)
/// into its <see cref="Godot.Sky"/>, and re-configures the uniforms every time a
/// sector is (re)built.
///
/// The node replaces the old <c>SpaceBackground</c> stub in both
/// <c>Scenes/Main.tscn</c> and <c>Scenes/LevelGenScene.tscn</c>. It also
/// parents a <see cref="PlanetBillboardSpawner"/> so procedural planets follow
/// the active camera along with the sky.
/// </summary>
public partial class SpaceSkyController : Node3D
{
    private const string ShaderPath = "res://Assets/shaders/sky/space_sky.gdshader";

    [Export] public NodePath? WorldEnvironmentPath { get; set; }

    /// <summary>Disable live twinkle / time-based uniforms for perf testing.</summary>
    [Export] public bool EnableTwinkle { get; set; } = true;

    private WorldEnvironment? _worldEnv;
    private Shader? _shader;
    private ShaderMaterial? _proceduralMaterial;
    private Godot.Sky? _proceduralSky;
    private PlanetBillboardSpawner _planets = null!;

    public override void _Ready()
    {
        _worldEnv = ResolveWorldEnvironment();
        _shader = ResourceLoader.Exists(ShaderPath) ? GD.Load<Shader>(ShaderPath) : null;

        if (_worldEnv == null)
            GD.PrintErr("[Sky] SpaceSkyController: no WorldEnvironment found in ancestors.");
        if (_shader == null)
            GD.PrintErr($"[Sky] SpaceSkyController: shader not found at {ShaderPath}.");

        _planets = new PlanetBillboardSpawner { Name = "Planets" };
        AddChild(_planets);

        if (!GameFeatures.SkyboxEnabled)
            ApplyDisabledSky();
    }

    /// <summary>
    /// Re-configures the sky and planet layer for the given sector. Called
    /// from <c>LevelGenerator.BuildFromSectorData</c> after entities are spawned.
    /// </summary>
    public void ApplySectorSky(SectorData data, BiomeDefinition biome)
    {
        if (_worldEnv?.Environment == null) return;

        if (!GameFeatures.SkyboxEnabled)
        {
            ApplyDisabledSky();
            return;
        }

        var profile = biome.SkyboxProfile;

        if (!string.IsNullOrEmpty(profile.OverrideSkyResourcePath) &&
            ResourceLoader.Exists(profile.OverrideSkyResourcePath))
        {
            if (GD.Load<Godot.Sky>(profile.OverrideSkyResourcePath) is { } overrideSky)
                _worldEnv.Environment.Sky = overrideSky;
        }
        else if (_shader != null)
        {
            EnsureProceduralSky();
            ConfigureShader(data.Seed, profile);
            _worldEnv.Environment.Sky = _proceduralSky;
        }

        _planets.Apply(data.Seed, profile);
    }

    /// <summary>
    /// Flat colour background, no <see cref="Godot.Sky"/>, no planet billboards.
    /// </summary>
    private void ApplyDisabledSky()
    {
        if (_worldEnv?.Environment == null) return;

        var env = _worldEnv.Environment;
        env.BackgroundMode = Godot.Environment.BGMode.Color;
        env.BackgroundColor = new Color(0.02f, 0.02f, 0.06f, 1f);
        env.Sky = null;

        foreach (var child in _planets.GetChildren())
            child.QueueFree();
    }

    // ── Shader wiring ────────────────────────────────────────────────

    private void EnsureProceduralSky()
    {
        if (_proceduralSky != null) return;

        _proceduralMaterial = new ShaderMaterial { Shader = _shader };
        _proceduralSky = new Godot.Sky
        {
            SkyMaterial = _proceduralMaterial,
            // Size64 gives crisp IBL reflections while costing almost nothing in
            // Quality mode (the sky is baked once per sector, never per frame).
            RadianceSize = Godot.Sky.RadianceSizeEnum.Size64,
            // Quality: the cubemap is baked once when the material becomes
            // dirty (happens in ApplySectorSky via SetShaderParameter) and then
            // sampled with a single texture fetch per screen pixel. The sky
            // shader therefore runs exactly once per sector change.
            ProcessMode = Godot.Sky.ProcessModeEnum.Quality,
        };
    }

    private void ConfigureShader(int seed, SkyboxProfile p)
    {
        if (_proceduralMaterial == null) return;

        var rng = new Random(seed);
        float seedF = (float)((seed & 0x7FFFFFFF) % 10_000) * 0.001f;

        SetParam("seed", seedF);

        SetParam("nebula_color_a",  p.NebulaColorA);
        SetParam("nebula_color_b",  p.NebulaColorB);
        SetParam("nebula_intensity", p.NebulaIntensity);
        SetParam("nebula_scale",    p.NebulaScale);
        SetParam("nebula_contrast", p.NebulaContrast);

        SetParam("star_density",    p.StarDensity);
        SetParam("star_brightness", p.StarBrightness);
        SetParam("star_twinkle",    EnableTwinkle ? p.StarTwinkle : 0f);

        SetParam("galaxy_color",     p.GalaxyColor);
        SetParam("galaxy_intensity", p.GalaxyIntensity);
        SetParam("galaxy_width",     p.GalaxyWidth);
        SetParam("galaxy_normal",    RandomUnit(rng));

        var suns = p.Suns;
        for (int i = 0; i < 3; i++)
        {
            string dirName = $"sun{i + 1}_direction";
            string colName = $"sun{i + 1}_color";
            string sizeName = $"sun{i + 1}_size";
            string intName = $"sun{i + 1}_intensity";

            if (i < suns.Length)
            {
                SetParam(dirName, RandomUnit(rng));
                SetParam(colName, suns[i].Color);
                SetParam(sizeName, suns[i].Size);
                SetParam(intName, suns[i].Intensity);
            }
            else
            {
                SetParam(intName, 0f);
            }
        }
    }

    private void SetParam(string name, Variant value) =>
        _proceduralMaterial?.SetShaderParameter(name, value);

    // ── Helpers ──────────────────────────────────────────────────────

    private WorldEnvironment? ResolveWorldEnvironment()
    {
        if (WorldEnvironmentPath != null && !WorldEnvironmentPath.IsEmpty)
        {
            var direct = GetNodeOrNull<WorldEnvironment>(WorldEnvironmentPath);
            if (direct != null) return direct;
        }

        // Walk up looking for a sibling "WorldEnvironment" node. Works for
        // both scene trees where SpaceBackground is a direct child of the root.
        Node? parent = GetParent();
        while (parent != null)
        {
            var sibling = parent.GetNodeOrNull<WorldEnvironment>("WorldEnvironment");
            if (sibling != null) return sibling;
            parent = parent.GetParent();
        }

        // Last resort: breadth-first search under the scene root.
        var root = GetTree()?.CurrentScene;
        return root != null ? FindWorldEnv(root) : null;
    }

    private static WorldEnvironment? FindWorldEnv(Node node)
    {
        if (node is WorldEnvironment we) return we;
        foreach (var child in node.GetChildren())
        {
            if (FindWorldEnv(child) is { } match) return match;
        }
        return null;
    }

    internal static Vector3 RandomUnit(Random rng)
    {
        float z = (float)(rng.NextDouble() * 2.0 - 1.0);
        float t = (float)(rng.NextDouble() * Math.PI * 2.0);
        float r = Mathf.Sqrt(Mathf.Max(0f, 1f - z * z));
        return new Vector3(r * Mathf.Cos(t), r * Mathf.Sin(t), z);
    }
}
