-- One database + one login per stateful service (database-per-service).
-- Runs once, on the first start of an empty postgres volume.
-- To re-run: docker compose down -v && docker compose up -d
-- Vehicle Communication, AI Detection and the Gateway are stateless: no database.

CREATE USER pothole_svc      WITH PASSWORD 'pothole_svc';
CREATE USER inventory_svc    WITH PASSWORD 'inventory_svc';
CREATE USER violation_svc    WITH PASSWORD 'violation_svc';
CREATE USER building_svc     WITH PASSWORD 'building_svc';
CREATE USER costcalc_svc     WITH PASSWORD 'costcalc_svc';
CREATE USER workorder_svc    WITH PASSWORD 'workorder_svc';
CREATE USER notification_svc WITH PASSWORD 'notification_svc';

CREATE DATABASE pothole_db      OWNER pothole_svc;
CREATE DATABASE inventory_db    OWNER inventory_svc;
CREATE DATABASE violation_db    OWNER violation_svc;
CREATE DATABASE building_db     OWNER building_svc;
CREATE DATABASE costcalc_db     OWNER costcalc_svc;
CREATE DATABASE workorder_db    OWNER workorder_svc;
CREATE DATABASE notification_db OWNER notification_svc;

-- Services must not connect to each other's databases.
REVOKE CONNECT ON DATABASE pothole_db, inventory_db, violation_db, building_db,
                           costcalc_db, workorder_db, notification_db FROM PUBLIC;
GRANT CONNECT ON DATABASE pothole_db      TO pothole_svc;
GRANT CONNECT ON DATABASE inventory_db    TO inventory_svc;
GRANT CONNECT ON DATABASE violation_db    TO violation_svc;
GRANT CONNECT ON DATABASE building_db     TO building_svc;
GRANT CONNECT ON DATABASE costcalc_db     TO costcalc_svc;
GRANT CONNECT ON DATABASE workorder_db    TO workorder_svc;
GRANT CONNECT ON DATABASE notification_db TO notification_svc;

-- PostGIS in every spatial service database (needs superuser, so done here).
\connect pothole_db
CREATE EXTENSION IF NOT EXISTS postgis;
\connect inventory_db
CREATE EXTENSION IF NOT EXISTS postgis;
\connect violation_db
CREATE EXTENSION IF NOT EXISTS postgis;
\connect building_db
CREATE EXTENSION IF NOT EXISTS postgis;
\connect workorder_db
CREATE EXTENSION IF NOT EXISTS postgis;
