using System.Collections.Generic;
using System.Linq;
using Godot;
using SpacedOut.Agents;
using SpacedOut.Sector;
using SpacedOut.Shared;
using SpacedOut.State;

namespace SpacedOut.LevelGen;

/// <summary>
/// Creates and manages 3D marker visuals for sector entities that appear on the 2D maps
/// but need visible 3D representations (contact markers, pin brackets).
/// Movable/abstract markers get a small HUD blip only when no 3D mesh could be
/// loaded (missing scene pool or load failure). When an agent archetype or asset
/// definition provides a scene, the mesh is used alone and rotated to face velocity.
/// Resource zones have no 3D visual — they are represented by the natural objects
/// (asteroids, debris, etc.) already placed inside them by the SectorGenerator.
/// </summary>
public class Sector3DMarkers
{
    private readonly Node3D _container;
    private readonly Dictionary<string, Node3D> _markers = new();
    private readonly Dictionary<string, Node3D> _meshRoots = new();
    private readonly Dictionary<string, Node3D> _pinBrackets = new();
    private SectorData? _sectorData;

    public Sector3DMarkers(Node3D container)
    {
        _container = container;
    }

    public void Initialize(SectorData data)
    {
        Clear();
        _sectorData = data;

        foreach (var entity in data.Entities)
        {
            if (entity.MapPresence != MapPresence.Point) continue;

            if (entity.IsMovable || IsAbstractMarker(entity))
            {
                SpawnContactNode(entity);
            }
        }

        UpdateVisibility();
    }

    public void UpdateVisibility()
    {
        if (_sectorData == null) return;

        foreach (var entity in _sectorData.Entities)
        {
            if (!_markers.TryGetValue(entity.Id, out var marker)) continue;

            marker.Visible = !entity.IsDestroyed;
            marker.GlobalPosition = entity.WorldPosition;

            if (_meshRoots.TryGetValue(entity.Id, out var mesh))
                OrientMeshToVelocity(entity, mesh, entity.Velocity);
        }
    }

    /// <summary>
    /// Creates a 3D marker for an entity added at runtime (not present during Initialize).
    /// </summary>
    public void AddDynamicMarker(SectorEntity entity)
    {
        if (_markers.ContainsKey(entity.Id)) return;
        SpawnContactNode(entity);
    }

    public void AddPinBracket(string entityId, Vector3 worldPos, float objectWorldRadius, string label)
    {
        if (_pinBrackets.ContainsKey(entityId)) return;
        var bracket = CreateBracketMarker(worldPos, objectWorldRadius, label);
        _container.AddChild(bracket);
        _pinBrackets[entityId] = bracket;
    }

    public void RemovePinBracket(string entityId)
    {
        if (!_pinBrackets.Remove(entityId, out var bracket)) return;
        bracket.QueueFree();
    }

    /// <summary>Removes 3D pin brackets whose contact id is no longer in the pinned set.</summary>
    public void RemovePinBracketsExcept(HashSet<string> keepContactIds)
    {
        foreach (var id in _pinBrackets.Keys.ToList())
        {
            if (!keepContactIds.Contains(id))
                RemovePinBracket(id);
        }
    }

    public void SetPinBracketWorldPosition(string contactEntityId, Vector3 worldPos)
    {
        if (_pinBrackets.TryGetValue(contactEntityId, out var bracket))
            bracket.GlobalPosition = worldPos;
    }

    public void Clear()
    {
        foreach (var m in _markers.Values) m.QueueFree();
        foreach (var b in _pinBrackets.Values) b.QueueFree();
        _markers.Clear();
        _meshRoots.Clear();
        _pinBrackets.Clear();
        _sectorData = null;
    }

    // ── Factory methods ─────────────────────────────────────────────

    private void SpawnContactNode(SectorEntity entity)
    {
        int seed = _sectorData?.Seed ?? 0;
        var node = new Node3D { Name = $"Marker_{entity.Id}" };

        if (TryInstantiateMesh(entity, seed, out var mesh))
        {
            node.AddChild(mesh);
            _meshRoots[entity.Id] = mesh;
            OrientMeshToVelocity(entity, mesh, entity.Velocity);
        }
        else
            node.AddChild(CreateHudBlip(entity));

        node.Position = entity.WorldPosition;
        _container.AddChild(node);
        _markers[entity.Id] = node;
    }

    /// <summary>
    /// Small billboard sphere + soft glow when no ship/marker mesh is available.
    /// </summary>
    private static Node3D CreateHudBlip(SectorEntity entity)
    {
        var color = ThemeColors.GetContactColor(entity.ContactType);
        var node = new Node3D { Name = "Blip" };

        var mesh = new SphereMesh { Radius = 2.5f, Height = 5f, RadialSegments = 8, Rings = 4 };
        var mat = new StandardMaterial3D
        {
            AlbedoColor = color with { A = 0.7f },
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            EmissionEnabled = true,
            Emission = color,
            EmissionEnergyMultiplier = 2f,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            BillboardMode = BaseMaterial3D.BillboardModeEnum.Enabled,
        };
        var mi = new MeshInstance3D { Mesh = mesh, MaterialOverride = mat };
        node.AddChild(mi);

        var light = new OmniLight3D
        {
            LightColor = color,
            LightEnergy = 0.3f,
            OmniRange = 15f,
            OmniAttenuation = 2f,
            ShadowEnabled = false,
        };
        node.AddChild(light);

        return node;
    }

