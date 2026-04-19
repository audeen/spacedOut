using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using SpacedOut.Player;
using SpacedOut.Sector;
using SpacedOut.Sky;

namespace SpacedOut.LevelGen;

public partial class LevelGenerator : Node3D
{
    private Node3D _levelContainer = null!;
    private InstancedAsteroidPool _asteroidPool = null!;
    private readonly List<SpawnedObject> _spawnedObjects = new();
    private bool _standaloneMode;

    private SectorData? _sectorData;
    private readonly SectorGenerator _sectorGenerator = new();

    // ── Public read-only state ──────────────────────────────────────

    public int CurrentSeed => _sectorData?.Seed ?? 0;
    public string CurrentBiomeId => _sectorData?.BiomeId ?? "asteroid_field";
    public IReadOnlyList<SpawnedObject> SpawnedObjects => _spawnedObjects;
    public Vector3 SpawnPoint => _sectorData?.SpawnPoint ?? Vector3.Zero;
    public Vector3 ExitPoint => _sectorData?.ExitPoint ?? Vector3.Zero;
    public Vector3 EncounterPosition => _sectorData?.EncounterPosition ?? Vector3.Zero;
    public SectorData? CurrentSectorData => _sectorData;
    public bool IsValid { get; private set; } = true;
    public List<string> ValidationMessages { get; } = new();

    // ── Lifecycle ───────────────────────────────────────────────────

    public override void _Ready()
    {
        _levelContainer = GetNode<Node3D>("GeneratedLevel");
        _asteroidPool = new InstancedAsteroidPool { Name = "InstancedAsteroids" };
        _levelContainer.AddChild(_asteroidPool);
        _standaloneMode = GetNodeOrNull("DebugUI") != null;

        GetNodeOrNull<LevelGenDebugOverlay>("DebugUI/DebugOverlay")
            ?.Initialize(this);

        if (_standaloneMode)
        {
            if (GetNodeOrNull("FlyCamera") is FlyCamera cam)
                cam.Activate();

            int seed = (int)(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() & 0x7FFFFFFF);
            GenerateLevel(seed);
        }
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (!_standaloneMode) return;
        if (@event is not InputEventKey { Pressed: true, Echo: false } key) return;

        switch (key.Keycode)
        {
            case Key.R when key.CtrlPressed:
                RegenerateWithNewSeed();
                break;
            case Key.R:
                GenerateLevel(CurrentSeed, CurrentBiomeId);
                break;
            case Key.F1:
                ToggleOverlay();
                break;
            case Key.Key1:
                GenerateLevel(CurrentSeed, "asteroid_field");
                break;
            case Key.Key2:
                GenerateLevel(CurrentSeed, "wreck_zone");
                break;
            case Key.Key3:
                GenerateLevel(CurrentSeed, "station_periphery");
                break;
        }
    }

    // ── Public API ──────────────────────────────────────────────────

    public void RegenerateWithNewSeed()
    {
        int seed = (int)(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() & 0x7FFFFFFF);
        GenerateLevel(seed);
    }

    /// <summary>
    /// Generate sector data and build the 3D scene from it.
    /// Standalone <see cref="LevelGenScene"/> uses <paramref name="radiusMultiplier"/> when set;
    /// otherwise defaults match gameplay (5× field/wreck, 0.6× station).
    /// </summary>
    public void GenerateLevel(int seed, string? biomeOverride = null, float? radiusMultiplier = null)
    {
        string biomeId = biomeOverride ?? PickRandomBiome(seed);
        float mult = radiusMultiplier ?? GetStandaloneDefaultRadiusMultiplier(biomeId);
        var data = _sectorGenerator.Generate(seed, biomeId, radiusMultiplier: mult);
        BuildFromSectorData(data);
    }

    private static float GetStandaloneDefaultRadiusMultiplier(string biomeId) =>
        biomeId == "station_periphery" ? 0.6f : 5f;

