using SmartCity.Contracts.Common;
using SmartCity.Contracts.Enums;

namespace SmartCity.Contracts.Events.Detection;

/// <summary>
/// Published by the Pothole service after a road-damage detection is stored.
/// IsNewPothole = false means a re-sighting was merged into an existing pothole
/// (consumers like Work Order must NOT create a second ticket in that case).
///
/// Walking skeleton: no clustering yet, so IsNewPothole is always true and
/// DetectionCount is always 1.
///
/// Subscribers: Cost Calculation, Work Order, Notification
/// </summary>
public record PotholeSaved : IntegrationEvent
{
    public Guid PotholeId { get; init; }
    public Guid SourceDetectionId { get; init; }
    public bool IsNewPothole { get; init; } = true;
    public int DetectionCount { get; init; } = 1;

    public DateTimeOffset FirstSeenAt { get; init; }
    public DateTimeOffset LastSeenAt { get; init; }
    public GeoLocation Location { get; init; } = default!;
    public District District { get; init; }
    public string? RoadName { get; init; }

    public DetectionType DetectionType { get; init; }     // Pothole / RoadCrack / SurfaceDamage
    public DamageStage Stage { get; init; }
    public SeverityLevel Severity { get; init; }
    public Dimensions? EstimatedSize { get; init; }
    public double Confidence { get; init; }

    public List<string> ImageUrls { get; init; } = new();
}
