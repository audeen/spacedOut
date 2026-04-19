# Asset-Pipeline

Ordner-Konventionen für alle 3D-Assets im Spiel.

## Struktur

```
Assets/
  models/           rohe .glb / .gltf Exporte (Godot importiert sie automatisch)
    asteroids/      asteroid_small_NN.glb, asteroid_medium_NN.glb, asteroid_large_NN.glb
    debris/         wreckage, floating scrap pieces
    ships/          raider_NN.glb, corsair_NN.glb, trader_NN.glb, hauler_NN.glb
    structures/     beacon.glb, station_relay.glb, exit_gate.glb
    pois/           crystal_geode.glb, rich_vein.glb, fissure_cavity.glb, drifting_pod.glb, argos_wreck.glb, …
    fx/             quellen für VFX-szenen (partikel-texturen o.ä.)
  scenes/           .tscn-Wrapper mit Collider, Lights, Scripts, VFX-Children
    ships/          ein .tscn pro Schiffs-Variante (referenziert .glb)
    structures/     beacon.tscn, station_overlay.tscn, exit_gate.tscn
    pois/           ein .tscn pro POI-Typ (enthält .glb + Drill-Socket-Nodes, Emission)
    fx/             jump_arrival.tscn, u.a.
  raw/              Blender-/Substance-Quellen (.blend, .spp). .gdignore => Godot lädt sie nicht.
```

## Regeln

- **Pools statt Einzelreferenz.** In [`Scripts/LevelGen/AssetLibrary.cs`](../Scripts/LevelGen/AssetLibrary.cs) trägt man mehrere Pfade in `ScenePaths` ein. [`AssetVariantPicker`](../Scripts/LevelGen/AssetVariantPicker.cs) wählt deterministisch pro Sector-Seed + Entity-Id.
- **Filler als `.glb`**, direkt referenziert (keine Collider-Anforderung).
- **Story-/Ship-/POI-Assets als `.tscn`** unter `Assets/scenes/`, referenzieren das `.glb` aus `Assets/models/` und hängen Collider, Lights, Emission-Shader und ggf. benannte Sockets (`DrillSocket`, `TractorSocket`, `OverlayMount`) an.
- **Pivot:** Boden/Mitte des Meshes auf Origin. Y = up. Schiffe looking down -Z (Godot-Konvention), damit die Velocity-Ausrichtung in [`Sector3DMarkers.OrientMeshToVelocity`](../Scripts/LevelGen/Sector3DMarkers.cs) passt.
- **Maßstab:** Eine Einheit = 1 m. `AssetDefinition.Radius` ist das logische Kollisions-/Spacing-Radius, `VisualScale` korrigiert Modelle, deren reale Ausdehnung davon abweicht.
- **Material:** `StandardMaterial3D`. Roughness 0.75–0.9 für Gestein, 0.4–0.6 für Metall. Emission nur für tatsächlich leuchtende Elemente.
- **Import-Settings:** Generate Tangents = off (außer für Normal-Mapping nötig). Pro `.glb` ggf. `*.import` manuell anpassen und einchecken.
- **Kein Material-Override** im .tscn wenn nicht nötig — das Originalmaterial aus dem `.glb` bleibt erhalten.

## Naming

- `asteroid_small_01.glb`, `asteroid_small_02.glb`, …
- `ship_pirate_raider_01.tscn` (scene) → lädt `ship_pirate_raider_01.glb` (model)
- FX-Scenes: snake_case, eindeutiger Zweck (`jump_arrival`, `extract_beam`).

## Verdrahtung

Asset-Pool eintragen in:

- **Statische Objekte & Marker:** [`Scripts/LevelGen/AssetLibrary.cs`](../Scripts/LevelGen/AssetLibrary.cs) → `ScenePaths = new[] { "res://Assets/scenes/..." }`
- **Schiffe:** [`Scripts/Agents/AgentDefinition.cs`](../Scripts/Agents/AgentDefinition.cs) → `ScenePaths = new[] { "res://Assets/scenes/ships/..." }`
- **Sprung-Ankunft:** Pfad ist hart verdrahtet in [`Scripts/LevelGen/LevelGenerator.cs`](../Scripts/LevelGen/LevelGenerator.cs) (`JumpArrivalScenePath`).

Fehlt ein Pfad oder lässt er sich nicht laden, fällt die Pipeline stillschweigend auf Primitive / Billboard-Blip zurück — das Spiel bleibt immer lauffähig.

## Mesh-Bundles (ein GLB → viele Pools)

Wenn ein einzelnes `.glb` viele ähnliche Meshes enthält (z.B. 100 Asteroiden als Top-Level-Kinder), lässt sich das über [`Scripts/LevelGen/MeshBundleCatalog.cs`](../Scripts/LevelGen/MeshBundleCatalog.cs) auf mehrere `AssetDefinition`-Pools gleichzeitig aufschalten — **shared**: jeder Pool sieht alle Kinder, der Unterschied liegt allein im `VisualScale` pro Pool.

```csharp
new MeshBundle {
    Id = "asteroid_field_basic",
    ScenePath = "res://Assets/models/asteroids/asteroid_pack_01/asteroid_pack_01.glb",
    PoolAssignments = new[] {
        new MeshBundlePoolAssignment { TargetAssetId = "asteroid_small",  VisualScale = 0.25f },
        new MeshBundlePoolAssignment { TargetAssetId = "asteroid_medium", VisualScale = 0.75f },
        new MeshBundlePoolAssignment { TargetAssetId = "asteroid_large",  VisualScale = 1.8f  },
    },
}
```

**Regeln:**

- **Top-Level Node3D-Kinder** im GLB mit **eindeutigen Namen** (Blender: Object-Namen). Blueprint-Empfehlung: `asteroid_001` … `asteroid_100`.
- `IncludeChildren` / `ExcludeChildren` im `MeshBundle` filtern die Namensliste bei Bedarf.
- `VisualScale` auf dem Bundle wird **multiplikativ** auf `AssetDefinition.VisualScale` angewendet. Damit bleibt die Logik (`Radius`, Spacing) der AssetDefinition erhalten, nur die Mesh-Größe ändert sich.
- Auswahl ist **deterministisch** über `seed + entity.Id` ([`AssetVariantPicker.PickRef`](../Scripts/LevelGen/AssetVariantPicker.cs)) — gleicher Seed reproduziert dieselbe Verteilung.
- Plain `ScenePaths` aus der `AssetDefinition` und Bundle-Kinder werden **kombiniert** (Union), man kann also handgebaute Szenen und Bundle-Varianten mischen.
- Bundle-Templates werden einmal pro Prozess geladen; einzelne Kinder via `Node.Duplicate()` geklont (Mesh-/Material-Ressourcen sind shared → günstig).

Dasselbe Schema lässt sich für **andere Biome** nutzen — einfach einen weiteren `MeshBundle`-Eintrag (z.B. `debris_pack_01`, `wreck_pack_01`, `station_module_pack_01`) im Catalog registrieren.
