using SmartCity.Contracts.Enums;

namespace SmartCity.Contracts.Events.Business;

/// <summary>
/// Published by Cost Calculation after estimating repair cost for a saved detection
/// entity (pothole, asset, violation, anomaly).
/// Estimates are snapshots: a later price change produces a NEW estimate event,
/// it never mutates an old one.
///
/// Subscribers: Work Order (attaches cost), Notification
/// </summary>
public record CostEstimateGenerated : IntegrationEvent
{
    public Guid EstimateId { get; init; }
    public Guid SourceDetectionId { get; init; }

    // What the estimate is for
    public Guid SourceEntityId { get; init; }               // PotholeId, AssetId, ...
    public SourceEntityType SourceEntityType { get; init; }

    public decimal MaterialCost { get; init; }
    public decimal LaborCost { get; init; }
    public decimal EquipmentCost { get; init; }
    public decimal TotalEstimatedCost { get; init; }
    public string Currency { get; init; } = "TRY";

    public List<MaterialItem> Materials { get; init; } = new();
    public double EstimatedLaborHours { get; init; }
    public int EstimatedCrewSize { get; init; }
    public PriorityLevel SuggestedPriority { get; init; }
}

public record MaterialItem
{
    public string Name { get; init; } = default!;           // "Asfalt", "Beton"
    public double Quantity { get; init; }
    public string Unit { get; init; } = default!;           // "m³", "kg", "adet"
    public decimal UnitCost { get; init; }
    public decimal TotalCost { get; init; }
}
