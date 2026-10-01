# Walking skeleton — build plan

Goal: a frame published by a simulated vehicle ends up as a work order that an officer can read
through the Gateway, and the work order can be traced back to the original image in MinIO.

Build the milestones **in order**. Each ends with **Done when** checks; run them and report the
results before moving on. Everything marked *stub* gets a `// SKELETON:` comment.

---

## Target solution layout

```
SmartCity.sln
src/
  SmartCity.Contracts/                 (exists — events, enums, protos, mock JSON)
  SmartCity.Contracts.Grpc/            generated gRPC client+server code from Contracts/Protos
  BuildingBlocks/
    SmartCity.BuildingBlocks/          shared plumbing: bus, logging, health, outbox, idempotency
  Services/
    VehicleCommunication/SmartCity.VehicleCommunication/
    AiDetection/SmartCity.AiDetection/
    Pothole/SmartCity.Pothole.Api/
    Inventory/SmartCity.Inventory.Api/
    Violation/SmartCity.Violation.Api/
    Building/SmartCity.Building.Api/
    CostCalculation/SmartCity.CostCalculation.Api/
    WorkOrder/SmartCity.WorkOrder.Domain/      (aggregate, no infrastructure references)
    WorkOrder/SmartCity.WorkOrder.Api/
    Notification/SmartCity.Notification.Api/
  Gateway/SmartCity.ApiGateway/
tools/
  VehicleSimulator/                    console app publishing fake frames + telemetry over MQTT
tests/
  SmartCity.WorkOrder.Domain.Tests/    unit tests for the state machine
  SmartCity.EndToEnd.Tests/            runs against the live local stack
```

One project per service is fine for the skeleton (folders inside for `Consumers/`, `Grpc/`,
`Data/`, `Domain/`). Only Work Order gets a separate Domain project, because it is the DDD aggregate.

---

## M0 — Solution, shared plumbing, infrastructure

1. `docker compose up -d`; confirm all containers healthy.
2. Create `SmartCity.sln` and add the existing `src/SmartCity.Contracts` project.
3. Create `SmartCity.Contracts.Grpc` (Grpc.Tools, Google.Protobuf, Grpc.AspNetCore.Server,
   Grpc.Net.Client) that compiles the protos from the Contracts project:
   ```xml
   <Protobuf Include="..\SmartCity.Contracts\Protos\*.proto"
             ProtoRoot="..\SmartCity.Contracts\Protos" GrpcServices="Both" />
   ```
   The protos have no `csharp_namespace`, so generated types land in `Smartcity.Common.V1`,
   `Smartcity.Enums.V1`, `Smartcity.Queries.V1`, `Smartcity.Commands.V1`. Leave it that way
   (do not edit protos without asking).
4. Create `SmartCity.BuildingBlocks` with extension methods every service calls in `Program.cs`:
   - `AddSmartCityLogging()` — Serilog, console + Seq, enrich with service name.
   - `AddSmartCityBus(configureConsumers)` — MassTransit 8 + RabbitMQ:
     - `cfg.Publish<IntegrationEvent>(p => p.Exclude = true)` (no catch-all exchange for the base type)
     - System.Text.Json with `JsonStringEnumConverter`
     - retry `Intervals(1s, 2s, 4s)` on every receive endpoint; failures go to MassTransit's `_error` queue (our DLQ)
     - a consume filter that pushes `CorrelationId` from the message into the Serilog log context
     - kebab-case endpoint names, one queue per consumer per service (e.g. `pothole-raw-detection`)
   - `AddSmartCityOutbox<TDbContext>()` — MassTransit EF Core outbox + inbox (Postgres lock provider).
   - `PublishFollowUp<T>(...)` helper (or a documented pattern) that sets `MessageId = EventId`
     and enforces copying `CorrelationId` from the consumed message.
   - `AddSmartCityHealthChecks()` mapping `/health/live` and `/health/ready`.
   - Kestrel config helper for the two-port setup (HTTP/1 + HTTP/2 gRPC) from CLAUDE.md.
5. Empty `Program.cs` for every service + Gateway, each wired with the building blocks and
   listening on its ports.

**Done when**
- `dotnet build SmartCity.sln` passes with zero errors.
- Every service starts; `curl localhost:<http-port>/health/ready` returns Healthy for each.
- RabbitMQ UI shows a connection per running service.
- Seq shows startup logs from every service.

---

## M1 — Ingestion: simulator → EMQX → Vehicle Communication → MinIO + events

