# QueryDemos — classroom runbook for Claude Code

This is a live-demo console app for **STY1004 Software Development, Classes 13–14** (Cambrian College):
advanced LINQ with EF Core 10, DTO projections, the N+1 problem, split queries, SQL injection,
execution plans and indexes. The instructor runs it on a projector and asks you to run steps and
explain the output to students. Each step maps to a slide in the lecture deck.

## Stack

- .NET 10, EF Core 10 (`Microsoft.EntityFrameworkCore.SqlServer` 10.0.x), C# 14.
- SQL Server 2022 in Docker (`docker-compose.yml`), or LocalDB on Windows.
- Connection string: environment variable `QUERYDEMOS_DB`; if unset, the Docker default in `Data/Db.cs`.
- Model (same as Lab 2): `GatewayDevice` 1 → many `TelemetryRecord` (DbSet `Readings`), plus
  `MaintenanceLog` (second collection, used in Demo 7). Seed: 99 gateways in 4 zones with ~50 readings
  each in the last 24 h, plus `GW-EMPTY` in zone `Freezer-Annex` with no readings.
- Schema: EF Core migrations in `Migrations/` (first one: `InitialCreate`), the same workflow as Lab 2
  and Capstone Phase 1. `setup` drops the database and applies them with `MigrateAsync()` (not
  `EnsureCreated`). `dotnet ef` is a local tool pinned in `dotnet-tools.json`; it builds the context
  through `Data/WarehouseDbFactory.cs` (design-time only, no SQL printer). Demo 9's two indexes are
  deliberately *not* in the model or migrations — Demo 9 adds and drops them at runtime.

## First-time setup (before class — not live)

1. `dotnet --version` must report 10.x. If not, tell the instructor to install the .NET 10 SDK.
2. `docker compose up -d`, then wait ~20 seconds for SQL Server to accept logins.
   (Windows with LocalDB instead: set `QUERYDEMOS_DB` — see README.)
3. `dotnet tool restore` (installs the pinned `dotnet-ef`); `dotnet ef --version` should report 10.x.
4. `dotnet build`. **This code was written without being compiled.** If the build fails, fix the
   errors with the smallest change that keeps each demo's intent. Do not "fix" the intentional
   problems: Demo 4's runtime failures and Demo 8's vulnerable `FromSqlRaw` concatenation are the lesson.
   An EF Core analyzer warning on Demo 8's concatenation is expected — keep it and point it out.
5. `dotnet run -- setup` (drops the database and recreates it by applying the migrations).
6. `dotnet run -- bigseed` (adds ~1,000,000 older readings for Demo 9; about a minute).
7. Smoke test: `dotnet run -- all --quiet`, then `dotnet run -- 9`. Report anything that throws
   outside the `try/catch` blocks that are meant to show errors.
8. Reset Demo 9 to its starting point: `dotnet run -- 9 --drop-index`.

If the entity model changes later: `dotnet ef migrations add <Name>`, then `dotnet run -- setup`.
`setup` fails with a pending-model-changes error when the model and the latest migration disagree.

## In class: when asked to run a step

- Run exactly `dotnet run -- <step>` (plus any flags the instructor asks for) from the repo root.
- Don't edit code, re-seed, or change connection settings unless asked.
- The terminal already shows the output — don't paste it back. Instead give a short explanation
  for students reading a projector: **3–5 bullets, plain language, quote the key numbers**
  (commands sent, rows, elapsed ms, logical reads), and name the slide.
- Never use `--pause` yourself (it waits for Enter); it's for the instructor at a keyboard.
- If students ask about migrations, the safe read-only commands to show are `dotnet ef migrations list`
  and `dotnet ef migrations script` (prints the SQL). Don't add or remove migrations unless asked.
  Equivalents: Django `makemigrations`/`migrate`, Prisma `migrate dev`, Flyway/Liquibase for Java.
- If students ask "how would this look in Python/JavaScript/Java?", answer briefly with
  Django/SQLAlchemy, Prisma/Node or JPA/Hibernate equivalents.

## Steps and what to point out

