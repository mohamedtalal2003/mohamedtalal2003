namespace SmartCity.Contracts.Enums;

/// <summary>
/// Mirrors Protos/enums.proto. Main path used by the walking skeleton:
/// Pending → Assigned → EnRoute → OnSite → (Paused ↔ OnSite) → Completed.
/// InProgress, AwaitingReview and Rejected exist in the proto for later use;
/// the skeleton's transition table does not allow entering them yet.
/// </summary>
public enum WorkOrderStatus
{
    Unspecified = 0,
    Pending = 1,
    Assigned = 2,
    InProgress = 3,
    AwaitingReview = 4,
    Completed = 5,
    Rejected = 6,
    EnRoute = 7,
    OnSite = 8,
    Paused = 9
}

public enum PauseReason
{
    Unspecified = 0,
    OutOfMaterials = 1,
    Weather = 2,
    EquipmentFailure = 3,
    SiteInaccessible = 4,
    Other = 5
}

public enum WorkOrderCategory
{
    Unspecified = 0,
    RoadRepair = 1,
    AssetReplacement = 2,
    SignageRemoval = 3,
    ConstructionInspection = 4
}