### tools/VehicleSimulator (console)
- Args: `--vehicles N` (default 5), `--interval-ms` (default 2000), `--frames K` (stop after K
  frames per vehicle; default infinite).
- Vehicle ids `MUN-VEH-001…`, plates `42 ABC 001…`. Each vehicle drives a small loop of GPS points
  inside Konya's central districts (Meram ~37.85,32.43 · Selçuklu ~37.94,32.50 · Karatay ~37.87,32.52).
- Per tick publish two MQTT v5 messages, QoS 1:
  - `vehicles/{vehicleId}/frames` — **payload = raw image bytes**; metadata in MQTT v5 user
    properties: `frameId`, `capturedAt` (ISO UTC), `lat`, `lon`, `accuracy`, `heading`, `speed`,
    `width`, `height`, `contentType`.
    *stub:* the image is a small generated placeholder JPEG (any valid tiny JPEG embedded as a
    resource is fine).
  - `vehicles/{vehicleId}/telemetry` — JSON: plate, location, speed, routeId, battery, cameraOnline,
    networkType, signalDbm, capturedAt.
- Print each `frameId` to the console so a trace can be started from it.

### Vehicle Communication (stateless)
- MQTTnet client subscribes with **shared subscriptions** so several instances can share load:
  `$share/vehiclecomm/vehicles/+/frames` and `$share/vehiclecomm/vehicles/+/telemetry`.
- Frame handling:
  1. Validate: vehicleId in topic is well formed; required user properties exist; lat/lon inside a
     Konya bounding box (lat 37.6–38.2, lon 32.2–32.8). Invalid → log a warning with reason, drop.
  2. Upload to MinIO bucket `frames-temp`, key `{vehicleId}/{yyyy-MM-dd}/{frameId}.jpg`
     (AWSSDK.S3 with `ServiceURL=http://localhost:9000`, `ForcePathStyle=true`).
  3. Publish `FrameReceived` with `CorrelationId = FrameId`, `ImageUrl` = public object URL.
- Telemetry handling: publish `VehicleTelemetryReceived` (new CorrelationId) **and** write the live
  position to Redis hash `vehicle:pos:{vehicleId}` (lat, lon, speed, heading, at) with 60 s TTL.
- No database. No outbox needed (nothing to commit atomically); publish directly.

**Done when**
- Simulator with `--vehicles 3 --frames 5` → MinIO console shows 15 objects under `frames-temp`.
- RabbitMQ UI: `FrameReceived` and `VehicleTelemetryReceived` exchanges exist and received messages.
- `redis-cli HGETALL vehicle:pos:MUN-VEH-001` returns a position.
- Publishing a frame with lat 0, lon 0 is dropped with a warning in Seq.

---

## M2 — Fake AI and detection routing

### AiDetection (stateless) — *stub model*
- Consumes `FrameReceived`. Downloads the image by URL (proves the URL works), then fakes inference:
  - Config `FakeAi:DetectionRate` (default 0.02) and `FakeAi:ForceDetection` (bool, for tests).
  - On "detection", emit one `RawDetectionReceived` with `Type` mostly `Pothole`/`RoadCrack`; with
    config `FakeAi:EmitAllTypes=true`, also occasionally emit 200s/300s/400s types to prove routing.
  - `ModelName = "fake-ai"`, `ModelVersion = "0.1.0-skeleton"`, random bbox, confidence 0.6–0.98,
    `Metadata["estimated_depth_cm"]` random 2–12 for road damage.
  - `DetectionId = new Guid`, `FrameId` and `CorrelationId` copied from the frame.
- No detection → publish nothing.

### Inventory, Violation, Building — *stub consumers*
- Each consumes `RawDetectionReceived`, filters with `IsAsset()/IsViolation()/IsBuilding()`
  (ignore others), logs `"received {Type} for detection {DetectionId} — not implemented"`.
- Each has its DbContext + outbox tables migrated (so the template is ready), but stores nothing yet.

**Done when**
- With `ForceDetection=true` and `EmitAllTypes=true`, Seq shows each detection type reaching
  exactly one detection service (and being ignored by the other three).
- Every log line of a single chain shares one `CorrelationId`; the Seq query
  `CorrelationId = '<frameId>'` shows Vehicle Communication → AiDetection → one detection service.

---

## M3 — The business chain: Pothole → Cost → Work Order → Notification

### Pothole
- EF Core + NetTopologySuite. Table `potholes`: id, source_detection_id (unique), correlation_id,
  location `geography(Point,4326)` with **GIST index**, district, road_name (null), detection_type,
  stage, severity, confidence, detection_count, first_seen_at, last_seen_at, status
  (Open/Fixed), image_urls (text[]).
