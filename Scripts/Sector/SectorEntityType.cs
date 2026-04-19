namespace SpacedOut.Sector;

public enum SectorEntityType
{
    // Ambient (3D only, Nearfield on Tactical)
    AsteroidSmall,
    AsteroidMedium,
    DebrisCluster,
    CargoCluster,

    // Landmarks (Point on map)
    AsteroidLarge,
    WreckMain,
    StationCore,

    // Structures (Point on map)
    StationModule,
    WreckMedium,

    // Markers / POI (Point on map)
    Beacon,
    ResourceNode,
    PoiMarker,
    LootMarker,
    UtilityNode,
    ExitMarker,
    EncounterMarker,
    MissionStructure,

    // Dynamic contacts (Point on map)
    PatrolDrone,
    HostileShip,
    NeutralShip,

    // Resource zones (Zone on map, Phase 2)
    ResourceField,
}