    /// <summary>
    /// Build the 3D scene from pre-generated SectorData.
    /// This is the main entry point when called from the orchestrator.
    /// </summary>
    public void BuildFromSectorData(SectorData data)
    {
        _sectorData = data;
        ClearLevel();

        GD.Print($"[LevelGen] Seed={data.Seed}  Biome={BiomeDefinition.Get(data.BiomeId).DisplayName}");

        int instanced = 0;
        foreach (var entity in data.Entities)
        {
            if (entity.IsMovable) continue;
            if (SpawnEntityAs3D(entity)) instanced++;
        }

        _asteroidPool.Commit();

        ApplySkybox(data);
        PlaceSpawnVisual(data.SpawnPoint);
        Validate();

        TeleportCamera();
        GetNodeOrNull<LevelGenDebugOverlay>("DebugUI/DebugOverlay")?.UpdateStats();

        GD.Print($"[LevelGen] Fertig – {_spawnedObjects.Count} Objekte " +
                 $"({instanced} instanced). Valide={IsValid}");
    }

    /// <summary>
    /// Adds a single static entity’s 3D representation at runtime (e.g. debug POI spawn).
    /// Does not clear or rebuild the level.
    /// </summary>
    public void AppendStaticEntity(SectorEntity entity)
    {
        if (_sectorData == null || entity.IsMovable) return;
        SpawnEntityAs3D(entity);
        _asteroidPool.Commit();
        GD.Print($"[LevelGen] AppendStaticEntity — jetzt {_spawnedObjects.Count} Objekte");
    }

    // ── 3D scene construction ───────────────────────────────────────

    /// <summary>
    /// Spawns the visual for <paramref name="entity"/>. Returns true when the
    /// entity was rendered through the <see cref="InstancedAsteroidPool"/>
    /// (no SceneTree node, MultiMesh transform only).
    /// </summary>
    private bool SpawnEntityAs3D(SectorEntity entity)
    {
        var def = AssetLibrary.GetById(entity.AssetId);
        if (def == null) return false;

        if (IsInstanceableAsteroid(def) &&
            PlaceholderFactory.TryAppendInstanced(
                def, entity.Scale, _sectorData!.Seed, entity.Id,
                entity.WorldPosition, entity.Rotation, _asteroidPool))
        {
            var stub = new SpawnedObject
            {
                InstanceId = entity.Id,
                AssetId = def.Id,
                Category = def.Category,
                BiomeType = _sectorData.BiomeId,
                ObjectRadius = def.Radius * entity.Scale,
                Tags = def.Tags,
                IsLandmark = def.IsLandmark,
                IsInstanced = true,
                IsPlaceholder = false,
                Name = $"{def.Id}_{entity.Id}",
                SectorEntityId = entity.Id,
            };
            stub.Position = entity.WorldPosition;
            stub.Rotation = entity.Rotation;
            _spawnedObjects.Add(stub);
            return true;
        }

        var obj = PlaceholderFactory.Create(
            def, entity.Scale, _sectorData!.BiomeId, entity.Id, _sectorData.Seed);
        obj.Position = entity.WorldPosition;
        obj.Rotation = entity.Rotation;
        obj.SectorEntityId = entity.Id;
        _levelContainer.AddChild(obj);
        _spawnedObjects.Add(obj);
        return false;
    }

    /// <summary>
    /// Non-landmark asteroids (Small, Medium, Large as scatter/mid fill) are
    /// safe to instance: no picking, no collision, and no agent logic points
    /// at them – everything goes through SectorEntity ids, not Node lookups.
    /// </summary>
    private static bool IsInstanceableAsteroid(AssetDefinition def)
    {
        if (def.IsLandmark) return false;
        return def.Category is AssetCategory.AsteroidSmall
            or AssetCategory.AsteroidMedium
            or AssetCategory.AsteroidLarge;
    }

    private void ApplySkybox(SectorData data)
    {
        var controller = FindSkyController();
        if (controller == null) return;

        if (!BiomeDefinition.TryGet(data.BiomeId, out var biome)) return;
        controller.ApplySectorSky(data, biome);
    }

    /// <summary>
    /// Re-applies the skybox for the current sector. Called after
    /// <see cref="SpacedOut.State.GameFeatures.SkyboxEnabled"/> is toggled
    /// at runtime (via the HUD debug panel) so the change is visible
    /// without regenerating the level.
    /// </summary>
    public void RefreshSkybox()
    {
        if (_sectorData != null)
            ApplySkybox(_sectorData);
    }

    private SpaceSkyController? FindSkyController()
    {
        // The SpaceSkyController replaces the old SpaceBackground stub. It can
        // sit as a sibling of the LevelGenerator (Main.tscn) or a sibling of
        // this node's parent chain (LevelGenScene.tscn).
        var root = GetTree()?.CurrentScene;
        return root != null ? SearchForController(root) : null;
    }

