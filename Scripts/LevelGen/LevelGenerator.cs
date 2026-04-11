using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using SpacedOut.Player;

namespace SpacedOut.LevelGen;

public partial class LevelGenerator : Node3D
{
    private int _currentSeed;
    private string _currentBiomeId = "asteroid_field";
    private Random _rng = new();
    private Node3D _levelContainer = null!;
    private readonly List<SpawnedObject> _spawnedObjects = new();
    private Vector3 _spawnPoint;
    private Vector3 _exitPoint;
    private Vector3 _landmarkPosition;
    private Vector3 _encounterPosition;
    private int _instanceCounter;
    private bool _standaloneMode;

    // ── Public read-only state ──────────────────────────────────────

    public int CurrentSeed => _currentSeed;
    public string CurrentBiomeId => _currentBiomeId;
    public IReadOnlyList<SpawnedObject> SpawnedObjects => _spawnedObjects;
    public Vector3 SpawnPoint => _spawnPoint;
    public Vector3 ExitPoint => _exitPoint;
    public Vector3 EncounterPosition => _encounterPosition;
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

            _currentSeed = (int)(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() & 0x7FFFFFFF);
            GenerateLevel(_currentSeed);
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
                GenerateLevel(_currentSeed, _currentBiomeId);
                break;
            case Key.F1:
                ToggleOverlay();
                break;
            case Key.Key1:
                GenerateLevel(_currentSeed, "asteroid_field");
                break;
            case Key.Key2:
                GenerateLevel(_currentSeed, "wreck_zone");
                break;
            case Key.Key3:
                GenerateLevel(_currentSeed, "station_periphery");
                break;
        }
    }

    // ── Public API ──────────────────────────────────────────────────

    public void RegenerateWithNewSeed()
    {
        _currentSeed = (int)(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() & 0x7FFFFFFF);
        GenerateLevel(_currentSeed);
    }

    public void GenerateLevel(int seed, string? biomeOverride = null)
    {
        _currentSeed = seed;
        _rng = new Random(seed);
        _instanceCounter = 0;

        if (biomeOverride != null)
            _currentBiomeId = biomeOverride;
        else
        {
            var ids = BiomeDefinition.GetAllIds();
            _currentBiomeId = ids[_rng.Next(ids.Length)];
        }

        ClearLevel();
        var biome = BiomeDefinition.Get(_currentBiomeId);

        GD.Print($"[LevelGen] Seed={seed}  Biome={biome.DisplayName}");

        CalculateLayout(biome);
        PlaceLandmarks(biome);

        var clusters = GenerateClusterCenters(biome);
        PlaceMidScale(biome, clusters);
        PlaceSmallInClusters(biome, clusters);
        PlaceScatter(biome);
        PlaceMarkers(biome);
        PlaceSpawnVisual();

        Validate(biome);

        TeleportCamera();
        GetNodeOrNull<LevelGenDebugOverlay>("DebugUI/DebugOverlay")?.UpdateStats();

        GD.Print($"[LevelGen] Fertig – {_spawnedObjects.Count} Objekte. Valide={IsValid}");
    }

    // ── Generation phases ───────────────────────────────────────────

    private void ClearLevel()
    {
        foreach (var child in _levelContainer.GetChildren())
            child.QueueFree();
        _spawnedObjects.Clear();
        ValidationMessages.Clear();
        IsValid = true;
    }

    private void CalculateLayout(BiomeDefinition biome)
    {
        float r = biome.LevelRadius;

        float spawnAngle = NextFloat() * Mathf.Tau;
        _spawnPoint = new Vector3(
            MathF.Cos(spawnAngle) * r * 0.85f,
            (NextFloat() - 0.5f) * r * 0.12f,
            MathF.Sin(spawnAngle) * r * 0.85f);

        float exitAngle = spawnAngle + MathF.PI + (NextFloat() - 0.5f) * 0.7f;
        _exitPoint = new Vector3(
            MathF.Cos(exitAngle) * r * 0.8f,
            (NextFloat() - 0.5f) * r * 0.12f,
            MathF.Sin(exitAngle) * r * 0.8f);

        float lmDist = r * biome.LandmarkCenterOffset;
        float lmAngle = NextFloat() * Mathf.Tau;
        _landmarkPosition = new Vector3(
            MathF.Cos(lmAngle) * lmDist,
            (NextFloat() - 0.5f) * r * 0.08f,
            MathF.Sin(lmAngle) * lmDist);

        // Encounter marker: placed at sector edge, roughly perpendicular
        // to the spawn–exit axis so the "unknown contact" approaches from the side
        float midAngle = (spawnAngle + exitAngle) / 2f;
        float encAngle = midAngle + MathF.PI / 2f + (NextFloat() - 0.5f) * 0.4f;
        if (_rng.Next(2) == 0) encAngle += MathF.PI;
        _encounterPosition = new Vector3(
            MathF.Cos(encAngle) * r * 0.9f,
            (NextFloat() - 0.5f) * r * 0.1f,
            MathF.Sin(encAngle) * r * 0.9f);
    }

    private void PlaceLandmarks(BiomeDefinition biome)
    {
        for (int i = 0; i < biome.LandmarkCount; i++)
        {
            string id = biome.LandmarkAssets[_rng.Next(biome.LandmarkAssets.Length)];
            var def = AssetLibrary.GetById(id);
            if (def == null) continue;

            float scale = Mathf.Lerp(def.MinScale, def.MaxScale, NextFloat());
            SpawnObject(def, _landmarkPosition, scale, RandomRotation());
        }
    }

    private List<Vector3> GenerateClusterCenters(BiomeDefinition biome)
    {
        int count = _rng.Next(biome.ClusterCountMin, biome.ClusterCountMax + 1);
        var centers = new List<Vector3>(count);

        for (int i = 0; i < count; i++)
        {
            bool found = false;
            for (int attempt = 0; attempt < 60; attempt++)
            {
                float angle = NextFloat() * Mathf.Tau;
                float dist = (NextFloat() * 0.55f + 0.2f) * biome.LevelRadius;
                var c = new Vector3(
                    MathF.Cos(angle) * dist,
                    (NextFloat() - 0.5f) * biome.LevelRadius * 0.22f,
                    MathF.Sin(angle) * dist);

                if (c.DistanceTo(_spawnPoint) < biome.SpawnSafeRadius * 1.5f) continue;
                if (IsInCorridor(c, biome.CorridorWidth * 0.8f)) continue;
                if (!AllFarEnough(c, centers, biome.ClusterSpacing)) continue;

                centers.Add(c);
                found = true;
                break;
            }

            if (!found)
            {
                // fallback: random position inside bounds
                float a = NextFloat() * Mathf.Tau;
                float d = NextFloat() * biome.LevelRadius * 0.6f;
                centers.Add(new Vector3(
                    MathF.Cos(a) * d,
                    (NextFloat() - 0.5f) * biome.LevelRadius * 0.15f,
                    MathF.Sin(a) * d));
            }
        }

        return centers;
    }

    private void PlaceMidScale(BiomeDefinition biome, List<Vector3> clusters)
    {
        int total = _rng.Next(biome.MidScaleMin, biome.MidScaleMax + 1);
        if (clusters.Count == 0) return;

        int perCluster = total / clusters.Count;
        int remainder = total % clusters.Count;

        for (int c = 0; c < clusters.Count; c++)
        {
            int n = perCluster + (c < remainder ? 1 : 0);
            for (int i = 0; i < n; i++)
            {
                string id = biome.MidScaleAssets[_rng.Next(biome.MidScaleAssets.Length)];
                var def = AssetLibrary.GetById(id);
                if (def == null) continue;

                var pos = clusters[c] + RandomInSphere(biome.ClusterRadius);
                float scale = Mathf.Lerp(def.MinScale, def.MaxScale, NextFloat());
                TrySpawn(def, pos, scale, biome.SpawnSafeRadius);
            }
        }
    }

    private void PlaceSmallInClusters(BiomeDefinition biome, List<Vector3> clusters)
    {
        int total = _rng.Next(biome.SmallMin, biome.SmallMax + 1);
        int inClusters = (int)(total * 0.65f);
        if (clusters.Count == 0) return;

        int perCluster = inClusters / clusters.Count;

        for (int c = 0; c < clusters.Count; c++)
        {
            for (int i = 0; i < perCluster; i++)
            {
                string id = biome.SmallAssets[_rng.Next(biome.SmallAssets.Length)];
                var def = AssetLibrary.GetById(id);
                if (def == null) continue;

                var pos = clusters[c] + RandomInSphere(biome.ClusterRadius * 1.3f);
                float scale = Mathf.Lerp(def.MinScale, def.MaxScale, NextFloat());
                TrySpawn(def, pos, scale, biome.SpawnSafeRadius);
            }
        }
    }

    private void PlaceScatter(BiomeDefinition biome)
    {
        int count = _rng.Next(biome.ScatterMin, biome.ScatterMax + 1);
        for (int i = 0; i < count; i++)
        {
            string id = biome.SmallAssets[_rng.Next(biome.SmallAssets.Length)];
            var def = AssetLibrary.GetById(id);
            if (def == null) continue;

            var pos = RandomInSphere(biome.LevelRadius * 0.8f);
            float scale = Mathf.Lerp(def.MinScale, def.MaxScale, NextFloat());
            TrySpawn(def, pos, scale, biome.SpawnSafeRadius);
        }
    }

    private void PlaceMarkers(BiomeDefinition biome)
    {
        // Exit marker
        var exitDef = AssetLibrary.GetById("exit_marker");
        if (exitDef != null)
            SpawnObject(exitDef, _exitPoint, 1f, Vector3.Zero);

        // Encounter marker (3D anchor for the unknown-contact event)
        var encDef = AssetLibrary.GetById("encounter_marker");
        if (encDef != null)
            SpawnObject(encDef, _encounterPosition, 1f, Vector3.Zero);

        // POI / resource / loot markers spread across the sector
        for (int i = 0; i < biome.MarkerCount; i++)
        {
            string id = biome.MarkerAssets[_rng.Next(biome.MarkerAssets.Length)];
            var def = AssetLibrary.GetById(id);
            if (def == null) continue;

            for (int attempt = 0; attempt < 50; attempt++)
            {
                var pos = RandomInSphere(biome.LevelRadius * 0.75f);
                if (SpawnValidator.CanPlace(pos, def.Radius, def.MinSpacing,
                        _spawnedObjects, _spawnPoint, biome.SpawnSafeRadius))
                {
                    SpawnObject(def, pos, 1f, Vector3.Zero);
                    break;
                }
            }
        }
    }

    private void PlaceSpawnVisual()
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
        mi.Position = _spawnPoint;
        mi.Name = "SpawnMarker";
        _levelContainer.AddChild(mi);
    }

    // ── Validation ──────────────────────────────────────────────────

    private void Validate(BiomeDefinition biome)
    {
        var msgs = SpawnValidator.ValidateLevel(
            _spawnedObjects, _spawnPoint, _exitPoint,
            biome.SpawnSafeRadius, biome.LevelRadius);

        ValidationMessages.AddRange(msgs);
        IsValid = msgs.Count == 0;

        if (IsValid)
            ValidationMessages.Add("Validierung erfolgreich");
    }

    // ── Helpers ─────────────────────────────────────────────────────

    private bool TrySpawn(AssetDefinition def, Vector3 pos, float scale, float spawnSafe)
    {
        float r = def.Radius * scale;
        if (!SpawnValidator.CanPlace(pos, r, def.MinSpacing,
                _spawnedObjects, _spawnPoint, spawnSafe))
            return false;

        SpawnObject(def, pos, scale, RandomRotation());
        return true;
    }

    private void SpawnObject(AssetDefinition def, Vector3 pos, float scale, Vector3 rot)
    {
        var obj = PlaceholderFactory.Create(def, scale, _currentBiomeId, NextInstanceId());
        obj.Position = pos;
        obj.Rotation = rot;
        _levelContainer.AddChild(obj);
        _spawnedObjects.Add(obj);
    }

    private void TeleportCamera()
    {
        if (!_standaloneMode) return;
        if (GetNodeOrNull("FlyCamera") is FlyCamera cam)
            cam.Teleport(_spawnPoint, _landmarkPosition);
    }

    private void ToggleOverlay()
    {
        var ov = GetNodeOrNull<Control>("DebugUI/DebugOverlay");
        if (ov != null) ov.Visible = !ov.Visible;
    }

    // ── Maths utilities ─────────────────────────────────────────────

    private float NextFloat() => (float)_rng.NextDouble();
    private string NextInstanceId() => $"{_instanceCounter++:D4}";

    private Vector3 RandomRotation() => new(
        NextFloat() * Mathf.Tau,
        NextFloat() * Mathf.Tau,
        NextFloat() * Mathf.Tau);

    private Vector3 RandomInSphere(float radius)
    {
        float u = NextFloat();
        float v = NextFloat();
        float theta = Mathf.Tau * u;
        float phi = MathF.Acos(2f * v - 1f);
        float r = radius * MathF.Cbrt(NextFloat());

        return new Vector3(
            r * MathF.Sin(phi) * MathF.Cos(theta),
            r * MathF.Sin(phi) * MathF.Sin(theta) * 0.3f,
            r * MathF.Cos(phi));
    }

    private bool IsInCorridor(Vector3 point, float width)
    {
        var axis = _landmarkPosition - _spawnPoint;
        float axisLenSq = axis.LengthSquared();
        if (axisLenSq < 0.01f) return false;
        float t = Mathf.Clamp((point - _spawnPoint).Dot(axis) / axisLenSq, 0, 1);
        var closest = _spawnPoint + axis * t;
        return point.DistanceTo(closest) < width;
    }

    private static bool AllFarEnough(Vector3 p, List<Vector3> pts, float minDist) =>
        pts.TrueForAll(q => p.DistanceTo(q) >= minDist);

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
