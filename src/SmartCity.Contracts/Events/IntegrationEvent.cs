namespace SmartCity.Contracts.Events;

/// <summary>
/// Envelope shared by every integration event in the system.
///
/// - EventId:       unique per event instance. Consumers store processed EventIds
///                  to ignore duplicate deliveries (RabbitMQ is at-least-once).
/// - OccurredAt:    when the fact happened, always UTC.
/// - CorrelationId: the FrameId that started the chain. Copied unchanged onto every
///                  downstream event, so one id traces frame → detection → pothole
///                  → cost → work order → notification.
/// - SchemaVersion: bumped only for breaking changes (see CONTRACT RULES in README).
///
/// NOTE for bus configuration: exclude this base type from the broker topology
/// (cfg.Publish&lt;IntegrationEvent&gt;(p =&gt; p.Exclude = true)) so MassTransit does
/// not create a catch-all exchange for it.
/// </summary>
public abstract record IntegrationEvent
{
    public Guid EventId { get; init; } = Guid.NewGuid();
    public DateTimeOffset OccurredAt { get; init; } = DateTimeOffset.UtcNow;
    public Guid CorrelationId { get; init; }
    public int SchemaVersion { get; init; } = 1;
}
