using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace SpacedOut.Campaign;

/// <summary>
/// Generates a directed acyclic graph (DAG) of map nodes for one sector,
/// following the Slay-the-Spire / FTL pattern:
///
///   Layer 0      Layer 1      Layer 2      ...     Layer N
///   [Start] ──── [ ] ──┬──── [ ] ──────── ... ──── [Boss]
///                [ ] ──┤     [ ]
///                [ ] ──┘     [ ]
///
/// Guarantees:
///   - Every node is reachable from Start
///   - Every node has a path to Boss
///   - No isolated nodes
///   - Deterministic for a given seed
/// </summary>
public static class SectorMapGenerator
{
    private static readonly string[] Biomes =
        { "asteroid_field", "wreck_zone", "station_periphery" };

    // ── Public API ──────────────────────────────────────────────────

    public static CampaignState GenerateCampaign(int runSeed, int sectorCount = 3)
    {
        var rng = new Random(runSeed);
        var campaign = new CampaignState
        {
            RunSeed = runSeed,
            IsActive = true,
        };

        for (int i = 0; i < sectorCount; i++)
        {
            int sectorSeed = rng.Next();
            string biome = Biomes[i % Biomes.Length];
            int difficulty = i + 1;

            var sector = GenerateSector(sectorSeed, biome, difficulty, i);
            campaign.Sectors.Add(sector);
        }

        campaign.CurrentSectorIndex = 0;
        var startNode = campaign.Sectors[0].Nodes.First(n => n.Type == NodeType.Start);
        startNode.Status = NodeStatus.Current;
        startNode.IsRevealed = true;
        campaign.CurrentNodeId = startNode.Id;

        UnlockAdjacentNodes(campaign.Sectors[0], startNode.Id);

        return campaign;
    }

    public static SectorDefinition GenerateSector(
        int seed, string biomeId, int difficulty, int sectorIndex)
    {
        var rng = new Random(seed);

        int intermediateLayers = 3 + Math.Min(difficulty, 4);
        int totalLayers = intermediateLayers + 2; // +1 start, +1 boss

        var sector = new SectorDefinition
        {
            Id = $"sector_{sectorIndex}",
            DisplayName = GetSectorName(biomeId, sectorIndex, rng),
            BiomeId = biomeId,
            SectorIndex = sectorIndex,
            Difficulty = difficulty,
            Seed = seed,
            LayerCount = totalLayers,
        };

        // Layer 0: Start node
        var startNode = CreateNode(sector, 0, 0, NodeType.Start, rng, difficulty);
        startNode.Label = "Eintritt";
        startNode.Description = "Sprungpunkt – Sektor betreten";
        startNode.IsRevealed = true;

        // Intermediate layers
        for (int layer = 1; layer <= intermediateLayers; layer++)
        {
            int nodeCount = GetNodeCount(layer, intermediateLayers, rng);

            var typeWeights = GetTypeWeights(layer, intermediateLayers, difficulty);

            for (int slot = 0; slot < nodeCount; slot++)
            {
                var type = PickWeightedType(typeWeights, rng);
                var node = CreateNode(sector, layer, slot, type, rng, difficulty);
                ApplyNodeFlavor(node, type, biomeId, rng);
            }
        }

        // Final layer: Boss node
        var bossNode = CreateNode(
            sector, totalLayers - 1, 0, NodeType.Boss, rng, difficulty);
        bossNode.Label = GetBossLabel(biomeId);
        bossNode.Description = GetBossDescription(biomeId);
        bossNode.DifficultyRating = difficulty + 2;

        // Generate edges (connections between layers)
        GenerateEdges(sector, rng);

        // Compute visual positions for display
        ComputeNodePositions(sector);

        // Reveal first layer
        foreach (var n in sector.Nodes.Where(n => n.Layer <= 1))
            n.IsRevealed = true;

        return sector;
    }

    // ── Node creation ───────────────────────────────────────────────

    private static MapNode CreateNode(
        SectorDefinition sector, int layer, int slot,
        NodeType type, Random rng, int difficulty)
    {
        var node = new MapNode
        {
            Id = $"{sector.Id}_L{layer}_S{slot}",
            Layer = layer,
            SlotIndex = slot,
            Type = type,
            Seed = rng.Next(),
            DifficultyRating = Math.Max(1, difficulty + rng.Next(-1, 2)),
            Icon = GetNodeIcon(type),
        };
        sector.Nodes.Add(node);
        return node;
    }

    // ── Edge generation ─────────────────────────────────────────────

