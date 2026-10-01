namespace SmartCity.Contracts.Enums;

/// <summary>
/// Category reported by the cloud AI. Numeric ranges route the detection:
/// 100s → Pothole, 200s → Inventory, 300s → Violation, 400s → Building.
/// Consumers filter with DetectionTypeRanges below.
/// </summary>
public enum DetectionType
{
    Unspecified = 0,

    // → Pothole service
    Pothole = 100,
    RoadCrack = 101,
    SurfaceDamage = 102,

    // → Inventory service
    TrafficLight = 200,
    TrafficSign = 201,
    StreetLight = 202,
    Barrier = 203,
    StreetFurniture = 204,

    // → Violation service
    CommercialSign = 300,
    Banner = 301,
    Billboard = 302,

    // → Building service
    Scaffolding = 400,
    ConstructionSite = 401,
    BuildingFacade = 402
}

public static class DetectionTypeRanges
{
    public static bool IsRoadDamage(this DetectionType t) => (int)t is >= 100 and < 200;
    public static bool IsAsset(this DetectionType t)      => (int)t is >= 200 and < 300;
    public static bool IsViolation(this DetectionType t)  => (int)t is >= 300 and < 400;
    public static bool IsBuilding(this DetectionType t)   => (int)t is >= 400 and < 500;
}