- Consumer for `RawDetectionReceived` (`IsRoadDamage()` only):
  - Insert a new pothole every time. *stub:* no clustering yet (real version: `ST_DWithin` 3–5 m).
  - *stub:* district from rough bounding boxes of the three districts (real: PostGIS polygons).
  - *stub:* severity from `estimated_depth_cm` (<4 Low, 4–7 Medium, 7–10 High, >10 Critical);
    stage = `Pothole`.
  - Publish `PotholeSaved` (IsNewPothole=true) through the outbox, in the same transaction.
- gRPC `RoadDamageQueryService`: `GetDamageById`, `ListDamages` (support the bounding-box filter
  with `ST_Intersects`/`&&` against the GIST index, plus district + pagination). Other RPCs →
  `UNIMPLEMENTED`.

### Cost Calculation
- Consumes `PotholeSaved` where `IsNewPothole`.
- *stub:* fixed price table by severity (TRY): Low 1,000 · Medium 1,800 · High 2,500 ·
  Critical 4,000; split 36% material / 48% labor / 16% equipment; one material line "Soğuk asfalt".
- Stores the estimate (table `cost_estimates`) and publishes `CostEstimateGenerated` via outbox.
- gRPC `CostQueryService.GetEstimateForEntity`.

### Work Order — DDD aggregate (real, not a stub)
`SmartCity.WorkOrder.Domain` (no EF/MassTransit references):
- `WorkOrder` aggregate root: Id, TicketNumber, SourceDetectionId, SourceEntityId, SourceEntityType,
  CorrelationId, Location, District, Category, Priority, Status, AssignedCrewId, EstimatedCost,
  SlaDueAt, CreatedAt; private list of `StatusHistoryEntry`; private list of domain events.
- Transition table (only these are allowed in the skeleton):
  ```
  Pending  → Assigned
  Assigned → EnRoute, Pending (unassign)
  EnRoute  → OnSite
  OnSite   → Paused, Completed
  Paused   → OnSite
  ```
- Methods: `Create(...)`, `AssignCrew(crewId, by)`, `MarkEnRoute(by)`, `MarkOnSite(by)`,
  `Pause(reason, by)`, `Resume(by)`, `Complete(proofUrl, by)`, `AttachCostEstimate(amount)`.
  Each goes through one private `TransitionTo(...)` that checks the table, appends history, and
  raises a domain event; illegal transitions throw `InvalidStateTransitionException`.
- SLA by priority: Critical 24 h, High 48 h, Medium 5 days, Routine 10 days.
- Ticket number `WO-{yyyy}-{6-digit sequence}` from a Postgres sequence.

`SmartCity.WorkOrder.Api`:
- Tables: `work_orders`, `work_order_status_history`, `pending_cost_estimates`, outbox/inbox tables.
- Consumers:
  - `PotholeSaved` (IsNewPothole) → create a Pending work order (unique on SourceEntityId, so a
    redelivery cannot create a second one). Category RoadRepair, priority from severity.
  - `CostEstimateGenerated` → attach to the work order with that SourceEntityId. **The estimate can
    arrive before the work order exists** (Cost and Work Order consume `PotholeSaved` in parallel):
    if no work order yet, store it in `pending_cost_estimates`; on work order creation, pick it up.
  - *stub:* consumers for `AssetAddedOrUpdated`, `ViolationSaved`, `ConstructionAnomalyDetected`
    that only log.
- After saving, map domain events to `WorkOrderCreated` / `WorkOrderStatusChanged` and publish via
  outbox (copy CorrelationId + SourceDetectionId).
- gRPC: `WorkOrderQueryService` (`GetWorkOrderById`, `ListWorkOrders` with status/district/
  overdue filters + pagination) and `WorkOrderCommandService` (`AssignCrew`, `MarkEnRoute`,
  `MarkOnSite`, `PauseWork`, `ResumeWork`, `SubmitCompletionProof`). Commands honor
  `CommandMetadata.idempotency_key` (store processed keys + the response; same key → same response).
  Illegal transition → `CommandResponse` with `ErrorCode = INVALID_STATE`. Other RPCs → `UNIMPLEMENTED`.

### Notification
- Consumes `WorkOrderCreated`, `WorkOrderStatusChanged`, `PotholeSaved`.
- Stores a row in `notifications` (recipient = "dashboard" for now), logs it, and pushes it to a
  SignalR hub at `/hubs/notifications` (all connected clients). *stub:* no SMS/push, no per-user routing.