    private static void GenerateEdges(SectorDefinition sector, Random rng)
    {
        var byLayer = sector.Nodes
            .GroupBy(n => n.Layer)
            .OrderBy(g => g.Key)
            .Select(g => g.ToList())
            .ToList();

        for (int li = 0; li < byLayer.Count - 1; li++)
        {
            var currentLayer = byLayer[li];
            var nextLayer = byLayer[li + 1];

            // Track which next-layer nodes have incoming edges
            var connected = new HashSet<string>();

            foreach (var node in currentLayer)
            {
                // Each node connects to 1-2 nodes in the next layer
                int edgeCount = nextLayer.Count == 1 ? 1 : rng.Next(1, 3);
                var targets = PickTargets(node, nextLayer, edgeCount, rng);

                foreach (var target in targets)
                {
                    sector.Edges.Add(new MapEdge
                    {
                        FromNodeId = node.Id,
                        ToNodeId = target.Id,
                    });
                    connected.Add(target.Id);
                }
            }

            // Ensure every node in the next layer has at least one incoming edge
            foreach (var nextNode in nextLayer)
            {
                if (connected.Contains(nextNode.Id)) continue;

                var source = currentLayer[rng.Next(currentLayer.Count)];
                sector.Edges.Add(new MapEdge
                {
                    FromNodeId = source.Id,
                    ToNodeId = nextNode.Id,
                });
            }
        }

        // Ensure every node in non-final layers has at least one outgoing edge
        for (int li = 0; li < byLayer.Count - 1; li++)
        {
            var currentLayer = byLayer[li];
            var nextLayer = byLayer[li + 1];

            foreach (var node in currentLayer)
            {
                bool hasOutgoing = sector.Edges.Any(e => e.FromNodeId == node.Id);
                if (!hasOutgoing)
                {
                    var target = nextLayer[rng.Next(nextLayer.Count)];
                    sector.Edges.Add(new MapEdge
                    {
                        FromNodeId = node.Id,
                        ToNodeId = target.Id,
                    });
                }
            }
        }

        RemoveDuplicateEdges(sector);
        PreventCrossingEdges(sector, byLayer);
    }

    private static List<MapNode> PickTargets(
        MapNode source, List<MapNode> nextLayer, int count, Random rng)
    {
        // Prefer nodes close to the source's slot position to reduce edge crossing
        var sorted = nextLayer
            .OrderBy(n => Math.Abs(n.SlotIndex - source.SlotIndex) + rng.NextDouble() * 0.8)
            .Take(count)
            .ToList();
        return sorted;
    }

    private static void RemoveDuplicateEdges(SectorDefinition sector)
    {
        var seen = new HashSet<string>();
        sector.Edges.RemoveAll(e =>
        {
            var key = $"{e.FromNodeId}->{e.ToNodeId}";
            return !seen.Add(key);
        });
    }

    /// <summary>
    /// Reduces edge crossings by swapping slot indices when beneficial.
    /// Simple heuristic: sort nodes within each layer by average
    /// connected position in the previous layer.
    /// </summary>
    private static void PreventCrossingEdges(
        SectorDefinition sector, List<List<MapNode>> byLayer)
    {
        for (int li = 1; li < byLayer.Count; li++)
        {
            var layer = byLayer[li];
            if (layer.Count <= 1) continue;

            foreach (var node in layer)
            {
                var incomingSlots = sector.Edges
                    .Where(e => e.ToNodeId == node.Id)
                    .Select(e => sector.Nodes.First(n => n.Id == e.FromNodeId).SlotIndex)
                    .ToList();

                node.MapY = incomingSlots.Count > 0
                    ? (float)incomingSlots.Average()
                    : node.SlotIndex;
            }

            var ordered = layer.OrderBy(n => n.MapY).ToList();
            for (int i = 0; i < ordered.Count; i++)
                ordered[i].SlotIndex = i;
        }
    }

    // ── Type weighting ──────────────────────────────────────────────

    private static int GetNodeCount(int layer, int totalIntermediate, Random rng)
    {
        if (layer == 1 || layer == totalIntermediate)
            return rng.Next(2, 4);

        return rng.Next(2, 5);
    }

    private static Dictionary<NodeType, float> GetTypeWeights(
        int layer, int totalIntermediate, int difficulty)
    {
        float progress = (float)layer / totalIntermediate;

        // Early layers: more exploration, scan, navigation
        // Mid layers: encounters, distress signals
        // Late layers: elite encounters, harder content
        var weights = new Dictionary<NodeType, float>
        {
            [NodeType.Navigation] = Mathf.Lerp(30f, 15f, progress),
            [NodeType.ScanAnomaly] = Mathf.Lerp(25f, 10f, progress),
            [NodeType.DebrisField] = Mathf.Lerp(10f, 20f, progress),
            [NodeType.Encounter] = Mathf.Lerp(5f, 25f, progress),
            [NodeType.DistressSignal] = 12f,
            [NodeType.Station] = progress < 0.3f || progress > 0.8f ? 15f : 8f,
            [NodeType.EliteEncounter] = Mathf.Lerp(0f, 15f * difficulty / 3f, progress),
        };

        return weights;
    }

