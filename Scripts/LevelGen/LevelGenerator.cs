using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using SpacedOut.Player;
using SpacedOut.Sector;

namespace SpacedOut.LevelGen;

public partial class LevelGenerator : Node3D
{
    private Node3D _levelContainer = null!;
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
    /// </summary>
    public void GenerateLevel(int seed, string? biomeOverride = null)
    {
        string biomeId = biomeOverride ?? PickRandomBiome(seed);
        var data = _sectorGenerator.Generate(seed, biomeId);
        BuildFromSectorData(data);
    }

    /// <summary>
    /// Build the 3D scene from pre-generated SectorData.
    /// This is the main entry point when called from the orchestrator.
    /// </summary>
    public void BuildFromSectorData(SectorData data)
    {
        _sectorData = data;
        ClearLevel();

        GD.Print($"[LevelGen] Seed={data.Seed}  Biome={BiomeDefinition.Get(data.BiomeId).DisplayName}");

        foreach (var entity in data.Entities)
        {
            if (entity.IsMovable) continue;
            SpawnEntityAs3D(entity);
        }

        PlaceSpawnVisual(data.SpawnPoint);
        Validate();

        TeleportCamera();
        GetNodeOrNull<LevelGenDebugOverlay>("DebugUI/DebugOverlay")?.UpdateStats();

        GD.Print($"[LevelGen] Fertig – {_spawnedObjects.Count} Objekte. Valide={IsValid}");
    }

    // ── 3D scene construction ───────────────────────────────────────

    private void SpawnEntityAs3D(SectorEntity entity)
    {
        var def = AssetLibrary.GetById(entity.AssetId);
        if (def == null) return;

        var obj = PlaceholderFactory.Create(def, entity.Scale, _sectorData!.BiomeId, entity.Id);
        obj.Position = entity.WorldPosition;
        obj.Rotation = entity.Rotation;
        obj.SectorEntityId = entity.Id;
        _levelContainer.AddChild(obj);
        _spawnedObjects.Add(obj);
    }

    private void PlaceSpawnVisual(Vector3 spawnPoint)
    {
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
            child.QueueFree();
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
            biome.SpawnSafeRadius, biome.LevelRadius);

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
