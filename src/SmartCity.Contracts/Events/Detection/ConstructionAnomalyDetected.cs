using SmartCity.Contracts.Common;
using SmartCity.Contracts.Enums;

namespace SmartCity.Contracts.Events.Detection;

/// <summary>
/// Published by the Building service when a construction-site anomaly is found.
/// Subscribers: Work Order, Notification
/// </summary>
public record ConstructionAnomalyDetected : IntegrationEvent
{
    public Guid AnomalyId { get; init; }
    public Guid SourceDetectionId { get; init; }

    public DateTimeOffset DetectedAt { get; init; }
    public GeoLocation Location { get; init; } = default!;
    public District District { get; init; }
    public string? Address { get; init; }

    public AnomalyType AnomalyType { get; init; }
    public SeverityLevel Severity { get; init; }
    public double Confidence { get; init; }
    public string? Description { get; init; }

    public string? SitePermitNumber { get; init; }
    public string? ContractorName { get; init; }

    public List<string> ImageUrls { get; init; } = new();
}