    private static NodeType PickWeightedType(
        Dictionary<NodeType, float> weights, Random rng)
    {
        float total = weights.Values.Sum();
        float roll = (float)(rng.NextDouble() * total);
        float acc = 0;

        foreach (var kvp in weights)
        {
            acc += kvp.Value;
            if (roll <= acc) return kvp.Key;
        }

        return NodeType.Navigation;
    }

    // ── Visual layout ───────────────────────────────────────────────

    private static void ComputeNodePositions(SectorDefinition sector)
    {
        foreach (var node in sector.Nodes)
        {
            int nodesInLayer = sector.Nodes.Count(n => n.Layer == node.Layer);
            float layerSpacing = 1000f / (sector.LayerCount + 1);
            float slotSpacing = nodesInLayer > 1
                ? 800f / (nodesInLayer + 1)
                : 0f;

            node.MapX = (node.Layer + 1) * layerSpacing;
            node.MapY = nodesInLayer > 1
                ? (node.SlotIndex + 1) * slotSpacing + 100f
                : 500f;
        }
    }

    // ── State transitions ───────────────────────────────────────────

    public static void UnlockAdjacentNodes(SectorDefinition sector, string nodeId)
    {
        var reachable = sector.Edges
            .Where(e => e.FromNodeId == nodeId)
            .Select(e => e.ToNodeId)
            .ToHashSet();

        foreach (var node in sector.Nodes)
        {
            if (reachable.Contains(node.Id) && node.Status == NodeStatus.Locked)
            {
                node.Status = NodeStatus.Available;
                node.IsRevealed = true;
            }
        }

        // Also reveal (but not unlock) nodes two layers ahead for anticipation
        var twoAhead = sector.Edges
            .Where(e => reachable.Contains(e.FromNodeId))
            .Select(e => e.ToNodeId);

        foreach (var id in twoAhead)
        {
            var node = sector.Nodes.FirstOrDefault(n => n.Id == id);
            if (node != null) node.IsRevealed = true;
        }
    }

    public static List<MapNode> GetAvailableNodes(SectorDefinition sector)
    {
        return sector.Nodes.Where(n => n.Status == NodeStatus.Available).ToList();
    }

    public static List<string> GetConnectedNodeIds(
        SectorDefinition sector, string fromNodeId)
    {
        return sector.Edges
            .Where(e => e.FromNodeId == fromNodeId)
            .Select(e => e.ToNodeId)
            .ToList();
    }

    // ── Flavor text & naming ────────────────────────────────────────

    private static void ApplyNodeFlavor(
        MapNode node, NodeType type, string biomeId, Random rng)
    {
        (node.Label, node.Description) = type switch
        {
            NodeType.Navigation => biomeId switch
            {
                "asteroid_field" => ("Durchquerung", "Navigieren Sie durch das Asteroidenfeld."),
                "wreck_zone" => ("Trümmerpassage", "Vorsichtige Passage durch Schiffstrümmer."),
                "station_periphery" => ("Frachtroute", "Folgen Sie der markierten Frachtroute."),
                _ => ("Navigation", "Standard-Routenabschnitt."),
            },
            NodeType.ScanAnomaly => PickRandom(rng,
                ("Anomalie", "Ungewöhnliche Sensorsignatur erkannt. Untersuchung empfohlen."),
                ("Energiesignatur", "Starke Energieemission aus unbekannter Quelle."),
                ("Gravitationsanomalie", "Lokale Gravitationsschwankungen stören die Sensoren.")),
            NodeType.DebrisField => PickRandom(rng,
                ("Trümmerfeld", "Dichtes Trümmerfeld – Schilde bereithalten."),
                ("Minenfeld", "Alte Munitionsreste treiben im Sektor."),
                ("Ionensturm", "Ionisierte Partikel beeinträchtigen Systeme.")),
            NodeType.Encounter => PickRandom(rng,
                ("Kontakt", "Unbekanntes Schiff auf Abfangkurs."),
                ("Patrouille", "Bewaffnete Patrouille fordert Identifikation."),
                ("Piratensignal", "Verdächtige Signale deuten auf Piratenaktivität.")),
            NodeType.DistressSignal => PickRandom(rng,
                ("Notsignal", "Automatisches Notsignal empfangen – Koordinaten berechnet."),
                ("Hilferuf", "Schwaches Kommunikationssignal: Hilfe benötigt."),
                ("Rettungskapsel", "Treibende Rettungskapsel auf Sensoren erkannt.")),
            NodeType.Station => PickRandom(rng,
                ("Versorgungsposten", "Kleine Raumstation bietet Reparatur und Vorräte."),
                ("Handelsstation", "Freihandelsstation – Upgrades verfügbar."),
                ("Reparaturdock", "Automatisiertes Dock für Notfallreparaturen.")),
            NodeType.EliteEncounter => PickRandom(rng,
                ("Schwerer Kontakt", "Großes bewaffnetes Schiff im Sektor."),
                ("Blockade", "Schwer bewaffnete Blockade versperrt den Weg."),
                ("Kriegsschiff", "Ein Kriegsschiff hält Position im Sektor.")),
            _ => ("Unbekannt", ""),
        };

        node.Reward = GenerateReward(type, rng);
    }