**Done when**
- `dotnet test tests/SmartCity.WorkOrder.Domain.Tests` passes: every allowed transition succeeds,
  every disallowed one throws, history is appended, SLA due date is correct per priority.
- With `ForceDetection=true`, one frame produces exactly: 1 pothole row, 1 estimate, 1 work order
  (Pending, with EstimatedCost set, whichever event arrived first), 2+ notifications.
- Restarting Work Order mid-run and redelivering messages does not create duplicate work orders.
- Seq `CorrelationId = '<frameId>'` shows the whole chain through Notification.

---

## M4 — API Gateway (REST → gRPC)

- gRPC clients via `Grpc.Net.ClientFactory`, with `Microsoft.Extensions.Http.Resilience`
  (timeout, retry, circuit breaker 5 failures → open 30 s).
- *stub auth:* no JWT yet; read `X-User-Id` header (default `"dev-officer"`) and pass it as
  `CommandMetadata.submitted_by`. Real JWT comes with the identity-provider decision.
- Endpoints:
  ```
  GET  /api/work-orders?status=&district=&overdueOnly=&page=&pageSize=
  GET  /api/work-orders/{id}
  POST /api/work-orders/{id}/assign       { "crewId": "CREW-MERAM-03" }
  POST /api/work-orders/{id}/en-route
  POST /api/work-orders/{id}/on-site
  POST /api/work-orders/{id}/pause        { "reason": "Weather" }
  POST /api/work-orders/{id}/resume
  POST /api/work-orders/{id}/complete     { "proofImageUrl": "http://localhost:9000/evidence/..." }
  GET  /api/potholes/{id}
  GET  /api/potholes?bbox=minLon,minLat,maxLon,maxLat   → GeoJSON FeatureCollection
  GET  /api/vehicles/live                                → positions from Redis
  GET  /api/estimates/by-entity/{entityId}
  ```
- POST endpoints require an `Idempotency-Key` header (400 if missing) and forward it.
- Map `ErrorCode` → HTTP: NOT_FOUND 404, INVALID_STATE 409, VALIDATION 400, DUPLICATE 200 (return
  original), AUTH_FAILED 403, INTERNAL 500. gRPC `Unavailable` → 503.
- Swagger UI in Development.

**Done when**
- `GET /api/work-orders` lists the work orders created in M3.
- Walking one through assign → en-route → on-site → pause → resume → complete works, and each
  step produces a `WorkOrderStatusChanged` notification.
- Calling `on-site` on a Pending order returns 409.
- Repeating a POST with the same `Idempotency-Key` returns the same response and changes nothing.
- Stopping the Pothole service makes `/api/potholes` return 503 quickly (circuit breaker), not hang.

---

## M5 — End-to-end test + containers

### tests/SmartCity.EndToEnd.Tests (runs against the live local stack)
1. Publish one frame over MQTT with a known `frameId` (reuse simulator code), with the fake AI in
   `ForceDetection` mode.
2. Poll `GET /api/work-orders` (up to 30 s) for a new work order.
3. Follow the trace: work order → `SourceEntityId` → `GET /api/potholes/{id}` → its `ImageUrls`
   contain the `frameId` → HTTP GET that image from MinIO returns 200.
4. Assert `GET /api/estimates/by-entity/{potholeId}` returns an estimate and the work order's
   EstimatedCost equals it.

### Containers
- A Dockerfile per service (multi-stage, `mcr.microsoft.com/dotnet/sdk:8.0` → `aspnet:8.0`).
- Add the services to `docker-compose.yml` under `profiles: ["apps"]`, using container hostnames
  (`postgres`, `rabbitmq`, `emqx`, `minio`, `redis`, `seq`) via environment variables.
- `docker compose --profile apps up -d --build` runs everything.

**Done when**
- The end-to-end test passes against `dotnet run` services **and** against the containerized stack.
- `README.md` at the repo root explains: start infra, run services, run simulator, run tests, and
  how to follow a trace in Seq.

---

## After the skeleton (not now)
Replace stubs with L3 designs, one service at a time, in this order: Work Order (full lifecycle:
reassign, priority change, LogWorkDetails, crew evaluation) → Cost Calculation (real price catalog)
→ Pothole clustering → Inventory → Violation → Building → Notification routing → real JWT auth →
EMQX mTLS + ACLs → Redis dashboard cache → real AI model.
`grep -rn "SKELETON:"` lists every stub that is left.
