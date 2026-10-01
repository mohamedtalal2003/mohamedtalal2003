using SmartCity.Contracts.Common;
using SmartCity.Contracts.Enums;

namespace SmartCity.Contracts.Events.Detection;

/// <summary>
/// Published by the Inventory service when a new asset is registered or an existing
/// asset's condition changes. Not published when a known asset is re-seen unchanged.
///
/// Subscribers: Cost Calculation (Broken/Missing), Work Order, Notification
/// </summary>
public record AssetAddedOrUpdated : IntegrationEvent
{
    public Guid AssetId { get; init; }
    public Guid SourceDetectionId { get; init; }
    public bool IsNewAsset { get; init; }

    public DateTimeOffset DetectedAt { get; init; }
    public GeoLocation Location { get; init; } = default!;
    public District District { get; init; }
    public string? RoadName { get; init; }

    public AssetType AssetType { get; init; }
    public AssetCategory Category { get; init; }
    public AssetCondition PreviousCondition { get; init; }   // Unspecified when IsNewAsset
    public AssetCondition CurrentCondition { get; init; }
    public double Confidence { get; init; }
    public string? AssetCode { get; init; }                   // municipality's internal id, if known

    public List<string> ImageUrls { get; init; } = new();
}
