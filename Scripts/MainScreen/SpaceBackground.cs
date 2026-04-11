using Godot;

namespace SpacedOut.MainScreen;

/// <summary>
/// Generates a simple starfield using GPUParticles3D and manages
/// the 3D representation of contacts on the main screen.
/// </summary>
public partial class SpaceBackground : Node3D
{
    private GpuParticles3D _starParticles = null!;
    private GpuParticles3D _dustParticles = null!;

    public override void _Ready()
    {
        CreateStarfield();
        CreateSpaceDust();
    }

    private void CreateStarfield()
    {
        _starParticles = new GpuParticles3D
        {
            Amount = 300,
            Lifetime = 20f,
            Explosiveness = 0f,
            Randomness = 1f,
            FixedFps = 0,
        };

        var material = new ParticleProcessMaterial
        {
            EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Box,
            EmissionBoxExtents = new Vector3(80, 40, 120),
            Direction = new Vector3(0, 0, 1),
            InitialVelocityMin = 0.5f,
            InitialVelocityMax = 2f,
            Gravity = Vector3.Zero,
            ScaleMin = 0.02f,
            ScaleMax = 0.08f,
            Color = new Color(0.9f, 0.92f, 1.0f, 0.8f),
        };
        _starParticles.ProcessMaterial = material;

        var mesh = new QuadMesh { Size = new Vector2(1, 1) };
        var meshMaterial = new StandardMaterial3D
        {
            AlbedoColor = new Color(1, 1, 1),
            EmissionEnabled = true,
            Emission = new Color(0.8f, 0.85f, 1f),
            EmissionEnergyMultiplier = 2f,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            BillboardMode = BaseMaterial3D.BillboardModeEnum.Enabled,
        };
        mesh.Material = meshMaterial;
        _starParticles.DrawPass1 = mesh;

        _starParticles.Position = new Vector3(0, 0, -60);
        AddChild(_starParticles);
    }

    private void CreateSpaceDust()
    {
        _dustParticles = new GpuParticles3D
        {
            Amount = 50,
            Lifetime = 15f,
            Explosiveness = 0f,
            Randomness = 1f,
        };

        var material = new ParticleProcessMaterial
        {
            EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Box,
            EmissionBoxExtents = new Vector3(30, 15, 60),
            Direction = new Vector3(0, 0, 1),
            InitialVelocityMin = 0.2f,
            InitialVelocityMax = 0.8f,
            Gravity = Vector3.Zero,
            ScaleMin = 0.01f,
            ScaleMax = 0.03f,
            Color = new Color(0.4f, 0.5f, 0.7f, 0.3f),
        };
        _dustParticles.ProcessMaterial = material;

        var mesh = new QuadMesh { Size = new Vector2(1, 1) };
        var meshMaterial = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.5f, 0.6f, 0.8f, 0.3f),
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            BillboardMode = BaseMaterial3D.BillboardModeEnum.Enabled,
        };
        mesh.Material = meshMaterial;
        _dustParticles.DrawPass1 = mesh;

        _dustParticles.Position = new Vector3(0, 0, -30);
        AddChild(_dustParticles);
    }
}
