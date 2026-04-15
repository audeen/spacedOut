using System;
using System.Collections.Generic;
using SpacedOut.Run;
using SpacedOut.State;

namespace SpacedOut.Agents;

public class AgentSpawnProfile
{
    public string AgentType { get; init; } = "";
    public int CountMin { get; init; }
    public int CountMax { get; init; }
    public AgentBehaviorMode InitialMode { get; init; }
    /// <summary>Spawn distance relative to LevelRadius (map-space).</summary>
    public float SpawnRadiusFactor { get; init; } = 0.5f;
    /// <summary>When true, spawns near the landmark/guard point instead of random position.</summary>
    public bool SpawnNearLandmark { get; init; }
}

/// <summary>
/// Provides spawn profiles per biome and run-node type combination.
/// </summary>
public static class AgentSpawnConfig
{
    public static List<AgentSpawnProfile> GetProfiles(string biomeId, RunNodeType? nodeType)
    {
        bool hostile = nodeType == RunNodeType.Hostile;
        bool station = nodeType == RunNodeType.Station;

        return biomeId switch
        {
            "asteroid_field" => AsteroidField(hostile, station),
            "wreck_zone" => WreckZone(hostile, station),
            "station_periphery" => StationPeriphery(hostile, station),
            _ => DefaultProfiles(hostile, station),
        };
    }

    private static List<AgentSpawnProfile> AsteroidField(bool hostile, bool station)
    {
        var profiles = new List<AgentSpawnProfile>();

        if (station)
        {
            profiles.Add(new AgentSpawnProfile
            {
                AgentType = "trader_ship", CountMin = 1, CountMax = 2,
                InitialMode = AgentBehaviorMode.Transit, SpawnRadiusFactor = 0.8f,
            });
            profiles.Add(new AgentSpawnProfile
            {
                AgentType = "cargo_hauler", CountMin = 1, CountMax = 1,
                InitialMode = AgentBehaviorMode.Transit, SpawnRadiusFactor = 0.7f,
            });
            return profiles;
        }

        profiles.Add(new AgentSpawnProfile
        {
            AgentType = "trader_ship", CountMin = 0, CountMax = 1,
            InitialMode = AgentBehaviorMode.Transit, SpawnRadiusFactor = 0.8f,
        });

        if (hostile)
        {
            profiles.Add(new AgentSpawnProfile
            {
                AgentType = "pirate_raider", CountMin = 1, CountMax = 2,
                InitialMode = AgentBehaviorMode.Patrol, SpawnRadiusFactor = 0.45f,
            });
            profiles.Add(new AgentSpawnProfile
            {
                AgentType = "pirate_corsair", CountMin = 0, CountMax = 1,
                InitialMode = AgentBehaviorMode.Guard, SpawnRadiusFactor = 0.3f,
                SpawnNearLandmark = true,
            });
        }

        return profiles;
    }

    private static List<AgentSpawnProfile> WreckZone(bool hostile, bool station)
    {
        var profiles = new List<AgentSpawnProfile>();

        if (station)
        {
            profiles.Add(new AgentSpawnProfile
            {
                AgentType = "trader_ship", CountMin = 1, CountMax = 2,
                InitialMode = AgentBehaviorMode.Transit, SpawnRadiusFactor = 0.8f,
            });
            profiles.Add(new AgentSpawnProfile
            {
                AgentType = "cargo_hauler", CountMin = 1, CountMax = 1,
                InitialMode = AgentBehaviorMode.Transit, SpawnRadiusFactor = 0.7f,
            });
            return profiles;
        }

        profiles.Add(new AgentSpawnProfile
        {
            AgentType = "cargo_hauler", CountMin = 0, CountMax = 1,
            InitialMode = AgentBehaviorMode.Transit, SpawnRadiusFactor = 0.8f,
        });

        if (hostile)
        {
            profiles.Add(new AgentSpawnProfile
            {
                AgentType = "pirate_raider", CountMin = 2, CountMax = 3,
                InitialMode = AgentBehaviorMode.Patrol, SpawnRadiusFactor = 0.45f,
            });
            profiles.Add(new AgentSpawnProfile
            {
                AgentType = "pirate_corsair", CountMin = 1, CountMax = 1,
                InitialMode = AgentBehaviorMode.Guard, SpawnRadiusFactor = 0.3f,
                SpawnNearLandmark = true,
            });
        }

        return profiles;
    }

    private static List<AgentSpawnProfile> StationPeriphery(bool hostile, bool station)
    {
        var profiles = new List<AgentSpawnProfile>();

        profiles.Add(new AgentSpawnProfile
        {
            AgentType = "trader_ship", CountMin = 1, CountMax = 2,
            InitialMode = AgentBehaviorMode.Transit, SpawnRadiusFactor = 0.8f,
        });
        profiles.Add(new AgentSpawnProfile
        {
            AgentType = "cargo_hauler", CountMin = 0, CountMax = 1,
            InitialMode = AgentBehaviorMode.Transit, SpawnRadiusFactor = 0.7f,
        });

        if (hostile && !station)
        {
            profiles.Add(new AgentSpawnProfile
            {
                AgentType = "pirate_raider", CountMin = 1, CountMax = 1,
                InitialMode = AgentBehaviorMode.Patrol, SpawnRadiusFactor = 0.5f,
            });
        }

        return profiles;
    }

    private static List<AgentSpawnProfile> DefaultProfiles(bool hostile, bool station)
    {
        var profiles = new List<AgentSpawnProfile>();

        profiles.Add(new AgentSpawnProfile
        {
            AgentType = "trader_ship", CountMin = 0, CountMax = 1,
            InitialMode = AgentBehaviorMode.Transit, SpawnRadiusFactor = 0.8f,
        });

        if (hostile)
        {
            profiles.Add(new AgentSpawnProfile
            {
                AgentType = "pirate_raider", CountMin = 1, CountMax = 2,
                InitialMode = AgentBehaviorMode.Patrol, SpawnRadiusFactor = 0.45f,
            });
        }

        return profiles;
    }
}
