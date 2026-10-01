# SmartCity Monitoring — walking skeleton

Starter repository for the SmartCity system (Konya). Contracts and infrastructure are ready;
the services are built milestone by milestone by following `docs/BUILD-PLAN.md`.

## Using this with Claude Code
1. Put this folder in a git repository and open it with Claude Code.
2. Claude Code reads `CLAUDE.md` automatically (rules, ports, conventions).
3. Start with: *"Read CLAUDE.md and docs/BUILD-PLAN.md, then do milestone M0 and report the Done-when checks."*
4. Continue with M1, M2, ... one at a time, reviewing each report.

## What's already here
- `src/SmartCity.Contracts/` — event records (with `IntegrationEvent` envelope), enums aligned with
  the protos, the four `.proto` files, and `MockMessages/mock-events.json` (one full trace).
- `docker-compose.yml` — PostgreSQL+PostGIS, RabbitMQ, EMQX, MinIO (+buckets), Redis, Seq.
- `infra/postgres/init/` — creates one database + login per service, enables PostGIS.
- `docs/design/` — the full design record and the decision summary.

## Infrastructure
```
cp .env.example .env
docker compose up -d
docker compose ps
```
