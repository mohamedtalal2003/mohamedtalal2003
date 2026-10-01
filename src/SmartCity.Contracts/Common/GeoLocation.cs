namespace SmartCity.Contracts.Common;

/// <summary>
/// GPS coordinates with optional accuracy metadata.
/// </summary>
public record GeoLocation
{
    public double Latitude { get; init; }
    public double Longitude { get; init; }
    public double? Altitude { get; init; }
    public double? AccuracyMeters { get; init; }
    public double? Heading { get; init; }
}
