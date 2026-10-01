# SmartCity Monitoring System — Full Design Record

*A complete record of the architecture: every decision, the reasoning behind it, and the alternatives that were considered and rejected. Written to be used both as a reference while building and as the narrative for a design defense.*

**Project:** AI-powered municipal infrastructure monitoring for Konya, Turkey.
**Stack:** .NET 8, React, RabbitMQ (MassTransit), gRPC, MQTT (EMQX), PostgreSQL + PostGIS.
**Status:** Architecture and contract design substantially complete; internal (component-level) design in progress, starting with the Pothole service.

---

## How to read this document

Each major decision is written in the same shape:

- **Decision** — what was chosen.
- **Why** — the reasoning.
- **Rejected alternatives** — what else was on the table and why it lost.

That third part is the important one for a defense. Anyone can say "we used gRPC." The mark of real design is being able to say "we considered request/reply over RabbitMQ instead, and here's why we didn't."

---

## 1. The problem

Cameras mounted on municipality vehicles drive around the city. As they move, they capture imagery of the road and roadside. That imagery is analyzed by AI to detect problems — potholes, damaged or missing street furniture, illegal signs and banners, unsafe construction sites. Confirmed problems are stored, costed, turned into work orders, and dispatched to field crews. Officers watch all of this on a dashboard; crews receive and complete assignments on mobile.

The whole system is just a reliable, scalable realization of that one flow: **vehicle sees a problem → system records and prices it → a crew fixes it → the loop is closed with proof.**

Everything in this document exists to serve that flow. When a decision seemed complex, the test was always: does this actually serve the flow at the scale we operate, or is it complexity for its own sake?

### Domain context

The system is grounded in Konya. Mock data throughout uses real Konya coordinates, Turkish street names (Ankara Caddesi, Feritpaşa Sokak), the three central districts (Meram, Selçuklu, Karatay), Turkish text for detected banners and work-order descriptions, and TRY currency. This grounding is deliberate — it keeps the design honest about the real use case instead of abstract.

---

## 2. Architecture philosophy

### Decision: microservices with database-per-service

The system is built as a set of independent services, each owning its own data, communicating through events and RPC. No shared database.

**Why.** The domains are genuinely independent — pothole processing has nothing to do with banner-permit checking. Independent services can be developed by different team members in parallel (there are four people on this project), deployed and scaled independently, and fail independently: if the Violation service is down, potholes still flow. Database-per-service is what makes that independence real — if services shared a database, a schema change or a heavy query in one would affect all of them, and the "independence" would be fiction.

**Rejected alternative: a monolith.** Simpler to build initially, one database, one deployment. Rejected because it defeats the point of the project (demonstrating a distributed system), couples the team's work, and makes independent scaling impossible. A monolith would also make the "one service crashing takes down everything" failure mode unavoidable.

**Rejected alternative: microservices sharing one database.** A common anti-pattern. It looks like microservices but the shared database re-couples everything. Rejected for that reason.

### Decision: all services are equal peers — no layer hierarchy

An early draft split the services into a "detection layer" and a "business layer," implying a top-down hierarchy. This was corrected. All seven non-gateway, non-ingestion services (Pothole, Inventory, Violation, Building, Cost Calculation, Work Order, Notification) are equal peers. Each one publishes events, subscribes to the events it cares about, and answers queries through the API Gateway.

**Why.** The "layer" framing was misleading. It suggested that business services sit above detection services and call down into them, which is not how the system works. In reality, an officer querying potholes goes API Gateway → Pothole service → response, exactly the same shape as an officer querying work orders. There is no caller/callee hierarchy; there is a mesh of peers coordinated by events. Modeling it as layers would have baked a false structure into the design and the diagrams.

**Rejected alternative: the layered framing.** Rejected because it did not match the actual runtime behavior and would have misled anyone reading the architecture.

### Decision: event-driven core, synchronous edges

Internally, services coordinate by publishing and subscribing to events on RabbitMQ. At the edges — where an officer's browser talks to the system — interactions are synchronous request/response.

