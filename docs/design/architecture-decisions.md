# SmartCity — Architecture Decisions (Session Summary)

Date: 2026-09-24
Scope: Communication patterns, vehicle-to-cloud data flow, AI positioning, and C4 modelling.

---

## 1. Communication patterns

Three transports, each with a clear job.

| Hop | Transport | Used for |
| --- | --- | --- |
| Browser ↔ API Gateway | HTTP (REST) | What the frontend already speaks |
| API Gateway ↔ Service | gRPC | Officer-initiated reads and writes — fast, typed, binary |
| Service ↔ Service | AMQP (RabbitMQ) | State propagation between services after a write completes |

**Reads are sync.** Officer waits for the answer. Nobody polls to see a list of potholes.

**Writes are sync by default.** Officer writes, service responds with the created record, *then* publishes an event for others to react. The officer's confirmation is immediate — no async cascade the user has to wait for.

**Async 202 pattern reserved for genuinely long-running operations.** District-wide batch recalcs, bulk reassignments. Not a default.

**Idempotency keys on all writes.** Each write request carries a key in the header. Service checks before creating to prevent duplicates from browser retries.

---

## 2. Vehicle-to-cloud data flow

**The vehicle is a dumb streamer.** No AI on the vehicle. It ships raw frames, GPS, telemetry, sensor readings — everything — to the cloud.

**The AI runs as its own service(s) inside our platform.** One or more AI Detection Services subscribe to frames and publish detections. Split by domain later if needed (Pothole AI, Asset AI, etc.) — same architecture either way.

**Chosen flow — Path B (event-driven):**

```
Vehicle → EMQX → Vehicle Communication
                       ↓
                 saves frame + telemetry to Blob Storage
                       ↓
                 publishes FrameReceived + VehicleTelemetryReceived to RabbitMQ
                                        ↓
                 AI Detection Services subscribe to FrameReceived
                                        ↓
                 fetches frame from Blob Storage, runs inference
                                        ↓
                 if detection → publishes RawDetectionReceived
                 if nothing   → publishes nothing
                                        ↓
                 Pothole / Inventory / Violation / Building subscribe as before
```

**Why not sync AI orchestration (Path A):** blocks Vehicle Communication on the slowest thing in the pipeline. Doesn't scale under bursty load.

**Why not AI-subscribes-to-MQTT (Path C):** duplicates the frame delivery, forces AI to speak two transports (MQTT + AMQP), and either wastes bandwidth or reinvents the event chain.

**Storage strategy — save-all for now.** Every frame goes to Blob Storage regardless of detection outcome. Retention/pruning is a separate step later.

**Load sanity check.** At ~50 vehicles × 1 frame every 2 seconds = ~25 msgs/sec of frame events + ~50 msgs/sec of telemetry. RabbitMQ handles 10,000+ msgs/sec on modest hardware. Two orders of magnitude below its limit. Critical rule: **messages carry URLs, not image bytes.**

---

## 3. Service structure

**Each service owns its own data store.** No shared database. Storage strategy (SQL, NoSQL, partitioning, caching) is decided per service later.

**9 services + AI + brokers:**

- API Gateway (entry point)
- Vehicle Communication (ingestion, blob storage, initial event publish)
- AI Detection Services (inference)
- Pothole, Inventory, Violation, Building (domain processing)
- Cost Calculation (repair cost estimation)
- Work Order (ticketing + crew dispatch)
- Notification (SignalR + SMS/push)

Plus infrastructure: EMQX (MQTT perimeter), RabbitMQ (event bus), Blob Storage (S3/MinIO).

---

## 4. External vs internal systems

**External (the municipality already owns or a third party runs):**

- Vehicle Fleet
- Blob Storage (AWS S3 in prod, MinIO for local)
- Permit Registry
- Zoning Database
- Building Permit Registry
- SMS / Push Provider

**Internal (we own):** everything inside the SmartCity Platform boundary — all 8 domain-related services, the gateway, and the brokers.

---

## 5. C4 modelling — tooling

**Structurizr DSL chosen over IcePanel.**

- Structurizr auto-generates C1 (System Context) and C2 (Container) views from the same model file.
- IcePanel requires manual diagram creation after import — not what we wanted.
- The reference look you had in mind is Simon Brown's banking example, which is native Structurizr styling.

**Render options:** online DSL editor at `structurizr.com/dsl` (easiest), or Structurizr Lite via Docker.

**File:** `smartcity-workspace.dsl`.

---

## 6. Event contracts — flagged for update

The AI-in-cloud decision changes two existing event contracts:

1. **`RawDetectionReceived`** — now published by AI Detection Services, not Vehicle Communication. Rename `EdgeModelVersion` → `ModelVersion`.
2. **New event: `FrameReceived`** — published by Vehicle Communication when a frame lands in Blob Storage. Carries `VehicleId`, `Timestamp`, `Location`, `FrameUrl`, telemetry snapshot. This is what AI subscribes to.

