# QueryDemos

Live demos for **STY1004 Software Development — Classes 13–14**: advanced LINQ with EF Core 10,
DTO projections, the N+1 problem, split queries, SQL injection, execution plans and indexes.
Each numbered step matches a slide in the lecture deck.

## What you need

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- SQL Server — either **Docker** (any OS, uses `docker-compose.yml`) or **LocalDB** (Windows)
- Optional: [SQL Server Management Studio](https://learn.microsoft.com/en-us/sql/ssms/download-sql-server-management-studio-ssms)
  to show graphical execution plans in Demo 9

## Quick start

```bash
docker compose up -d          # start SQL Server (wait ~20 s the first time)
dotnet tool restore           # installs dotnet-ef 10.x (pinned in dotnet-tools.json)
dotnet build
dotnet run -- setup           # create the database (applies migrations) and demo data
dotnet run -- bigseed         # add ~1M older readings (needed for Demo 9)
dotnet run -- 1               # run a demo step
dotnet run                    # list all steps and options
```

### Using LocalDB instead of Docker (Windows)

PowerShell, for the current terminal session:

```powershell
$env:QUERYDEMOS_DB = "Server=(localdb)\MSSQLLocalDB;Database=QueryDemos;Trusted_Connection=True;TrustServerCertificate=True"
dotnet run -- setup
```

## Steps

| Step | Slides | Demo |
|------|--------|------|
| `setup` | — | Recreate the database with demo data |
| `bigseed` | — | Add ~1,000,000 older readings (`bigseed 500000` for a different amount) |
| `1` | 3 | A LINQ query is a plan: `ToQueryString()`, materialize last |
| `2` | 4–5 | Inner join (navigation and `Join`), `LeftJoin` (.NET 10), group-join shape |
| `3` | 6–7 | Zone report: `GroupBy`, aggregates, DTOs vs over-fetching |
| `4` | 8 | Null handling and translation gotchas — each one fails, then gets fixed |
| `5` | 9 | The N+1 problem, with every round trip counted |
| `6` | 10 | Fixing N+1: projection, filtered `Include`, batch `Contains`, explicit loading |
| `7` | 11 | Cartesian explosion vs `AsSplitQuery()` |
| `8` | 12 | SQL injection: concatenation vs parameters (`--destructive` drops a scratch table) |
| `9` | 13–14 | Execution plans, logical reads and indexes (`--add-index`, `--add-time-index`, `--drop-index`) |
| `all` | 3–12 | Run steps 1–8 in order |

Useful options: `--latency 5` (imitate a cloud database), `--quiet`, `--ef-log`, `--pause`.

## Migrations

The schema comes from EF Core migrations in `Migrations/`, the same workflow as Lab 2
(think Django's `makemigrations` and `migrate`). `dotnet ef` finds the `DbContext` through
`Data/WarehouseDbFactory.cs`, because a console app has no dependency injection for it to use.

```bash
dotnet ef migrations list                 # which migrations exist / are applied
dotnet ef migrations script               # the SQL they run
dotnet ef migrations add <Name>           # after changing the entities or OnModelCreating
dotnet run -- setup                       # drop the database and apply every migration
```

Commit all three generated files per migration (`…_Name.cs`, `…_Name.Designer.cs`,
`WarehouseDbModelSnapshot.cs`).

## Running it with Claude Code

Open this folder in Claude Code. `CLAUDE.md` tells it how to set up the database, build, run each step
and explain the output. In class you can simply say, for example:

- "Set up the demos and smoke-test them" (before class)
- "Run step 5, then run it again with 5 ms latency"
- "Run step 9, add the index, and run it again — explain what changed"

## How it works

- `Infrastructure/CommandCounter.cs` is an EF Core `DbCommandInterceptor` that counts and prints
  every SQL command — that's what makes round trips visible.
- `Infrastructure/Probe.cs` warms each query up once, then measures commands, elapsed time and memory.
- Demo 9 reads logical reads via `SET STATISTICS IO` and the plan's operators from SQL Server's plan cache.

## Notes

- The SA password in `docker-compose.yml` and `Data/Db.cs` is for this local demo only. Real
  applications keep secrets in user secrets, environment variables or a vault.
- Demo 8 contains deliberately vulnerable code. Don't copy it into real projects.