**Why.** Events give loose coupling and resilience between services (a publisher doesn't wait for or even know its subscribers). But an officer asking a question needs an answer now, synchronously — you cannot hand a user an event and tell them to wait. So the system is event-driven where that buys resilience (service-to-service) and synchronous where that serves the user (edge-to-system). The details of this split are in section 4.

---

## 3. Service catalog

Nine services plus the AI, the brokers, and blob storage.

| Service | Role |
| --- | --- |
| **API Gateway (BFF)** | Single HTTPS entry point for dashboard and mobile. JWT auth, routing, rate limiting, SSL termination. Calls services over gRPC. |
| **Vehicle Communication** | Ingestion gateway. Receives frames + telemetry from vehicles (via EMQX), stores frames in blob storage, publishes events. Stateless — no data store of its own. |
| **AI Detection Services** | One or more models (pothole, asset, signage, building). Subscribe to frame events, fetch the image, run inference, publish detection events when something is found. |
| **Pothole** | Road damage. Dimension estimation, severity, spatial clustering of repeat detections. |
| **Inventory** | Fixed roadside assets (lights, signs, barriers). Condition lifecycle: Intact → Faded → Broken → Missing. |
| **Violation** | Unauthorized / oversized / illegally placed signs and banners. Cross-references permit and zoning databases. |
| **Building** | Construction-site safety violations, illegal extensions, scaffolding anomalies. Cross-references building-permit registry. |
| **Cost Calculation** | Estimates repair cost (materials, labor, equipment) per confirmed detection. |
| **Work Order** | Maintenance tickets, crew assignment, priority, SLA tracking, full crew lifecycle. |
| **Notification** | Real-time alerts: SignalR to the dashboard, SMS/push to crews. Subscribe-only from RabbitMQ. |

Plus infrastructure: **EMQX** (MQTT security perimeter), **RabbitMQ** (event bus), **Blob Storage** (S3-compatible; MinIO locally), and one shared **PostgreSQL + PostGIS** server hosting a separate database per stateful service.

### Note on naming

The Violation service was originally called "Signage." It was renamed to Violation because the service covers more than signage (oversized banners, illegal placement) and because the event it publishes is `ViolationSaved`. Consistent naming between the service and its primary event reduces confusion.

---

## 4. Communication patterns

This section went through several revisions in design. The final model is simple, but the path to it matters, because the rejected options are instructive.

### The three transports

| Hop | Transport | Why this one |
| --- | --- | --- |
| Browser ↔ API Gateway | HTTP / REST | Browsers speak HTTP. Nothing to justify — it's the web. |
| API Gateway ↔ Service | gRPC | Fast, typed, binary, inside the trust boundary. Contracts are explicit `.proto` files. |
| Service ↔ Service | AMQP (RabbitMQ) | Loose coupling and durability for state propagation between services. |

### Decision: reads are synchronous

When an officer queries data (list potholes in Meram, get one work order), the call is synchronous: Gateway → service (gRPC) → data → back → officer, in one round trip. No events involved.

**Why.** A read is a question, and the user needs the answer before they can act. You cannot return "202 Accepted, come back later" to someone asking to see a list — they have nothing to do with that. gRPC keeps the internal call fast and typed. The service reads its own store and returns.

**Rejected alternative: async reads with polling.** An earlier version of the design had *all* operations async — the officer would fire a query, get a request ID, and poll for the result. This was rejected once we reasoned it through: it makes no sense for reads. It adds latency and complexity to the most common operation in the system for zero benefit. The async-with-polling idea only makes sense for genuinely long-running *commands*, not for reads (see below).

### Decision: writes are synchronous, then publish an event

When an officer performs a write (create a work order, update an asset's condition), the service writes to its own store, returns the result to the officer immediately, and *then* publishes an event so other services can react. The officer's confirmation is immediate; the downstream fan-out (notifications, cost estimates) happens after the response, asynchronously.

**Why.** Most writes in this system are simple database inserts that take tens of milliseconds. Returning the created record immediately (201 Created) is honest and gives the best user experience. The event-driven fan-out still happens — it just happens *after* the user has their answer, so they never wait for the cascade. Example: officer creates a work order → Work Order service writes it and returns the ticket → then publishes `WorkOrderCreated` → Notification service picks it up and texts the crew. The officer saw success in one round trip; the SMS happened a moment later without them waiting.

**Rejected alternative: async writes with 202 + no polling (the "e-commerce order" pattern).** This was seriously considered and is a real pattern (AWS APIs, Stripe payment intents, GitHub Actions all work this way): the write returns 202 Accepted with a Pending status, the work happens in the background, and the user simply re-reads the resource later with the normal GET to see its final state — no dedicated polling endpoint. It's elegant and it was tempting.

It was rejected as the *default* because most of our writes don't justify it. Returning "202 Pending" for something that's actually a 50ms insert feels wrong, and it would force every writeable resource to carry a Pending → Processing → Completed → Failed lifecycle — real modeling overhead on every service, paid for a benefit we mostly don't need. The honest conclusion: use sync writes by default, and reserve the async 202 pattern for the few operations that are genuinely long-running (a district-wide cost recalculation, a bulk reassignment). Those may adopt it later, per-operation, where it earns its keep. It is not the default.

**Rejected alternative: query/reply over RabbitMQ for internal reads.** Instead of the Gateway calling services over gRPC, the Gateway could publish a query message to RabbitMQ and wait on a reply queue — keeping everything on one transport. Rejected because it adds latency and complexity to reads for the sake of transport uniformity. gRPC is the right tool for a synchronous, typed request/response; RabbitMQ is the right tool for fire-and-forget state propagation. Using the queue for synchronous reads would be forcing one tool to do the other's job.

### Decision: idempotency keys on every write

Every write request carries an idempotency key (a client-generated UUID) in its command metadata. The service checks the key before creating; the same key seen twice returns the original result instead of creating a duplicate.

**Why.** Networks are unreliable. If an officer's browser retries a "create work order" request because the response got lost, you must not end up with two work orders. The idempotency key makes retries safe — this is standard practice for any reliable write API and costs almost nothing to include from the start.

### Why events are still needed alongside all this

Reads and writes above are the *synchronous* edge. Events are the *asynchronous* core, and they do a completely different job: propagating state between services after a write completes. `PotholeSaved`, `WorkOrderCreated`, and the rest are one-way news — a service announces that its state changed, and any interested service reacts. No one is asking a question; no one waits for a reply. This is why the system needs both the gRPC contracts (section 9) and the event contracts (section 8) — they are not redundant, they serve different purposes.

---

## 5. The vehicle-to-cloud data flow and where the AI lives

This was one of the most involved design discussions, because moving the AI changes the whole ingestion shape. Three candidate flows were considered.

### Decision: the vehicle is a dumb streamer; AI runs in the cloud as its own service(s)

The vehicle has no AI. It is a camera + GPS + sensors + radio. It streams raw frames and telemetry (speed, battery, network, GPS, sensor readings) to the cloud. The AI runs inside our platform as one or more services.

**Why.** Keeping AI off the vehicle keeps the edge device cheap and simple, lets us update and retrain models centrally without touching vehicles, and means a model improvement instantly applies to the whole fleet. It also concentrates the compute (GPUs) where we can manage and scale it.

### Decision: event-driven ingestion (Path B)

The chosen flow:

```
Vehicle → EMQX → Vehicle Communication
                       ↓ saves frame + telemetry to Blob Storage
                       ↓ publishes FrameReceived + VehicleTelemetryReceived (AMQP)
                                        ↓
                       AI Detection Services subscribe to FrameReceived
                                        ↓ fetch frame from Blob Storage, run inference
                       if detection → publishes RawDetectionReceived
                       if nothing   → publishes nothing
                                        ↓
                       Pothole / Inventory / Violation / Building subscribe as before
```

Vehicle Communication is a pure ingestion pipe: receive, store the image, fire an event. The AI reacts. Nothing detected → no downstream event. Multiple AI services can detect different things in the same frame, producing multiple detection events.

**Why.** It fits the event-driven core the rest of the system already uses. The AI is the slowest thing in the whole pipeline, and putting a queue in front of the slowest thing is exactly what queues are for — it absorbs bursts and lets AI process at its own pace without blocking ingestion. Adding more AI capacity is free: more instances just subscribe to the same event (competing consumers). And Vehicle Communication stays fast because it never waits on inference.

**Rejected alternative — Path A: Vehicle Communication orchestrates the AI synchronously.** Vehicle Communication would save the image, call the AI service(s) directly (gRPC), wait for the result, and only publish a detection event if something was found. This is clean and easy to reason about, and the queue only ever carries real detections.

It was rejected because it blocks the ingestion pipeline on the slowest component. Under load — 50 vehicles, AI taking ~800ms per frame — Vehicle Communication either processes one frame at a time (backlog grows unrecoverably during rush hour) or goes multi-threaded, at which point its thread pool *is* an in-memory queue that dies on restart and lacks durability and observability. Once you need concurrency to survive load, the real queue (RabbitMQ) is the simpler, more robust answer. The user's own defense of Path A — "Vehicle Communication takes from MQTT one by one, so no waiting" — holds only at low load; at real load it needs concurrency, and then the queue wins.

**Rejected alternative — Path C: AI subscribes to MQTT alongside Vehicle Communication.** Both Vehicle Communication and the AI would subscribe to the EMQX topic; the AI would process frames and publish detections directly to RabbitMQ. Rejected as architecturally messy: two independent consumers of the same MQTT topic means EMQX is doing double duty (security perimeter *and* internal fan-out), the AI would have to speak both MQTT and AMQP, and it either processes raw bytes over MQTT (wasteful) or waits for Vehicle Communication to save the image and tell it the URL — which just reinvents the event chain it was trying to avoid. Path C duplicates work or recreates Path B badly.

### Decision: save all frames for now; retention is a later, separate step

Every frame goes to blob storage regardless of whether the AI finds anything. Pruning is deferred.

**Why.** Simplicity now. The concern about not wanting to keep every image is real and was discussed, but it's an optimization, not a day-one requirement. The clean way to add it later without changing the event shape is a blob-storage lifecycle rule (temp bucket auto-expires after N hours; images tied to real detections get promoted to a permanent bucket). Crucially, the promotion/copy step belongs to Vehicle Communication, not the AI — AI's job is inference, not blob-storage lifecycle management. That separation-of-concerns point was explicitly raised and settled: the AI publishes a lightweight "found something" result; a storage-owning component does the copy.

### The load math (why RabbitMQ will not crash)

A concern was raised that the flood of frame data could overwhelm RabbitMQ. Worked through with real numbers:

- ~50 vehicles, ~1 frame every 2s = ~25 frame messages/sec, plus ~50 telemetry messages/sec ≈ **~75 messages/sec at peak.**
- A single RabbitMQ node handles **10,000–50,000 small messages/sec** on modest hardware.
- So the system runs about **two orders of magnitude below** RabbitMQ's ceiling.

The load-bearing rule that keeps this true: **messages carry references (URLs), never image bytes.** A `FrameReceived` message is ~500 bytes of JSON (ids, GPS, a URL). The 2 MB image travels separately, fetched from blob storage by whoever needs it. If images were embedded in messages, 25 frames/sec × 2 MB = 50 MB/sec through the broker *would* be a problem — which is exactly why we don't do that. Extra safety: EMQX rate-limits each vehicle at the perimeter, so a malfunctioning device gets throttled before RabbitMQ ever sees it.

### Contract consequence

Moving AI into the cloud changes two event contracts (tracked in section 8): a new `FrameReceived` event (published by Vehicle Communication, carrying the frame URL + telemetry snapshot) that the AI subscribes to, and `RawDetectionReceived` is now published by the AI rather than the vehicle — its `EdgeModelVersion` field becomes `ModelVersion` (the cloud model's version).

---

## 6. Security perimeter

### Decision: EMQX MQTT broker in front of Vehicle Communication, with mTLS, per-device ACLs, and per-device rate limits

Vehicles do not talk to Vehicle Communication directly. They connect to an EMQX MQTT broker that sits at the edge of the system and enforces three controls before any message reaches our code.

**Why each control:**

- **mTLS (mutual TLS).** Each vehicle carries a unique X.509 certificate. On connect, EMQX verifies it was issued by our authority. No valid certificate → connection rejected at the broker, before reaching any application code. An unknown device or attacker gets nothing. A compromised certificate can be revoked individually without touching other vehicles.
- **Per-device ACLs.** Each vehicle may only publish to its own topics (e.g. `vehicles/MUN-VEH-042/...`). It cannot read other vehicles' data or publish to system topics. Blast radius of one compromised device is limited to that device.
- **Per-device rate limiting.** Even a legitimate but malfunctioning vehicle (firing 1000 msg/sec instead of the expected handful) is throttled at the perimeter before it can flood the system.

**Why at the broker and not in application code.** Rejecting bad actors at the perimeter means our services never spend a cycle on them. Authentication, authorization, and rate limiting are exactly the kind of cross-cutting concern a purpose-built broker does better and more safely than hand-rolled checks inside Vehicle Communication.

This was added in direct response to the question "how do we stop unauthorized senders?" — the answer is: don't let them past the front door.

---

## 7. Resilience patterns

Added in response to the concern "I don't want the service to go down under heavy load." The conclusion reached was important and worth recording: **the existing architecture is already well-suited to this; these patterns are additions, not a restructuring.** RabbitMQ is the key — it acts as a shock absorber.

| Pattern | What it does | Why |
| --- | --- | --- |
| **Backpressure via queue** | Producers publish fast; consumers pull at their own pace; the queue absorbs spikes. | A traffic spike grows the queue temporarily and then drains, instead of overwhelming any service. |
| **Durable queues + dead-letter queues** | Messages survive broker restarts; messages that fail all retries go to a DLQ. | Nothing is lost. A crashed consumer's backlog waits for it; a poison message is quarantined for inspection and replay instead of vanishing. |
| **Competing consumers** | Run N instances of a service; RabbitMQ distributes messages among them; each message processed once. | Horizontal scaling for free — add instances under load, remove them when quiet. This is the same mechanism that scales AI throughput. |
| **Circuit breaker (Polly)** | After 5 consecutive failures to a dependency, the circuit opens and fails fast for 30s. | If a database is down, requests fail immediately for 30s instead of every request hanging on a timeout, giving the dependency time to recover. |
| **Retry with backoff** | Transient failures retried 3 times: 1s → 2s → 4s. | Most transient failures (a network blip, a brief lock) resolve within a couple of retries and never reach the DLQ. |
| **Health checks** | Each service exposes `/health/live` (am I running?) and `/health/ready` (can I do work?). | Docker/Kubernetes restarts unhealthy containers automatically. |

The through-line: **RabbitMQ decouples producers from consumers in time**, so a slow or crashed consumer degrades gracefully (backlog) instead of catastrophically (lost data or cascading failure).

---

## 8. Event contracts (the asynchronous core)

Events are C# records in a shared `SmartCity.Contracts` library, published/consumed via MassTransit. Every service references the same library, so publisher and subscriber agree on the shape by construction.

### Decision: every event carries `SourceDetectionId`

Every event downstream of a detection carries the id of the original raw detection.

**Why.** End-to-end traceability. From a work order, you can trace back through the cost estimate, the confirmed detection, and all the way to the original frame the AI saw. This is invaluable for debugging ("why was this work order created?") and for auditing.

### The events

| Event | Published by | Consumed by |
| --- | --- | --- |
| `VehicleTelemetryReceived` | Vehicle Communication | (live positions / cache) |
| `FrameReceived` *(new)* | Vehicle Communication | AI Detection Services |
| `RawDetectionReceived` | AI Detection Services *(was: Vehicle Comm)* | Pothole, Inventory, Violation, Building |
| `PotholeSaved` | Pothole | Cost Calc, Work Order, Notification |
| `AssetAddedOrUpdated` | Inventory | Cost Calc, Work Order, Notification |
| `ViolationSaved` | Violation | Work Order, Notification |
| `ConstructionAnomalyDetected` | Building | Work Order, Notification |
| `CostEstimateGenerated` | Cost Calc | Work Order, Notification |
| `WorkOrderCreated` | Work Order | Notification |
| `WorkOrderStatusChanged` | Work Order | Notification |

Consumers of `RawDetectionReceived` filter by a `DetectionType` enum whose values are grouped in numbered ranges (100s = pothole, 200s = asset, 300s = signage, 400s = building), so each detection service processes only what belongs to it.

### Naming decisions (from review)

- `PotholeConfirmed` → **`PotholeSaved`** — clearer that it has been processed and persisted, not merely "confirmed."
- `AssetConditionUpdated` → **`AssetAddedOrUpdated`** — reflects the dual purpose (a detection can create a new asset or update an existing one).
- `SignageViolationDetected` → **`ViolationSaved`** — matches the renamed Violation service and the "saved" convention.

**Why these matter.** Event names are part of the contract every team member codes against. Precise, consistent names ("Saved" = persisted, matching the service that owns it) prevent misunderstandings across a four-person team.

### Contract changes still pending (from the AI-in-cloud decision)

- Add `FrameReceived`.
- Change `RawDetectionReceived`'s publisher to the AI; rename `EdgeModelVersion` → `ModelVersion`.

---

## 9. Synchronous API contracts (the gRPC surface)

These are the request/response contracts for officer- and crew-initiated reads and writes, defined as `.proto` files in `SmartCity.Contracts/Protos/`. They are the sync surface; the events above are the async surface. Four files: `common.proto`, `enums.proto`, `queries.proto`, `commands.proto`.

### Decision: separate query services and command services (CQRS at the API level)

Reads live in `*QueryService` definitions; writes live in `*CommandService` definitions.

**Why.** Reads and writes have different shapes, different performance characteristics, and different scaling needs. Separating them at the contract level keeps each clean and makes the CQRS intent explicit — the read path and the write path are visibly different things.

### Decision: shared `common.proto` and `enums.proto`, imported by both

Pagination, geo types, time ranges, command metadata, error codes (common), and all domain enums (enums) live in shared files that both queries and commands import.

**Why.** One definition of `GeoCoordinates`, `District`, `PriorityLevel`, etc., reused everywhere. The original draft had `commands.proto` importing `queries.proto` just to reuse enums, which coupled the two. Splitting enums into their own file removes that coupling — both queries and commands depend on enums, neither depends on the other.

### The fixes applied to the first draft, and why

The first draft of the contracts (queries + commands for three services) was reviewed. Issues found and fixed:

1. **Missing services.** Only Road Damage, Inventory, and Work Order were covered. Added the rest: Violation, Building, Cost Calculation, Notification — each needs its own gRPC surface or the Gateway cannot call it.
2. **Strings that should be enums.** `detection_type` and `priority_level` were free-form strings (`"ROAD_DAMAGE"`, etc.). Changed to enums (`SourceEntityType`, `PriorityLevel`). **Why:** a typo in a string compiles fine and fails at runtime; an enum is checked at compile time and gives autocomplete. This is the single biggest correctness win in the contracts.
3. **No filters on list queries.** `ListDamages` had no way to filter by district, time, map area, or severity — it would return "all potholes ever," which is useless on a real dashboard. Added district, time-range, map-bounding-box, and type/severity filters to every `List*` request. **Why:** officers always query a slice (this district, this week, this map view), never the whole dataset.
4. **Thin command response.** The original `CommandResponse` was just `success + message + entity_id` — a client couldn't tell *why* something failed. Added an `ErrorCode` enum (DUPLICATE, NOT_FOUND, INVALID_STATE, VALIDATION, AUTH_FAILED, INTERNAL) plus `request_id` (echoes the idempotency key) and `processed_at`. **Why:** machine-readable errors let the client branch correctly (retry vs show-not-found vs re-auth).
5. **Idempotency key location.** Placed in `CommandMetadata`, carried by every command. **Why:** see section 4 — safe retries.
6. **Blob-URL validation note.** Every `*_image_url` field carries the requirement that the service must reject any URL not pointing to our own blob storage. **Why:** otherwise a malicious client could submit an arbitrary URL as "proof" and pollute records. The upload flow is: client uploads photo → gateway returns a blob URL → client passes *that* URL.
7. **Enum numbering.** `DamageStage` had confusing off-by-one numbering (STAGE_0 = value 1). Renamed to drop the embedded numbers. **Why:** readability; the semantic name carries the meaning, not a number that mismatches the enum value.

### The additional operations (from the "what else might we need" pass)

A deliberate pass was made through the different actors to find operations the CRUD-only first draft missed. Scope was confirmed with the user before building. Added:

- **Full field-crew lifecycle** (Work Order commands): `MarkEnRoute`, `MarkOnSite`, `PauseWork` (with a `PauseReason` enum), `ResumeWork`, `LogWorkDetails` (materials used + labor hours, so actuals can be reconciled against the estimate), plus `SubmitCompletionProof`. The `WorkOrderStatus` enum gained `EN_ROUTE`, `ON_SITE`, `PAUSED`. **Why:** the crew's real workflow has states between "assigned" and "done"; modeling only start/finish would lose the operational reality.
- **Crew view** (`ListMyWorkOrders`): reads the crew id from the JWT so a crew sees only its own assignments. **Why:** a crew should not page through the whole city's tickets.
- **Supervisor evaluation** — `SubmitCrewEvaluation` (score 1–5 + written feedback), with `ListCrewEvaluations` / `GetEvaluationById` and an `average_score` in the list response.
- **Work-order management**: `ReassignWorkOrder` (with reason), `ChangePriority` (with reason). **Why:** circumstances change after a ticket is created.
- **Dashboard aggregations**: `GetDistrictSummary` (open counts per district per type + a city-wide roll-up), `GetWorkloadStats` (active tickets per crew).
- **Detection timelines**: `GetDamageTimeline`, `GetAssetTimeline` — the full history of a detection (first seen, re-detections, stage/condition changes, work order created, repaired).

### Decision: supervisor evaluation is a separate record, not a field on the work order

**Why.** The point of scoring crews is to track performance *over time*. If the score sits on the work order, "how has this crew been doing this month?" means scanning every work order they touched. A dedicated `CrewEvaluation` record (linked to the work order but its own entity) makes crew-performance queries direct, keeps the work order focused on the work, and models the reality that the evaluation is created later, by a different person, in a separate step. It is also explicitly *not* a gate — the crew's completion closes the ticket; the evaluation is feedback after the fact.

**Rejected alternative: a rating field on the work order.** Simpler, but couples performance data to work-order records and makes crew-level aggregation a scan. Rejected for the reasons above.

### Deliberately left out

- **Data export** and **SLA escalation workflow** — not selected as in-scope. Note that SLA *breach* is still visible (a `overdue_only` filter on `ListWorkOrders`, and overdue counts in the dashboard); there just isn't an automated escalation process.
- **`FrameReceived` and the detection events are NOT in the proto files.** They are events (AMQP), not request/response, so they live with the C# event records, not the gRPC contracts. The protos are strictly the synchronous surface.

---

## 10. C4 modeling and diagram tooling

### Decision: model the architecture with the C4 model, stopping at the level that matches the current phase

C4 gives four levels of abstraction: L1 System Context (the whole system + external actors), L2 Container (the services, brokers, stores), L3 Component (inside one service), L4 Code (skip). We do L1 and L2 as the baseline and drop to L3 per service only when designing that service.

**Why.** C4 is a systematic, well-known way to describe a system at multiple zoom levels without drawing one incomprehensible mega-diagram. Stopping at the right level per phase avoids over-detailing things we haven't designed yet.

**Rejected alternative: Event Storming.** Considered, but it's a technique for *discovering* domain events from scratch — work we had already done. C4 fit better for documenting the structure we already understood.

### Decision: Structurizr (DSL) over IcePanel for the diagrams

**Why.** The goal was clean, separate C1 and C2 diagrams in the style of the canonical C4 examples. IcePanel imports a model but does not auto-generate those separate C4 views — it shows one model canvas, and you must hand-build each diagram by dragging objects. Structurizr (built by the inventor of C4) auto-generates the Landscape, Context, and Container views from a single text `workspace.dsl` file. One text file, rename-once-updates-everywhere, and the exact reference look. That matched the intent; IcePanel didn't.

**Note on the earlier misstatement:** it was initially said that IcePanel would auto-generate the C4 diagrams. It does not — that was corrected, and the move to Structurizr followed. (The IcePanel import file was also fixed along the way for two schema issues: database objects must use `type: store`, not `type: database`; and the tag-group icon must be from the allowed set.)

**How to render Structurizr:** paste `smartcity-workspace.dsl` into the online editor at `structurizr.com/dsl`, or run Structurizr Lite locally via Docker (`docker run -it --rm -p 8080:8080 -v "PATH:/usr/local/structurizr" structurizr/lite`, with the DSL file named `workspace.dsl` in that folder).

---

## 11. Per-service internal design (L3)

The **detection-style services — Pothole, Inventory, Violation, Building — share one internal template.** The Pothole service is the worked example below because it has the hardest internal problem (spatial clustering), but the same shape (own PostGIS DB, clustering/condition logic on the write side, materialized views for stats, a bbox endpoint for the map, a gRPC query API) applies to all four. Inventory clusters and tracks asset condition instead of pothole severity; Violation cross-references permit/zoning data; Building cross-references site permits — but the storage-and-CQRS skeleton is identical.

**Work Order is deliberately different** and will be designed separately. It is not a detection-processing service — it has no clustering, no AI input. It is a workflow/state-machine service: a ticket moves through a lifecycle (Pending → Assigned → En route → On site → Paused → Completed), it owns crew assignment and SLA logic, and its "hard problem" is correct state transitions, not spatial matching. Cost Calculation and Notification are also their own shapes. So: one shared template for the four detection services, and a per-service design for Work Order, Cost Calculation, and Notification when we reach them.

### Decision: the shared per-service template

- **Own database** on the shared PostgreSQL server (one DB per service, separate users/permissions).
- **PostGIS** where the service is spatial (Pothole, Inventory, Violation, Building — and Work Order has a location too).
- **Materialized views** for that service's own statistics, refreshed every 1–5 minutes, served over gRPC.
- **A bounding-box endpoint** for the map (GeoJSON, backed by a GIST spatial index), returning only open items.

### Decision: single store per service, not full CQRS with a separate read model

The generic pattern would split each service into a write store (optimized for its processing) and a separate read store kept in sync by a projection. We chose a single store per service instead, with materialized views for reports.

**Why.** Full CQRS with a separate read model is real complexity — two stores, a projection to keep them in sync, and eventual consistency to reason about. At this project's data volumes, one well-indexed PostGIS instance comfortably serves both the write load and the read load. The query API doesn't care whether it reads from a dedicated read model or the master store, so we can always split later if profiling ever demands it. Starting with a single store avoids paying for CQRS machinery we don't need.

**Rejected alternative: full CQRS (write store + projection + read store) per service.** Impressive to demonstrate, and appropriate at large scale, but rejected as premature here. The projection is the cost of CQRS: it exists only to sync a second store, and it brings eventual consistency (a small window where the master has data the read model doesn't). Not worth it for a project unless demonstrating CQRS is itself the goal — and we get the CQRS *idea* anyway through separate query/command services and materialized views, without the two-store overhead.

### The Pothole service specifically

Its defining problem: the same pothole is filmed by many vehicles over time, and the service must recognize those as one pothole, not many. That single requirement (spatial clustering) drives its internal design.

**Components:** a Detection Consumer (subscribes to `RawDetectionReceived`, type 100s) → a Clustering Engine (is this a new pothole or a repeat of an existing one?) → the Master Store; plus an Event Publisher (`PotholeSaved`) and a Query API (gRPC reads). All the intelligence is on the write side; the read side is deliberately simple and fast.

**Decision: PostgreSQL + PostGIS for the Pothole store.** The clustering engine must answer "is there already a pothole within N meters of this new detection?" on every incoming detection — a geospatial proximity query. PostGIS does exactly this with `ST_DWithin` over a GIST spatial index, and being relational it pairs naturally with everything else. **Rejected alternative: MongoDB with a 2dsphere index** — also does geospatial, and its flexible schema suits varying metadata, but it's weaker on the relational side (links to work orders, joins) and the clustering problem is fundamentally spatial-relational, which is PostGIS's home turf.

**Decision: fixed-radius clustering (≈3–5 m).** GPS accuracy is ~2 m, so a tighter radius risks splitting one pothole into two. This is a tuning parameter, not an architectural commitment — easy to change.

**Decision: no partitioning by district for now.** A single spatial index handles a city's worth of potholes easily; partitioning matters at millions of rows. Recorded as a future scaling option, not built.

---

## 12. Storage, caching, and the scaling ladder

This section carries the single most important scaling insight in the whole design, plus an honest correction.

### The key finding: the database is not the bottleneck — images and AI are

Worked from real numbers. At 50 vehicles, ~1 frame every 2s = ~25 frames/sec:

| What it hits | Load | A problem? |
| --- | --- | --- |
| DB writes | Only ~2% of frames produce a detection → ~0.5 writes/sec | No. PostgreSQL handles thousands/sec. |
| DB reads | ~20 officers, few active → ~10–25 requests/sec | No. One indexed server handles hundreds–thousands/sec. |
| Image storage | 25 × 200KB = 5 MB/s ≈ 140 GB per 8-hour shift | **Yes — the first thing that hurts.** |
| AI | 25 frames/sec × model count | **Yes — the second thing that hurts.** |

Even at 1000 vehicles, DB writes stay around 10/sec — trivial. Therefore **sharding is almost certainly never needed**, and the whole "distributed database" worry is misplaced for this system. Effort should go to AI throughput and storage, not to database scaling.

### The correction on caching (important, and worth stating plainly)

An earlier claim in this design was: "materialized views handle the reports, Redis is optional." That reasoning was monolith-shaped and is **wrong for a database-per-service architecture**, for a specific reason:

- A materialized view lives *inside one database*. It can pre-compute "pothole counts by district" inside the Pothole DB. It **cannot** aggregate across the Pothole, Violation, and Building DBs — they are separate databases (possibly different engines). The main dashboard overview needs exactly that cross-service aggregation.

So the corrected rule is:

> **Within a service, a materialized view is your report cache. Across services, a Gateway cache is.** They are not competitors — you use both, at different layers.

### Decision: one Redis from Phase 1

A single Redis instance, present from the start.

**Why (the real justification — not single-DB load).** Its job is the cross-service aggregation the materialized views cannot do, plus shared/common data. What goes in and what stays out:

- **In:** cross-service dashboard aggregations (overview, sidenav counts; TTL 30–60s); shared/common lookup documents and other repeated info; session data; rate-limit counters; live vehicle positions; later, the SignalR backplane once Notification runs more than one instance.
- **Out:** map bounding-box queries (too varied to cache usefully); single-entity detail reads; live work-order state. Within-service report stats stay in materialized views, not Redis.
- **Invalidation:** driven by the RabbitMQ events already published (e.g. on `PotholeSaved`, drop the affected dashboard keys), so the TTL is only a safety net and the dashboard stays fresh.

**Why not Redis Pub/Sub for real-time:** the system already has RabbitMQ → Notification service → SignalR as its real-time path. Adding Redis Pub/Sub on top would duplicate that. Redis is used for state (positions, presence, locks, backplane), not as a second event bus.

### The map and reports

- **Map:** each service exposes a bounding-box endpoint with filters → GeoJSON, backed by a GIST index. The frontend requests one layer per service *in parallel* (potholes, assets, violations, buildings) and clusters points in the browser. Only open items are shown (clustering merges repeat detections; fixed items drop off), so point counts stay far lower than a naive "all points" estimate.
- **Reports:** materialized view inside each service; the Gateway aggregates their fast results in parallel and holds the combined answer in Redis.

Note on the "180ms dashboard" worry: that figure assumed the Gateway calls the five services *sequentially*. Calling them *in parallel* makes the total ≈ the slowest single service (~30ms), and materialized views bring each service's own part down further. Parallel fan-out plus in-Gateway caching is why the dashboard is fast without heavy machinery.

### The scaling ladder — start simple, climb only on a measured trigger

Numbers are rough estimates; the real decision is always by measurement. The point is that **each rung solves a different, specific bottleneck** — that's what makes it engineering rather than buzzword-stacking.

**Phase 1 — start here (up to ~100 vehicles, ~30 officers).** One PostgreSQL + PostGIS server (DB per service), one Redis, one RabbitMQ node, one MinIO with a retention policy, one GPU, one instance per service, Gateway fans out in parallel.

| Trigger | Action |
| --- | --- |
| `FrameReceived` queue backs up and won't drain (consumer lag) — usually the first thing, ~100 vehicles | Add AI workers or another GPU (competing consumers) |
| Storage size/cost climbs | Shorten retention, or keep only frames that had a detection |
| Need >1 Gateway or Notification instance (availability, or officers past ~50) | Redis's real trigger — shared cache + SignalR backplane |
| Map viewport returns >5–10k items or payload > a few MB | At low zoom, return server-side aggregated counts via `ST_SnapToGrid` instead of points |
| Detection-history table passes tens of millions of rows | Partition by month on the same server (easy pruning) |

**Phase 2 — full municipality (hundreds of vehicles, 50–150 officers).** Add multiple GPUs, multiple instances of hot services, a small RabbitMQ cluster. DB often still one server.

| Trigger | Action |
| --- | --- |
| One service dominates the shared server's resources | Move just that service's DB to its own server (cheaper/simpler than a replica) |
| CPU steadily >70%, or p95 on indexed queries >200ms after tuning — usually past ~150 active officers | Add a read replica for that service only |
| Server-side aggregation no longer enough; map slow with tens of thousands of open items | Add a tile server (Martin or pg_tileserv) on the PostGIS replica |

**Phase 3 — big city / multiple cities (thousands of vehicles) — describe, don't build.** Kubernetes for autoscaling/self-healing; CDN in front of blob storage and vector tiles; data lake for historical analytics. Sharding only if the numbers above are ever exceeded — likely never.

### Which bottleneck each rung solves

Gateway cache → cross-service aggregation latency. Materialized views → within-service report cost. Competing consumers / GPUs → AI throughput (the real early bottleneck). Retention policy → storage cost. Read replica → read throughput. Tile server → map render volume. Sharding → write/data volume (last resort). CDN → geographic distribution.

### Read replicas (master/slave), specifically

**What it is:** a copy of a service's database that is read-only and kept in sync automatically from the primary (streaming replication, millisecond lag). The primary (master) takes all writes; the replica (slave) takes reads.

**What triggers it:** the writes and reads on one service's database start competing — CPU steadily above ~70%, or p95 latency on already-indexed queries creeping past ~200ms after tuning, usually once there are more than ~150 active officers hammering the dashboard. That is the signal that one machine serving both the AI-worker writes and the officer reads is no longer enough.

**How it's used in code:** two connection strings per service — writes go to the primary, reads go to the replica. You can add several replicas for more read capacity, and if the primary fails a replica can be promoted to primary (a failover/availability benefit on top of the performance one).

**Why not from day one:** at Phase 1 volumes a single well-indexed database serves both loads comfortably — a replica would be machinery with nothing to do. It is a Phase 2 step, and even then it is added *only to the specific service that needs it*, not blanket across all services. Note also the cheaper first move in Phase 2: if a single service is simply hogging the shared PostgreSQL server, moving *just that service's database onto its own server* is simpler and often enough before you reach for replication.

### Vector tiles and the tile server, in depth

This is the strongest tool for fast map reads at scale, and worth understanding properly because it solves a problem plain GeoJSON cannot.

**The problem it solves.** The map endpoints return GeoJSON for a bounding box. That is fine for hundreds or a few thousand points. But when an officer zooms out to the whole city, the query can match tens of thousands of features, the response balloons to many megabytes, and the browser stutters trying to draw them all. Even caching that giant response only partly helps, because every slightly different viewport is a different response.

**What vector tiles are.** The map is divided into a fixed grid of small square tiles, per zoom level. Each tile contains only the geometry inside its own area, pre-encoded in a compact binary format. The browser (MapLibre, Leaflet with a vector plugin) requests only the handful of tiles currently visible on screen at the current zoom — never the whole city. Pan or zoom, and it fetches just the newly visible tiles. The data transferred per view stays roughly constant no matter how many total features exist in the database.

**How it's served.** A tool like **Martin** or **`pg_tileserv`** connects directly to the PostGIS store (ideally a read replica), and generates the tiles automatically from your geometry tables — you point it at the table and it works, no custom tiling code. It turns "give me everything in this bbox" into "give me tile z/x/y," which is cacheable and bounded.

**Why tiles cache beautifully.** Unlike an arbitrary bounding box, a tile has a fixed, discrete address (zoom / x / y). The same tile is requested by every officer looking at that area at that zoom, so tiles cache extremely well — in the tile server, in a CDN, or in the browser. This is exactly the "cache the big picture of a bounding box" idea: at low zoom the tiles *are* the pre-rendered big picture of each area, computed once and reused by everyone, instead of every officer triggering a fresh full-area query. Tiles can also carry pre-aggregated data at low zoom (cluster counts per grid cell via `ST_SnapToGrid` or built-in clustering) so the far-out view shows "42 potholes here" rather than 42 individual points.

**What triggers adopting it.** The map slowing down under real point volume — tens of thousands of open items in a city view, or payloads past a few megabytes. Below that, browser-side clustering of a GeoJSON response is simpler and enough.

**Why not from day one.** It is another component to run and understand. At Phase 1 volumes GeoJSON + browser clustering covers the need. Vector tiles are a Phase 2 upgrade — but a high-value one the moment map reads become the bottleneck, which is why they are called out here rather than buried: if fast map reads matter, this is the tool, and it pairs naturally with a read replica (tiles served off the replica) and a CDN (tiles cached at the edge).

---

## 13. The design process (chronological narrative)

This is the order things happened, which is itself useful for a defense — it shows the design was reasoned into, not guessed.

1. **Service catalog and event pipeline first.** Identified the nine services and designed the asynchronous event flow (detections propagating from vehicles through RabbitMQ to detection and business services). Wrote the C# event contracts and mock JSON payloads.
2. **Corrected the layer framing.** An early draft implied detection/business layers; this was corrected to all-services-are-equal-peers.
3. **Added the security perimeter** in response to "how do we stop unauthorized senders?" → EMQX with mTLS/ACL/rate-limit.
4. **Added resilience patterns** in response to "I don't want it to go down under load" → concluded the architecture was already sound and these are additions (Polly, DLQ, competing consumers, health checks, backpressure).
5. **Refined event names** on review (`PotholeSaved`, `AssetAddedOrUpdated`, `ViolationSaved`; Signage service → Violation).
6. **Designed the synchronous side.** Realized the async pipeline was only half the system — the officer's request/response side needed its own design. Worked through the communication patterns.
7. **Iterated hard on the communication model.** Async-everything-with-polling → sync reads + async writes → settled on sync reads + sync writes + events, with idempotency, and the async 202 pattern reserved for genuinely long-running operations only.
8. **Chose C4 + Structurizr** for modeling (after finding IcePanel doesn't auto-generate the C4 views).
9. **Moved AI into the cloud.** Decided the vehicle is a dumb streamer; worked through Paths A/B/C for ingestion; chose event-driven Path B; worked the load math showing RabbitMQ is fine; settled save-all-for-now with a clean path to retention later.
10. **Built the gRPC contracts**, reviewed them, and fixed seven classes of issue (missing services, strings→enums, missing filters, thin responses, idempotency, blob-URL validation, enum numbering); then added the operations a CRUD-only draft missed (crew lifecycle, evaluations, dashboard, timelines, reassign/reprioritize).
11. **Started per-service internal design (L3)** with the Pothole service; chose single-store + PostGIS + materialized views over full CQRS.
12. **Worked out storage and scaling.** Found the DB is not the bottleneck (images/AI are); corrected the earlier cache advice for the database-per-service reality; decided one Redis from Phase 1 for cross-service aggregation and shared data; laid out the phased scaling ladder with measured triggers.

---

## 14. Consolidated list of rejected alternatives

A single place to point to when asked "what else did you consider?"

| Instead of… | We rejected… | Because |
| --- | --- | --- |
| Microservices + DB-per-service | Monolith | Couples the team, defeats the project's purpose, no independent scaling/failure isolation |
| DB-per-service | Microservices sharing one DB | Re-couples everything; the independence becomes fiction |
| Equal peers | Detection/business layers | Doesn't match runtime behavior; bakes a false hierarchy into the design |
| Sync reads | Async reads with polling | No benefit for reads; adds latency/complexity to the commonest operation |
| Sync writes + events | Async 202 writes as the default | Most writes are 50ms inserts; forcing a Pending lifecycle on all of them is overhead for a rare need |
| gRPC for internal reads | Query/reply over RabbitMQ | Forces the queue to do RPC's job; adds latency for transport uniformity's sake |
| Event-driven ingestion (Path B) | Path A (Vehicle Comm orchestrates AI sync) | Blocks ingestion on the slowest component; needs concurrency under load, at which point the queue is the better answer |
| Event-driven ingestion (Path B) | Path C (AI subscribes to MQTT too) | EMQX doing double duty; AI speaking two transports; duplicates work or reinvents the event chain |
| Storage lifecycle owned by Vehicle Comm | AI doing the temp→permanent copy | Separation of concerns — AI does inference, not blob management |
| Single store + materialized views per service | Full CQRS (write store + projection + read store) | Premature; projection + eventual consistency is cost we don't need at this scale |
| PostGIS for Pothole | MongoDB | Clustering is spatial-relational — PostGIS's home turf |
| Structurizr | IcePanel | IcePanel doesn't auto-generate separate C4 views; Structurizr does, from one text file |
| One Redis for cross-service aggregation | "No cache, materialized views are enough" | Materialized views can't aggregate across separate service databases |
| RabbitMQ→SignalR for real-time | Redis Pub/Sub | Would duplicate the existing real-time path |
| Deferring sharding (likely forever) | Sharding / distributed DB early | DB writes stay ~10/sec even at 1000 vehicles — the DB is not the bottleneck |

---

## 15. Still open

- **Blob storage:** decided *what* (S3-compatible; MinIO local). Still open: exact prod choice (AWS S3 likely) and the retention window (hours/days) for the temp bucket when retention is implemented.
- **How many AI services:** one to start; may split by detection domain (pothole AI, asset AI, …) later. Architecture doesn't change either way — they all subscribe to `FrameReceived` and publish `RawDetectionReceived`.
- **Identity provider:** Keycloak vs Azure AD vs custom — shapes Gateway JWT validation.
- **Deployment target for Phase 1:** plain Docker Compose to start; Kubernetes is a Phase 3 story.
- **Observability stack:** Prometheus + Grafana suggested; Seq/Jaeger for logs/tracing — not finalized.
- **Timezone strategy:** store UTC and convert at display, vs store `+03:00` throughout. Affects every timestamp.
- **Proto organization:** shared proto library alongside `SmartCity.Contracts` (chosen shape so far) vs per-service protos generating client stubs.

---

## 16. Artifacts produced

| Artifact | What it is | Where |
| --- | --- | --- |
| `SmartCity.Contracts/` | C# event records, enums, common types, mock JSON | outputs folder |
| `SmartCity.Contracts/Protos/` | `common`, `enums`, `queries`, `commands` `.proto` files | outputs folder |
| `smartcity-workspace.dsl` | Structurizr DSL — auto-generates C1/C2 diagrams | outputs folder |
| `smartcity-system-design.html` | Published visual design page (v1.1) | outputs folder + published artifact |
| `icepanel-landscape-import.yaml` | IcePanel import (superseded by Structurizr, kept for reference) | outputs folder |
| `architecture-decisions.md` | Shorter decision summary | outputs folder |
| `smartcity-design-record.md` | **This document** — the full record | outputs folder |
| SmartCity — Architecture Roadmap | Living roadmap doc (C4-structured plan, phases A/B/C) | Claude Doc |

---

## 17. Glossary

- **AMQP** — Advanced Message Queuing Protocol; the wire protocol RabbitMQ speaks.
- **BFF** — Backend for Frontend; the API Gateway pattern of one tailored entry point per client family.
- **CQRS** — Command Query Responsibility Segregation; separating the write model/path from the read model/path.
- **Competing consumers** — multiple instances of a service pulling from one queue, each message handled once; the horizontal-scaling mechanism.
- **DLQ** — Dead-Letter Queue; where messages go after failing all retries, for inspection/replay.
- **GIST index** — the PostgreSQL/PostGIS spatial index type that makes proximity queries fast.
- **Idempotency key** — a client-generated id that makes a retried write safe (same key → same result, no duplicate).
- **Materialized view** — a stored, pre-computed result of an expensive query, refreshed on a schedule; a within-database report cache.
- **mTLS** — mutual TLS; both sides present certificates, so the server authenticates each device.
- **Projection** — the component that copies/reshapes data from a write store into a read store in CQRS (not used here — we chose single-store).
- **PostGIS** — the geospatial extension to PostgreSQL (points, distance queries, spatial indexes).
- **SourceDetectionId** — the id threaded through every event for end-to-end traceability from frame to work order.
- **Vector tiles** — map data sliced into per-zoom, per-area tiles so the browser fetches only what's on screen.