C# records in `SmartCity.Contracts` need updating to match — noted for the next work session.

---

## 7. Per-service storage template and scaling ladder

The same template applies to every stateful service (Pothole, Inventory, Violation, Building, Cost, Work Order). The key finding: **the database is not the scaling bottleneck — images and AI are.** Even at 1000 vehicles, DB writes stay near 10/sec (only ~2% of frames produce a detection). So sharding is almost certainly never needed.

### Per-service template

- Own database (database-per-service preserved), but all on one shared PostgreSQL server to start — one DB per service (`pothole_db`, `inventory_db`, …), each with its own user/permissions so no service can read another's data.
- PostGIS where the service is spatial (Pothole, Inventory, Violation, Building — and Work Order has a location too).
- Materialized views for that service's own stats, refreshed every 1–5 min, served over gRPC.
- A bounding-box endpoint per service for the map (GeoJSON, backed by a GIST index), returning only open items.

### One Redis from Phase 1 (decided)

A single Redis instance, justified by cross-service aggregation and shared data — not by single-DB load. What goes in it:

- **In:** cross-service dashboard aggregations (overview, sidenav counts, TTL 30–60s); shared/common lookup documents; session data; rate-limit counters; live vehicle positions; later, the SignalR backplane when Notification runs more than one instance.
- **Out:** map bounding-box queries (too varied to cache); single-entity detail reads; live work-order state. Within-service report stats stay in materialized views, not Redis.
- Invalidate dashboard keys from the RabbitMQ events already published (e.g. on `PotholeSaved`), so TTL is just a safety net.

### The map and reports

- **Map:** each service exposes a bbox endpoint with filters → GeoJSON. Frontend requests one layer per service in parallel; clustering happens in the browser. Only open items are shown, so point counts stay low.
- **Reports:** materialized view inside each service; the Gateway aggregates their results in parallel and holds them in Redis.

### Phase 1 — start here (up to ~100 vehicles, ~30 officers)

One PostgreSQL + PostGIS server (DB per service), one Redis, one RabbitMQ node, one MinIO with a retention policy, one GPU, one instance per service. Gateway calls services in parallel.

| Trigger | Action |
| --- | --- |
| `FrameReceived` queue backs up and doesn't drain (consumer lag) — usually the first thing to happen, around ~100 vehicles | Add AI workers or another GPU (competing consumers) |
| Storage size/cost climbs | Shorten retention, or keep only frames that had a detection |
| Need >1 Gateway or Notification instance (availability, or officers past ~50) | This is Redis's real trigger — shared cache + SignalR backplane |
| Map viewport returns >5–10k items or payload exceeds a few MB | At low zoom, return server-side aggregated counts via `ST_SnapToGrid` instead of points |
| Detection-history table passes tens of millions of rows | Partition it by month on the same server (easy pruning of old data) |

### Phase 2 — full municipality (hundreds of vehicles, 50–150 officers)

Add: multiple GPUs; multiple instances of the hot services; small RabbitMQ cluster. Database is often still one server. (Redis already present from Phase 1.)

| Trigger | Action |
| --- | --- |
| One service dominates server resources | Move just that service's DB to its own server — cheaper and simpler than a replica |
| CPU steadily >70%, or p95 on indexed queries >200ms after tuning — usually past ~150 active officers | Add a read replica for that service only |
| Server-side point aggregation no longer enough; map slow with tens of thousands of open items | Add a tile server (Martin or pg_tileserv) on the PostGIS replica |

### Phase 3 — big city / multiple cities (thousands of vehicles) — describe, don't build

Kubernetes for autoscaling and self-healing; CDN (Cloudflare) in front of blob storage and vector tiles; data lake for historical analytics. Sharding by district only if the numbers above are ever exceeded — likely never.

### Which bottleneck each rung solves

Gateway cache → cross-service aggregation latency. Materialized views → within-service report cost. Competing consumers/GPUs → AI throughput (the real early bottleneck). Retention policy → storage cost. Read replica → read throughput. Tile server → map render volume. Sharding → write/data volume (last resort). CDN → geographic distribution.

---

## 8. Still open

Everything in the Roadmap doc's "Open questions" section — plus one that partially got answered here:

- **Blob storage details** — decided *what* (S3-compatible), still open on *which* (MinIO for local, AWS for prod is likely).
- **How many AI services** — one to start, may split by detection domain later.

---

## 9. Next work

Per the roadmap doc:

- **A1.** Draw the L1 System Context diagram — now done via Structurizr DSL.
- **A2.** Update the L2 Container diagram — now done via Structurizr DSL.
- **A3.** Define query contracts (proto or C# records — TBD).
- **A4.** Define command contracts with idempotency key envelope.
- **Contracts update.** Modify `RawDetectionReceived`, add `FrameReceived`.