    /// <summary>
    /// Tries agent archetype ScenePaths first (for agent-driven movables),
    /// falls back to the asset library ScenePaths (for static-ish markers
    /// that still get 3D visuals). Returns false if neither pool yields a
    /// loadable scene — caller then shows the blip only.
    /// </summary>
    private static bool TryInstantiateMesh(SectorEntity entity, int seed, out Node3D mesh)
    {
        // Agents keep the plain path flow (no bundle support currently).
        if (!string.IsNullOrEmpty(entity.AgentTypeId) &&
            AgentDefinition.TryGet(entity.AgentTypeId, out var agentDef))
        {
            var path = AssetVariantPicker.PickScenePath(agentDef.ScenePaths, seed, entity.Id);
            if (path == null) { mesh = null!; return false; }

            var packed = GD.Load<PackedScene>(path);
            if (packed == null) { mesh = null!; return false; }

            var holder = new Node3D { Name = "Mesh" };
            var instance = packed.Instantiate<Node3D>();
            float s = entity.Scale * agentDef.VisualScale;
            instance.Scale = new Vector3(s, s, s);
            holder.AddChild(instance);

            mesh = holder;
            return true;
        }

        // Asset-driven markers go through the bundle-aware picker.
        if (!string.IsNullOrEmpty(entity.AssetId))
        {
            var asset = AssetLibrary.GetById(entity.AssetId);
            if (asset == null) { mesh = null!; return false; }

            var variant = AssetVariantPicker.PickRef(asset, seed, entity.Id);
            if (variant is not { } v) { mesh = null!; return false; }

            Node3D? instance;
            if (v.ChildName is { } childName)
            {
                instance = MeshBundleResolver.DuplicateChild(v.ScenePath, childName);
            }
            else
            {
                var packed = GD.Load<PackedScene>(v.ScenePath);
                instance = packed?.Instantiate<Node3D>();
            }
            if (instance == null) { mesh = null!; return false; }

            var holder = new Node3D { Name = "Mesh" };
            float s = entity.Scale * asset.VisualScale * v.VisualScale;
            instance.Scale = new Vector3(s, s, s);
            holder.AddChild(instance);

            mesh = holder;
            return true;
        }

        mesh = null!;
        return false;
    }

    /// <summary>
    /// Rotates the mesh holder around Y so its -Z axis points along the
    /// current velocity. Ships are modelled looking down -Z by convention;
    /// agent <see cref="AgentDefinition.VisualYawOffsetRadians"/> corrects GLBs
    /// that face +Z. Skipped for near-zero velocity to avoid jitter on idle agents.
    /// </summary>
    private static void OrientMeshToVelocity(SectorEntity entity, Node3D mesh, Vector3 velocity)
    {
        float yawOffset = 0f;
        if (!string.IsNullOrEmpty(entity.AgentTypeId) &&
            AgentDefinition.TryGet(entity.AgentTypeId, out var agentDef))
            yawOffset = agentDef.VisualYawOffsetRadians;

        var flat = new Vector3(velocity.X, 0f, velocity.Z);
        if (flat.LengthSquared() < 0.01f) return;
        float yaw = Mathf.Atan2(flat.X, flat.Z) + Mathf.Pi + yawOffset;
        mesh.Rotation = new Vector3(0f, yaw, 0f);
    }

    /// <summary>
    /// Pin ring sized from <paramref name="objectWorldRadius"/>; orientation via <see cref="PinBracketFacing"/>
    /// (LookAt active camera each frame — material billboards fail for TorusMesh).
    /// </summary>
    private static Node3D CreateBracketMarker(Vector3 pos, float objectWorldRadius, string label)
    {
        var node = new PinBracketFacing { Name = $"Pin_{label}" };

        float meanR = Mathf.Clamp(objectWorldRadius * 1.28f, 4f, 140f);
        float tube = Mathf.Max(meanR * 0.07f, 0.45f);
        float inner = Mathf.Max(meanR - tube, 0.5f);
        float outer = meanR + tube;

        var ring = new TorusMesh
        {
            InnerRadius = inner,
            OuterRadius = outer,
            Rings = Mathf.Max(12, (int)(16 * Mathf.Clamp(meanR / 24f, 0.85f, 1.4f))),
            RingSegments = 12,
        };
        var mat = new StandardMaterial3D
        {
            AlbedoColor = ThemeColors.Cyan with { A = 0.8f },
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            EmissionEnabled = true,
            Emission = ThemeColors.Cyan,
            EmissionEnergyMultiplier = 1.5f,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        };
        var mi = new MeshInstance3D { Mesh = ring, MaterialOverride = mat };
        node.AddChild(mi);

        node.Position = pos;
        return node;
    }

    private static bool IsAbstractMarker(SectorEntity entity) =>
        entity.Type is SectorEntityType.PatrolDrone
            or SectorEntityType.HostileShip
            or SectorEntityType.NeutralShip;
}
