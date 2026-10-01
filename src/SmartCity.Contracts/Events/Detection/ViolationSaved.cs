using SmartCity.Contracts.Common;
using SmartCity.Contracts.Enums;

namespace SmartCity.Contracts.Events.Detection;

/// <summary>
/// Published by the Violation service when an unauthorized, oversized, illegally
/// placed, or expired-permit sign/banner is found.
/// Subscribers: Work Order, Notification
/// </summary>
public record ViolationSaved : IntegrationEvent
{
    public Guid ViolationId { get; init; }
    public Guid SourceDetectionId { get; init; }

    public DateTimeOffset DetectedAt { get; init; }
    public GeoLocation Location { get; init; } = default!;
    public District District { get; init; }

    public ViolationType ViolationType { get; init; }
    public Dimensions? EstimatedSize { get; init; }
    public string? DetectedText { get; init; }                // OCR from the sign, if any
    public string? BusinessName { get; init; }
    public double Confidence { get; init; }

    // Permit check result
    public bool PermitFound { get; init; }
    public string? PermitNumber { get; init; }
    public DateTimeOffset? PermitExpiryDate { get; init; }
    public string? ViolatedRuleCode { get; init; }

    public List<string> ImageUrls { get; init; } = new();
}