    private static NodeReward GenerateReward(NodeType type, Random rng)
    {
        return type switch
        {
            NodeType.Navigation => new NodeReward
            {
                ScrapGain = rng.Next(5, 15),
            },
            NodeType.ScanAnomaly => new NodeReward
            {
                ScrapGain = rng.Next(10, 25),
                FuelGain = rng.Next(0, 2),
            },
            NodeType.DebrisField => new NodeReward
            {
                ScrapGain = rng.Next(15, 30),
            },
            NodeType.Encounter => new NodeReward
            {
                ScrapGain = rng.Next(20, 40),
                FuelGain = rng.Next(1, 3),
            },
            NodeType.DistressSignal => new NodeReward
            {
                ScrapGain = rng.Next(5, 15),
                HullRepair = rng.Next(0, 2) == 0 ? 10f : 0f,
            },
            NodeType.Station => new NodeReward
            {
                HullRepair = 25f,
                FuelGain = 3,
            },
            NodeType.EliteEncounter => new NodeReward
            {
                ScrapGain = rng.Next(30, 60),
                FuelGain = rng.Next(2, 4),
                Upgrades = { PickRandomUpgrade(rng) },
            },
            NodeType.Boss => new NodeReward
            {
                ScrapGain = rng.Next(40, 80),
                FuelGain = 5,
                Upgrades = { PickRandomUpgrade(rng) },
            },
            _ => new NodeReward(),
        };
    }

    private static string PickRandomUpgrade(Random rng)
    {
        string[] upgrades =
        {
            "reinforced_hull", "advanced_sensors", "shield_capacitor",
            "engine_boost", "repair_drone", "evasion_module",
        };
        return upgrades[rng.Next(upgrades.Length)];
    }

    private static string GetSectorName(string biomeId, int index, Random rng)
    {
        string[] prefixes = { "Alpha", "Beta", "Gamma", "Delta", "Epsilon" };
        string prefix = index < prefixes.Length ? prefixes[index] : $"Sektor-{index + 1}";

        return biomeId switch
        {
            "asteroid_field" => $"{prefix}-Asteroidengürtel",
            "wreck_zone" => $"{prefix}-Wrackfeld",
            "station_periphery" => $"{prefix}-Stationsperipherie",
            _ => $"Sektor {prefix}",
        };
    }

    private static string GetBossLabel(string biomeId) => biomeId switch
    {
        "asteroid_field" => "Gravitationskern",
        "wreck_zone" => "Flaggschiff-Wrack",
        "station_periphery" => "Stationskontrolle",
        _ => "Sektor-Ausgang",
    };

    private static string GetBossDescription(string biomeId) => biomeId switch
    {
        "asteroid_field" =>
            "Ein massiver Asteroid mit starkem Gravitationsfeld blockiert den Ausgang.",
        "wreck_zone" =>
            "Das Wrack eines Flaggschiffs ist noch teilweise aktiv – Vorsicht.",
        "station_periphery" =>
            "Die Stationskontrolle verlangt Andockfreigabe unter erschwerten Bedingungen.",
        _ => "Finale Herausforderung des Sektors.",
    };

    private static string GetNodeIcon(NodeType type) => type switch
    {
        NodeType.Start => "play",
        NodeType.Navigation => "compass",
        NodeType.ScanAnomaly => "radar",
        NodeType.DebrisField => "warning",
        NodeType.Encounter => "crosshairs",
        NodeType.DistressSignal => "sos",
        NodeType.Station => "wrench",
        NodeType.EliteEncounter => "skull",
        NodeType.Boss => "crown",
        _ => "circle",
    };

    private static (string, string) PickRandom(
        Random rng, params (string, string)[] options)
    {
        return options[rng.Next(options.Length)];
    }

}
