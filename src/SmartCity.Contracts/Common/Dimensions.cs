namespace SmartCity.Contracts.Common;

/// <summary>
/// Physical dimensions estimated from AI detection (in meters).
/// </summary>
public record Dimensions
{
    public double? WidthMeters { get; init; }
    public double? HeightMeters { get; init; }
    public double? DepthMeters { get; init; }
    public double? AreaSquareMeters { get; init; }
}
