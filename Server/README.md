# Jumping Ninja online service

The ASP.NET Core service provides registration/login and the authenticated
Ninja/leaderboard API. A user owns up to 20 cloud Ninjas; the leaderboard has
one row per account and uses that account's highest Ninja score. Scores and
their first server timestamp are monotonic, so retries and lower submissions
are harmless. This release deliberately provides basic abuse protection
(ownership checks, rate limits, monotonic updates and idempotent imports), not
client-side cheat-proof scoring.

## Local Compose

Create `Server/.env.local` from `.env.example` and replace both placeholders.
Never put VPS credentials or production secrets in the repository. Start the
local API with:

```powershell
docker compose --env-file .env.local up -d --build
```

The API is available at `http://127.0.0.1:5050`. Relational databases run the
checked-in EF migrations at startup; the in-memory test host still uses
`EnsureCreated`.

```powershell
Invoke-WebRequest http://127.0.0.1:5050/health
docker compose --env-file .env.local ps
docker compose --env-file .env.local down
```

`down -v` deletes the local PostgreSQL data and is intentionally not part of
the normal verification flow. Do not use it against the VPS volume.

## Authenticated API

All routes below require `Authorization: Bearer <JWT>` and derive the account
from the JWT `sub` claim; request bodies cannot select another account.

| Route | Purpose |
| --- | --- |
| `GET /api/v1/ninjas` | Cloud snapshot, capacity (`maxNinjas = 20`) and account best |
| `POST /api/v1/ninjas` | Create a unique Ninja name (201) |
| `POST /api/v1/ninjas/import` | Idempotently import a legacy v1 profile; accepts compact `N` and hyphenated `D` GUID strings, and same names merge |
| `PUT /api/v1/ninjas/{id}/best-score` | Monotonic best-score update and current account rank |
| `GET /api/v1/leaderboard?limit=50` | Top accounts (maximum 100) plus current account rank |
| `GET /api/v1/leaderboard/targets?fromScore=N&limit=20` | Distinct score milestones, excluding current account |

Stable error codes include `ninja_name_invalid`, `ninja_name_taken`,
`ninja_limit_reached`, `legacy_profile_claimed`, `legacy_profile_invalid`,
`score_invalid`, `ninja_not_found`, and `rate_limited`. `legacyProfileId` is
received as text at the HTTP boundary and parsed with `Guid.TryParse`, so old
clients that still send `Guid.ToString("N")` continue to work; malformed text
gets the stable `legacy_profile_invalid` response instead of framework 400 JSON.

## Database migration

The first release used `EnsureCreated`, so existing VPS databases have no EF
migration history. Before deploying this build:

1. Back up PostgreSQL and record the current API image ID.
2. On a copy of the existing database, run
   [`baseline-existing-database.sql`](./baseline-existing-database.sql) with
   `psql`. The script verifies the expected Identity tables/columns, creates
   only `__EFMigrationsHistory`, and records
   `20260902141820_IdentityBaseline` without recreating any Identity table.
3. Start the new API. It applies `20260902141849_AddOnlineLeaderboard`
   automatically and keeps all existing accounts.
4. Verify the health/auth/leaderboard smoke tests before public traffic.

Example (replace the connection values with the protected VPS values; do not
commit them):

```bash
psql "$DATABASE_URL" -v ON_ERROR_STOP=1 -f baseline-existing-database.sql
```

For a fresh database, simply start the API: both migrations run from zero.
Never remove or recreate the PostgreSQL volume as a migration strategy.

## VPS deployment

The production Compose project is `/opt/jumping-ninja-auth`. Nginx and the
public address remain unchanged: `https://jumpingninja.dukechen.top:9443`.
Nginx proxies to API loopback port `15050`; PostgreSQL is private to the
Compose network.

```bash
cd /opt/jumping-ninja-auth
docker compose --env-file .env.vps -f docker-compose.vps.yml -p jumping-ninja-auth up -d --build
docker compose --env-file .env.vps -f docker-compose.vps.yml -p jumping-ninja-auth ps
docker compose --env-file .env.vps -f docker-compose.vps.yml -p jumping-ninja-auth restart
```

Keep the previous image ID and database backup until the new health, login,
migration, score, leaderboard, restart-persistence and rollback checks pass.
If the new API fails, restore the previous image; the additive leaderboard
tables are ignored by the old authentication code.

Production deployment status (2026-09-03): the leaderboard API is live and the
N/D legacy-ID compatibility fix is deployed. The running image is
`sha256:e5cbeef430e7e327622eea5aa084f5502127e3afd9f3af8bcb4a3b8b95198f30`;
the previous leaderboard image is tagged
`jumping-ninja-auth-api:pre-import-fix-20260903`, while the auth-only rollback
image remains `jumping-ninja-auth-api:pre-leaderboard-20260903`. The fresh
verified database backup is
`/opt/jumping-ninja-auth/backups/jumpingninja-pre-import-fix-20260903T130209Z.dump`
with SHA-256
`DA29D3B158AA060FD2755352A7F7482D8688DCC13AC653608277C081EA07BE82`.
Both EF migrations remain registered, all pre-existing accounts were
preserved, public N/D import and malformed-ID smoke tests passed, and the
exact probe account was removed afterward. No PostgreSQL volume was recreated.

## Smoke tests

Authentication checks:

```powershell
.\verify-auth.ps1
```

Online feature checks (registers a unique probe account, creates two Ninjas,
submits scores, verifies account aggregation, board/targets and monotonic
updates):

```powershell
.\verify-leaderboard.ps1
.\verify-leaderboard.ps1 -VerifyPersistence
```

Use `-BaseUrl "https://jumpingninja.dukechen.top:9443"` for a public smoke
run. Public probes use a unique account; remove that exact account and its
cascade data from the database after verification so the production board is
not polluted. The scripts never print passwords or tokens.
