namespace SmartCity.Contracts.Enums;

// Mirrors Protos/enums.proto (same names, same numbers).

public enum DamageStage
{
    Unspecified = 0,
    Raveling = 1,
    TransverseCracking = 2,
    BlockCracking = 3,
    FatigueCracking = 4,
    Rutting = 5,
    Depression = 6,
    Pothole = 7
}

public enum AssetCategory
{
    Unspecified = 0,
    TrafficSign = 1,
    Lighting = 2,
    SafetyElement = 3,
    UrbanFurniture = 4,
    PedestrianInfra = 5
}

public enum AssetCondition
{
    Unspecified = 0,
    Intact = 1,
    Faded = 2,
    Graffiti = 3,
    Broken = 4,
    Missing = 5
}

public enum ViolationType
{
    Unspecified = 0,
    Unauthorized = 1,
    Oversized = 2,
    IllegalPlacement = 3,
    ExpiredPermit = 4
}

public enum AnomalyType
{
    Unspecified = 0,
    MissingBarrier = 1,
    IllegalExtension = 2,
    Scaffolding = 3,
    Debris = 4
}
