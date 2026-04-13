using Godot;

namespace SpacedOut.Shared;

/// <summary>
/// Converts between 3D map space (0–1000 per axis, center at 500,500,500)
/// and 3D world space. Map-Z represents altitude and maps to world-Y.
/// Shared by GameManager (bridge camera) and MissionGenerator (level→map).
/// </summary>
public static class CoordinateMapper
{
    private const float MapCenter = 500f;

    /// <summary>Full 3D mapping: map (X,Y,Z) → world (X,Y,Z) where map-Z becomes world-Y (altitude).</summary>
    public static Vector3 MapToWorld(float mapX, float mapY, float mapZ, float levelRadius)
    {
        float x = (mapX - MapCenter) * levelRadius / MapCenter;
        float z = (mapY - MapCenter) * levelRadius / MapCenter;
        float y = (mapZ - MapCenter) * levelRadius / MapCenter;
        return new Vector3(x, y, z);
    }

    /// <summary>2D overload for callers that don't use altitude (defaults to map-Z = 500 → world-Y = 0).</summary>
    public static Vector3 MapToWorld(float mapX, float mapY, float levelRadius)
    {
        return MapToWorld(mapX, mapY, MapCenter, levelRadius);
    }

    /// <summary>Full 3D inverse: world (X,Y,Z) → map (X,Y,Z) including altitude.</summary>
    public static Vector3 WorldToMap3D(Vector3 worldPos, float levelRadius)
    {
        float mapScale = MapCenter / levelRadius;
        return new Vector3(
            worldPos.X * mapScale + MapCenter,
            worldPos.Z * mapScale + MapCenter,
            worldPos.Y * mapScale + MapCenter);
    }

    /// <summary>2D inverse (ignores world-Y altitude), kept for backward compatibility.</summary>
    public static Vector2 WorldToMap(Vector3 worldPos, float levelRadius)
    {
        float mapScale = MapCenter / levelRadius;
        return new Vector2(
            worldPos.X * mapScale + MapCenter,
            worldPos.Z * mapScale + MapCenter);
    }
}
