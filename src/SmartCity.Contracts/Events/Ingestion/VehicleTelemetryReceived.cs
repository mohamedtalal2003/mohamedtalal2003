using SmartCity.Contracts.Common;

namespace SmartCity.Contracts.Events.Ingestion;

/// <summary>
/// Published by Vehicle Communication every few seconds per vehicle.
/// Used for live vehicle positions (cached in Redis) and device health.
/// CorrelationId = a new Guid per message (telemetry does not start a detection chain).
/// </summary>
public record VehicleTelemetryReceived : IntegrationEvent
{
    public string VehicleId { get; init; } = default!;
    public string VehiclePlate { get; init; } = default!;
    public DateTimeOffset CapturedAt { get; init; }
    public GeoLocation Location { get; init; } = default!;
    public double SpeedKmh { get; init; }
    public string? RouteId { get; init; }

    // Device health
    public double BatteryPercent { get; init; }
    public bool CameraOnline { get; init; }
    public string NetworkType { get; init; } = default!;  // 4G, 5G, WiFi
    public int SignalStrengthDbm { get; init; }
}
