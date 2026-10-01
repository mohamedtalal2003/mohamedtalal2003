using SmartCity.Contracts.Common;
using SmartCity.Contracts.Enums;

namespace SmartCity.Contracts.Events.Ingestion;

/// <summary>
/// Published by AI Detection Services when inference on a frame finds something.
/// One frame can produce several of these (one per detected object).
/// No detection → nothing is published.
///
/// Consumed by Pothole / Inventory / Violation / Building, each filtering by
/// DetectionType range (see DetectionTypeRanges).
///
/// CorrelationId = the FrameId it came from.
/// </summary>
public record RawDetectionReceived : IntegrationEvent
{
    public Guid DetectionId { get; init; }
    public Guid FrameId { get; init; }
    public string VehicleId { get; init; } = default!;
    public DateTimeOffset CapturedAt { get; init; }
    public GeoLocation Location { get; init; } = default!;
    public double SpeedKmh { get; init; }

    // What the cloud AI detected
    public DetectionType Type { get; init; }
    public double Confidence { get; init; }               // 0.0 – 1.0
    public string ModelName { get; init; } = default!;    // e.g. "road-damage-yolo"
    public string ModelVersion { get; init; } = default!; // e.g. "1.0.0"

    // Bounding box in the frame (normalized 0–1)
    public double BboxX { get; init; }
    public double BboxY { get; init; }
    public double BboxWidth { get; init; }
    public double BboxHeight { get; init; }

    // Evidence
    public string ImageUrl { get; init; } = default!;     // the frame's blob URL

    // Model-specific extras (e.g. "ocr_text", "estimated_depth_cm")
    public Dictionary<string, string>? Metadata { get; init; }
}
