using SmartCity.Contracts.Enums;

namespace SmartCity.Contracts.Events.Business;

/// <summary>
/// Published by Work Order on every lifecycle transition
/// (the aggregate's TransitionTo method raises it).
/// Subscribers: Notification; later, detection services to mark items fixed on Completed.
/// </summary>
public record WorkOrderStatusChanged : IntegrationEvent
{
    public Guid WorkOrderId { get; init; }
    public string TicketNumber { get; init; } = default!;
    public Guid SourceDetectionId { get; init; }
    public Guid SourceEntityId { get; init; }
    public SourceEntityType SourceEntityType { get; init; }

    public WorkOrderStatus PreviousStatus { get; init; }
    public WorkOrderStatus NewStatus { get; init; }
    public string ChangedBy { get; init; } = default!;      // user id or "system"

    public PriorityLevel Priority { get; init; }
    public string? AssignedCrewId { get; init; }
    public PauseReason? PauseReason { get; init; }          // set only when NewStatus = Paused
    public string? Notes { get; init; }

    public bool SlaBreached { get; init; }
    public TimeSpan TimeInPreviousStatus { get; init; }
}