    private static SpaceSkyController? SearchForController(Node node)
    {
        if (node is SpaceSkyController ctrl) return ctrl;
        foreach (var child in node.GetChildren())
        {
            if (SearchForController(child) is { } match) return match;
        }
        return null;
    }

    private const string JumpArrivalScenePath = "res://Assets/scenes/fx/jump_arrival.tscn";

    private void PlaceSpawnVisual(Vector3 spawnPoint)
    {
        if (ResourceLoader.Exists(JumpArrivalScenePath) &&
            GD.Load<PackedScene>(JumpArrivalScenePath) is PackedScene packed)
        {
            var fx = packed.Instantiate<Node3D>();
            fx.Name = "SpawnMarker";
            fx.Position = spawnPoint;
            _levelContainer.AddChild(fx);
            return;
        }

        // Fallback glow sphere until the real FX scene exists.
        var mesh = new SphereMesh { Radius = 2f, Height = 4f };
        var mat = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.2f, 0.6f, 1f, 0.5f),
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            EmissionEnabled = true,
            Emission = new Color(0.2f, 0.6f, 1f),
            EmissionEnergyMultiplier = 2f,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        };
        var mi = new MeshInstance3D { Mesh = mesh, MaterialOverride = mat };
        mi.Position = spawnPoint;
        mi.Name = "SpawnMarker";
        _levelContainer.AddChild(mi);
    }

    private void ClearLevel()
    {
        foreach (var child in _levelContainer.GetChildren())
        {
            // The instanced asteroid pool is re-used across sector builds –
            // its MultiMesh instance counts are zeroed out via Reset() below.
            if (child == _asteroidPool) continue;
            child.QueueFree();
        }

        _asteroidPool.Reset();

        // Instanced stubs never joined the SceneTree. They are plain managed
        // Godot objects and must be freed explicitly.
        foreach (var obj in _spawnedObjects)
        {
            if (obj.IsInstanced && GodotObject.IsInstanceValid(obj) && !obj.IsInsideTree())
                obj.Free();
        }
        _spawnedObjects.Clear();

        ValidationMessages.Clear();
        IsValid = true;
    }

    // ── Validation ──────────────────────────────────────────────────

    private void Validate()
    {
        if (_sectorData == null) return;
        var biome = BiomeDefinition.Get(_sectorData.BiomeId);
        var msgs = SpawnValidator.ValidateLevel(
            _spawnedObjects, _sectorData.SpawnPoint, _sectorData.ExitPoint,
            biome.SpawnSafeRadius, _sectorData.LevelRadius);

        ValidationMessages.AddRange(msgs);
        IsValid = msgs.Count == 0;

        if (IsValid)
            ValidationMessages.Add("Validierung erfolgreich");
    }

    // ── Helpers ──────────────────────────────────────────────────────

    private static string PickRandomBiome(int seed)
    {
        var ids = BiomeDefinition.GetAllIds();
        return ids[new Random(seed).Next(ids.Length)];
    }

    private void TeleportCamera()
    {
        if (!_standaloneMode || _sectorData == null) return;
        if (GetNodeOrNull("FlyCamera") is FlyCamera cam)
            cam.Teleport(_sectorData.SpawnPoint, _sectorData.LandmarkPosition);
    }

    private void ToggleOverlay()
    {
        var ov = GetNodeOrNull<Control>("DebugUI/DebugOverlay");
        if (ov != null) ov.Visible = !ov.Visible;
    }

    // ── Debug helpers used by the overlay ────────────────────────────

    public int GetLandmarkCount() =>
        _spawnedObjects.Count(o => o.IsLandmark);

    public int GetMidScaleCount() =>
        _spawnedObjects.Count(o => !o.IsLandmark && o.ObjectRadius >= 8f);

    public int GetScatterCount() =>
        _spawnedObjects.Count(o => o.ObjectRadius < 8f && o.Category < AssetCategory.ResourceNode);

    public int GetMarkerCount() =>
        _spawnedObjects.Count(o => o.Category >= AssetCategory.ResourceNode);

    public Dictionary<AssetCategory, int> GetObjectCounts()
    {
        var counts = new Dictionary<AssetCategory, int>();
        foreach (var o in _spawnedObjects)
        {
            if (!counts.TryAdd(o.Category, 1))
                counts[o.Category]++;
        }
        return counts;
    }
}
