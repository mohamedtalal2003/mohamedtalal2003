# SmartCity Monitoring — instructions for Claude Code

## What this is
Cameras on Konya municipality vehicles stream frames + telemetry to the cloud. A cloud AI
detects road/infrastructure problems (potholes, damaged assets, illegal signs, unsafe
construction). Detections are stored, costed, turned into work orders, and dispatched to crews.

We are building a **walking skeleton**: every service exists and the whole chain runs end to
end, but most internals are deliberately dumb stubs. Real logic (L3 design) replaces stubs later.

- Full design: `docs/design/smartcity-design-record.md` (read §3, §4, §5, §8, §11 before coding)
- **What to build, in order: `docs/BUILD-PLAN.md`.** Work one milestone at a time. After each
  milestone, run its "Done when" checks and report the results before starting the next one.

## Stack (do not substitute)
.NET 8 · ASP.NET Core · MassTransit **8.x** on RabbitMQ (do not upgrade to v9: it is commercially
licensed) · gRPC (Grpc.AspNetCore) · MQTTnet **4.x** · EF Core 8 + Npgsql + NetTopologySuite
(PostGIS) · AWSSDK.S3 against MinIO (same code works on AWS S3 later) · StackExchange.Redis ·
SignalR · Serilog → Seq · xUnit.

## Architecture invariants — never break these
1. **Database per service.** A service only ever connects to its own database. Never read another
   service's tables. Need another service's data? Call its gRPC API or keep a local copy from events.
2. **Transports:** Browser ↔ Gateway = HTTP/REST. Gateway ↔ service = gRPC. Service ↔ service =
   RabbitMQ events only. No service-to-service gRPC calls, no HTTP between services.
3. **Reads are synchronous** (gRPC). **Writes are synchronous, then publish an event.** Publish via
   the **MassTransit EF Core transactional outbox**, so the DB write and the event commit together.
4. **Messages carry URLs, never image bytes.** Images live in MinIO.
5. **Every event inherits `IntegrationEvent`.** When a consumer publishes a follow-up event it MUST
   copy `CorrelationId` unchanged and set `SourceDetectionId`. This is what makes the trace work.
6. **Consumers are idempotent.** RabbitMQ delivers at least once. Use the MassTransit EF Core
   inbox (comes with the outbox) and set MassTransit `MessageId = EventId` when publishing.
7. **Every write command carries an idempotency key** (`Idempotency-Key` HTTP header → gRPC
   `CommandMetadata.idempotency_key`). Same key twice returns the first result.
8. **Services are equal peers.** No "layers", no orchestrator service.

## Contract rules (src/SmartCity.Contracts)
- The contracts are shared by a team of four. **Do not change an existing event or proto without
  asking the user first.**
- Once agreed, changes are **additive only**: add optional fields; never rename or remove. A breaking
  change becomes a new type (`PotholeSavedV2`) and a `SchemaVersion` bump.
- C# enums in `Enums/` mirror `Protos/enums.proto` name-for-name and number-for-number. Change both
  together or neither.
- Enums go over the bus as **strings** (configure `JsonStringEnumConverter`); see
  `src/SmartCity.Contracts/MockMessages/mock-events.json` for the exact wire shape.

## Skeleton conventions
- Mark every stub with a `// SKELETON:` comment saying what the real version will do. Later L3
  work finds them with `grep -rn "SKELETON:"`.
- Times are UTC everywhere (`DateTimeOffset.UtcNow`). Convert to `+03:00` only in the UI.
- Coordinates: WGS84 (SRID 4326). PostGIS columns are `geography(Point,4326)`.
- Config via `appsettings.json` + environment variables. No secrets in code.
- Every service exposes `/health/live` and `/health/ready` (ready checks its DB + RabbitMQ).
- Logs: Serilog to console + Seq (`http://localhost:5341`). Every log line inside a consumer must
  carry `CorrelationId` as a property (push it into the log context in a consume filter).

## Ports (local dev)
| Service | HTTP (health, REST, SignalR) | gRPC (HTTP/2, no TLS) | Database |
|---|---|---|---|
| ApiGateway | 5000 | – | – |
| VehicleCommunication | 5210 | – | – (stateless) |
| AiDetection | 5220 | – | – (stateless) |
| Pothole | 5230 | 5231 | pothole_db |
| Inventory | 5240 | 5241 | inventory_db |
| Violation | 5250 | 5251 | violation_db |
| Building | 5260 | 5261 | building_db |
| CostCalculation | 5270 | 5271 | costcalc_db |
| WorkOrder | 5280 | 5281 | workorder_db |
| Notification | 5290 | 5291 | notification_db |

Use two Kestrel endpoints per service (Http1 on the HTTP port, Http2 on the gRPC port), because
plain-text gRPC needs HTTP/2-only endpoints.

DB connection string pattern:
`Host=localhost;Port=5432;Database=<svc>_db;Username=<svc>_svc;Password=<svc>_svc`
(users/dbs are created by `infra/postgres/init/01-create-databases.sql`).

## Infrastructure
```
cp .env.example .env
docker compose up -d          # postgres+postgis, rabbitmq, emqx, minio(+buckets), redis, seq
docker compose ps             # everything healthy before running services
```
UIs: RabbitMQ http://localhost:15672 (smartcity/smartcity) · EMQX http://localhost:18083
(admin/public) · MinIO http://localhost:9001 (minioadmin/minioadmin) · Seq http://localhost:8081

Reset all data: `docker compose down -v && docker compose up -d`

## Commands
```
dotnet build SmartCity.sln
dotnet test SmartCity.sln
dotnet run --project src/Services/Pothole/SmartCity.Pothole.Api
dotnet run --project tools/VehicleSimulator -- --vehicles 5 --interval-ms 2000
```

## How to work
- Small, buildable steps. `dotnet build` must pass after every step.
- When the design record and this file disagree, this file wins; mention the conflict to the user.
- When something is genuinely undecided (see design record §15 "Still open"), pick the simplest
  option, mark it `// SKELETON:`, and list it in your milestone report. Do not invent L3 design.
