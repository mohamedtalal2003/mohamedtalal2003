namespace SmartCity.Contracts.Enums;

/// <summary>Fine-grained asset kind. Each maps to one AssetCategory.</summary>
public enum AssetType
{
    Unspecified = 0,
    TrafficLight = 1,
    StopSign = 2,
    SpeedLimitSign = 3,
    WarningSign = 4,
    StreetLight = 5,
    Barrier = 6,
    Bollard = 7,
    Bench = 8,
    TrashBin = 9,
    BusStop = 10
}
