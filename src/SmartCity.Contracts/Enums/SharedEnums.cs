namespace SmartCity.Contracts.Enums;

// IMPORTANT: names and numeric values mirror Protos/enums.proto exactly,
// so the same integer means the same thing in events, gRPC, and the database.
// Change both files together or not at all.

public enum District
{
    Unspecified = 0,
    Meram = 1,
    Selcuklu = 2,
    Karatay = 3
}

public enum SourceEntityType
{
    Unspecified = 0,
    RoadDamage = 1,
    AssetDamage = 2,
    Violation = 3,
    BuildingAnomaly = 4
}

public enum PriorityLevel
{
    Unspecified = 0,
    Critical = 1,
    High = 2,
    Medium = 3,
    Routine = 4
}

public enum SeverityLevel
{
    Unspecified = 0,
    Low = 1,
    Medium = 2,
    High = 3,
    Critical = 4
}
