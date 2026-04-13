using System.Collections.Generic;
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

            bool visible = entity.Discovery != DiscoveryState.Hidden || entity.PreRevealed;
            marker.Visible = visible;

            if (entity.IsMovable)
                marker.GlobalPosition = entity.WorldPosition;
        }
    }

    public void AddPinBracket(string entityId, Vector3 worldPos, string label)
    {
        if (_pinBrackets.ContainsKey(entityId)) return;
        var bracket = CreateBracketMarker(worldPos, label);
        _container.AddChild(bracket);
        _pinBrackets[entityId] = bracket;
    }

    public void RemovePinBracket(string entityId)
    {
        if (!_pinBrackets.Remove(entityId, out var bracket)) return;
        bracket.QueueFree();
    }

    public void UpdatePinPositions(SectorData data)
    {
        foreach (var entity in data.Entities)
        {
            if (!_pinBrackets.TryGetValue(entity.Id, out var bracket)) continue;
            bracket.GlobalPosition = entity.WorldPosition;
        }
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

    private static Node3D CreateBracketMarker(Vector3 pos, string label)
    {
        var node = new Node3D { Name = $"Pin_{label}" };

        var ring = new TorusMesh
        {
            InnerRadius = 4f,
            OuterRadius = 5f,
            Rings = 16,
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
            BillboardMode = BaseMaterial3D.BillboardModeEnum.FixedY,
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