| Step | Slide | Shows | Point out |
|------|-------|-------|-----------|
| `1` | 3 | IQueryable is a plan; `ToQueryString()`; materialize last | 0 commands until `ToListAsync()`; ToList-before-Where pulls all 100 rows and has no WHERE; `zone` travels as `@zone` |
| `2` | 4–5 | Inner join via navigation vs `Join()`; .NET 10 `LeftJoin`; group-join shape | Same INNER JOIN both ways; `LeftJoin` gives `LEFT JOIN` and NULL for `GW-EMPTY`; counts and latest reading are subqueries in one statement |
| `3` | 6–7 | `GroupBy` + aggregates into `ZoneReportDto`; over-fetching; `DeviceStatusDto` | `GROUP BY … HAVING COUNT(*) >= @p`; rows from server ~4 vs ~5,000; elapsed and memory difference |
| `4` | 8 | Null and translation gotchas, each failing then fixed | AVG over zero rows → exception, fixed with `(double?)`; one empty gateway breaks a whole projection; C# method in `Where` → "could not be translated" |
| `5` | 9 | The N+1 loop | 100 gateways → **101 commands**; first 3 SQL commands printed, the rest counted |
| `6` | 10 | Fixes: projection, filtered `Include`, batch `Contains`, explicit loading | Summary table: 101 → 1 → 1 → 2 commands; EF Core 10 sends `Contains` lists as `IN (@ids1, @ids2, …)` |
| `7` | 11 | Cartesian explosion vs `AsSplitQuery()` | Single query ≈ 32,000 rows vs split ≈ 5,700 rows in 3 commands; trade-offs: round trips and consistency |
| `8` | 12 | SQL injection | `x' OR '1'='1` returns all 100 gateways through concatenation; LINQ, `FromSql` and `FromSqlRaw` + `{0}` all return 0 |
| `9` | 13–14 | Execution plans, logical reads, indexes | Before: a scan (or seek + Key Lookup) with thousands of logical reads; after `--add-index`: Q1 is an Index Seek with single-digit reads, Q2 unchanged (column order); `--add-time-index` fixes Q2 |

## Live variations the instructor may ask for

- **Latency:** `dotnet run -- 5 --latency 5` imitates a cloud database (5 ms per command), so the
  N+1 loop costs 500+ ms. Compare with `dotnet run -- 6 --latency 5`.
- **CS8072:** in `Demos/Demo04_NullsAndTranslation.cs`, uncomment the line marked `LIVE DEMO`,
  run `dotnet build` to show the compiler error, then comment it out again and rebuild.
- **Destructive injection:** `dotnet run -- 8 --destructive` drops the scratch table
  `InjectionVictim` through the vulnerable line. Demo tables are not touched.
- **Indexes:** `dotnet run -- 9 --add-index`, then `dotnet run -- 9 --add-time-index`,
  then `dotnet run -- 9 --drop-index` to reset. The plan's operators are printed root first;
  SSMS (Ctrl+M) draws the same plan right to left. Each Demo 9 query prints paste-ready SQL for SSMS.
- **EF Core's own logging:** add `--ef-log` to show `LogTo` + `EnableSensitiveDataLogging` output
  (slide 3) instead of the demo's SQL printer.
- **Quiet mode:** `--quiet` hides SQL and keeps only results and metrics.

## Troubleshooting

- `SQL Server error` on connect: is the container running (`docker ps`)? First start takes ~20 s.
  On Apple Silicon, Docker Desktop needs Rosetta emulation enabled for the SQL Server image.
- Empty results in Demos 1–8: run `dotnet run -- setup` again (it recreates everything).
- Demo 9 shows "plan not found in the cache": the numbers are still valid; open the printed SQL in SSMS.
- Elapsed times vary from run to run; the command counts and row counts are what matter.

## Sources (Microsoft Learn)

- What's new in EF Core 10 — https://learn.microsoft.com/en-us/ef/core/what-is-new/ef-core-10.0/whatsnew
- Join operations in LINQ — https://learn.microsoft.com/en-us/dotnet/csharp/linq/standard-query-operators/join-operations
- Complex query operators — https://learn.microsoft.com/en-us/ef/core/querying/complex-query-operators
- Efficient querying — https://learn.microsoft.com/en-us/ef/core/performance/efficient-querying
- Single vs. split queries — https://learn.microsoft.com/en-us/ef/core/querying/single-split-queries
- SQL queries — https://learn.microsoft.com/en-us/ef/core/querying/sql-queries
- Performance diagnosis — https://learn.microsoft.com/en-us/ef/core/performance/performance-diagnosis
- Indexes — https://learn.microsoft.com/en-us/ef/core/modeling/indexes
