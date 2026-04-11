using System.Collections.Generic;
using System.Linq;
using Godot;

namespace SpacedOut.LevelGen;

public static class SpawnValidator
{
    /// <summary>
    /// Returns true when <paramref name="position"/> is far enough from
    /// the spawn point and every already-placed object.
    /// </summary>
    public static bool CanPlace(
        Vector3 position,
        float radius,
        float minSpacing,
        IReadOnlyList<SpawnedObject> existing,
        Vector3 spawnPoint,
        float spawnSafeRadius)
    {
        if (position.DistanceTo(spawnPoint) < spawnSafeRadius)
            return false;

        foreach (var obj in existing)
        {
            float minDist = radius + obj.ObjectRadius + minSpacing;
            if (position.DistanceTo(obj.Position) < minDist)
                return false;
        }

        return true;
    }

    /// <summary>
    /// Post-generation validation.  Returns a list of human-readable
    /// warning / error strings (empty ⇒ valid).
    /// </summary>
    public static List<string> ValidateLevel(
        IReadOnlyList<SpawnedObject> objects,
        Vector3 spawnPoint,
        Vector3 exitPoint,
        float spawnSafeRadius,
        float levelRadius)
    {
        var msgs = new List<string>();

        // Spawn area must be clear
        int blockingSpawn = objects.Count(o =>
            o.Position.DistanceTo(spawnPoint) < spawnSafeRadius);
        if (blockingSpawn > 0)
            msgs.Add($"{blockingSpawn} Objekt(e) blockieren Spawn-Zone");

        // At least one landmark
        if (!objects.Any(o => o.IsLandmark))
            msgs.Add("Keine Landmarke platziert");

        // Exit marker present
        if (!objects.Any(o => o.Category == AssetCategory.ExitMarker))
            msgs.Add("Kein Exit-Marker platziert");

        // Enough total objects for a readable scene
        if (objects.Count < 10)
            msgs.Add($"Zu wenige Objekte ({objects.Count})");

        // Objects inside level bounds
        int outside = objects.Count(o =>
            o.Position.Length() > levelRadius * 1.1f);
        if (outside > 0)
            msgs.Add($"{outside} Objekt(e) außerhalb der Levelgrenzen");

        // Spawn → exit path not fully blocked (rough heuristic:
        // check that a cylinder along spawn→exit is not too dense)
        var dir = (exitPoint - spawnPoint).Normalized();
        float pathLen = spawnPoint.DistanceTo(exitPoint);
        int pathBlockers = 0;
        foreach (var obj in objects)
        {
            var toObj = obj.Position - spawnPoint;
            float proj = toObj.Dot(dir);
            if (proj < 0 || proj > pathLen) continue;
            var closest = spawnPoint + dir * proj;
            float dist = obj.Position.DistanceTo(closest);
            if (dist < obj.ObjectRadius + 10f)
                pathBlockers++;
        }

        if (pathBlockers > objects.Count * 0.4f)
            msgs.Add("Navigationsweg Spawn → Exit stark blockiert");

        return msgs;
    }
}
