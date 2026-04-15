using System.Collections.Generic;
using System.Linq;
using Godot;
using SpacedOut.Sector;
using SpacedOut.Shared;
using SpacedOut.State;

namespace SpacedOut.LevelGen;

/// <summary>
/// Creates and manages 3D marker visuals for sector entities that appear on the 2D maps
/// but need visible 3D representations (contact markers, pin brackets).
/// Resource zones have no 3D visual — they are represented by the natural objects
/// (asteroids, debris, etc.) already placed inside them by the SectorGenerator.
/// </summary>
public class Sector3DMarkers
{
    private readonly Node3D _container;
    private readonly Dictionary<string, Node3D> _markers = new();
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
                var marker = CreateContactMarker(entity);
                _container.AddChild(marker);
                _markers[entity.Id] = marker;
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

            marker.Visible = true;
            marker.GlobalPosition = entity.WorldPosition;
        }
    }

    /// <summary>
    /// Creates a 3D marker for an entity added at runtime (not present during Initialize).
    /// </summary>
    public void AddDynamicMarker(SectorEntity entity)
    {
        if (_markers.ContainsKey(entity.Id)) return;
        var marker = CreateContactMarker(entity);
        _container.AddChild(marker);
        _markers[entity.Id] = marker;
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
        _pinBrackets.Clear();
        _sectorData = null;
    }

    // ── Factory methods ─────────────────────────────────────────────

    private static Node3D CreateContactMarker(SectorEntity entity)
    {
        var color = ThemeColors.GetContactColor(entity.ContactType);
        var node = new Node3D { Name = $"Marker_{entity.Id}" };

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

        node.Position = entity.WorldPosition;
        return node;
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
