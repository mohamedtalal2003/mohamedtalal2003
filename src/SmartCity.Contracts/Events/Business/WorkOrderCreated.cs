using SmartCity.Contracts.Common;
using SmartCity.Contracts.Enums;

namespace SmartCity.Contracts.Events.Business;

/// <summary>
/// Published by Work Order when a ticket is created (automatically from a saved
/// detection, or manually by an officer).
/// Subscribers: Notification
/// </summary>
public record WorkOrderCreated : IntegrationEvent
{
    public Guid WorkOrderId { get; init; }
    public string TicketNumber { get; init; } = default!;  // "WO-2026-000123"
    public Guid SourceDetectionId { get; init; }           // Guid.Empty when created manually

    public Guid SourceEntityId { get; init; }
    public SourceEntityType SourceEntityType { get; init; }

    public GeoLocation Location { get; init; } = default!;
    public District District { get; init; }

    public WorkOrderCategory Category { get; init; }
    public PriorityLevel Priority { get; init; }
    public WorkOrderStatus Status { get; init; } = WorkOrderStatus.Pending;

    public string Title { get; init; } = default!;
    public string Description { get; init; } = default!;
    public DateTimeOffset? SlaDueAt { get; init; }
    public decimal? EstimatedCost { get; init; }
    public string CreatedBy { get; init; } = "system";
}
