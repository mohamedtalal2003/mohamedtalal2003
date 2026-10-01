using SmartCity.Contracts.Common;

namespace SmartCity.Contracts.Events.Ingestion;

/// <summary>
/// Published by Vehicle Communication after it has stored a camera frame in blob storage.
/// Consumed by AI Detection Services, which fetch the image by URL and run inference.
///
/// Carries a URL, never image bytes (keeps messages ~500 bytes).
/// CorrelationId = FrameId: this event starts the trace chain.
/// </summary>
public record FrameReceived : IntegrationEvent
{
    public Guid FrameId { get; init; }
    public string VehicleId { get; init; } = default!;
    public DateTimeOffset CapturedAt { get; init; }       // camera time on the vehicle (UTC)
    public GeoLocation Location { get; init; } = default!;
    public double SpeedKmh { get; init; }

    public string ImageUrl { get; init; } = default!;     // blob URL, e.g. frames-temp/{vehicleId}/{yyyy-MM-dd}/{frameId}.jpg
    public string ContentType { get; init; } = "image/jpeg";
    public int WidthPx { get; init; }
    public int HeightPx { get; init; }
}
