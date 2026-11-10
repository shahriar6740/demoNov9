using System.Text;
using Microsoft.Data.SqlClient;
using QueryDemos;
using QueryDemos.Data;
using QueryDemos.Demos;
using QueryDemos.Infrastructure;

Console.OutputEncoding = Encoding.UTF8;
var options = CliOptions.Parse(args);

Out.Pause = options.Pause;
Db.EfLog = options.EfLog;
CommandCounter.Instance.Echo = !options.Quiet && !options.EfLog;
CommandCounter.Instance.LatencyMs = options.LatencyMs;

var demos = new (string Key, string Slides, string Title, Func<Task> Run)[]
{
    ("1", "3",     "A LINQ query is a plan: ToQueryString, materialize last", Demo01_WhereDoesItRun.RunAsync),
    ("2", "4–5",   "Inner join, LeftJoin (.NET 10), group-join shape",        Demo02_Joins.RunAsync),
    ("3", "6–7",   "Zone report: GroupBy, aggregates, DTOs vs over-fetching", Demo03_ZoneReport.RunAsync),
    ("4", "8",     "Null handling and translation gotchas (fail, then fix)",  Demo04_NullsAndTranslation.RunAsync),
    ("5", "9",     "The N+1 problem (counts every round trip)",               Demo05_NPlusOne.RunAsync),
    ("6", "10",    "Fixing N+1: projection, filtered Include, batch, explicit", Demo06_FixNPlusOne.RunAsync),
    ("7", "11",    "Cartesian explosion vs AsSplitQuery()",                   Demo07_SplitQueries.RunAsync),
    ("8", "12",    "SQL injection: concatenation vs parameters",              () => Demo08_SqlInjection.RunAsync(options.Destructive)),
    ("9", "13–14", "Execution plans, logical reads and indexes",              () => Demo09_ExecutionPlans.RunAsync(options)),
};

if (options.LatencyMs > 0)
    Out.Warn($"Simulating {options.LatencyMs} ms of network latency per database command.");

try
{
    switch (options.Step)
    {
        case "setup":
            await Seeder.ResetAsync();
            break;

        case "bigseed":
            await Seeder.BigSeedAsync(options.Count ?? 1_000_000);
            break;

        case "all":
            foreach (var demo in demos.Where(d => d.Key != "9"))
                await demo.Run();
            break;

        default:
            var match = demos.FirstOrDefault(d => d.Key == options.Step);
            if (match.Run is null) PrintHelp();
            else await match.Run();
            break;
    }

    return 0;
}
catch (SqlException ex)
{
    Out.Bad($"SQL Server error {ex.Number}: {ex.Message.Split('\n')[0]}");
    Out.Note("Is SQL Server running?    docker compose up -d   (wait ~20 s after the first start)");
    Out.Note("Using LocalDB instead?    set QUERYDEMOS_DB to your LocalDB connection string (see README)");
    Out.Note("Database not created yet? dotnet run -- setup");
    return 1;
}

void PrintHelp()
{
    Out.Title("QueryDemos · STY1004 Classes 13–14 live demos", ".NET 10 · EF Core 10 · SQL Server");
    Out.Note("Usage: dotnet run -- <step> [options]");
    Console.WriteLine();
    Out.Table(
        ["Step", "Slides", "What it shows"],
        [
            ["setup", "", "Recreate the database with demo data (run once)"],
            ["bigseed", "", "Add ~1,000,000 older readings for Demo 9 (bigseed <count> to change)"],
            .. demos.Select(d => new[] { d.Key, d.Slides, d.Title }),
            ["all", "", "Run demos 1–8 in order"],
        ]);
    Console.WriteLine();
    Out.Note("Options:");
    Out.Note("  --latency <ms>     add artificial network latency per command (try with step 5)");
    Out.Note("  --quiet            don't print SQL commands");
    Out.Note("  --ef-log           show EF Core's own LogTo output instead of the demo printer");
    Out.Note("  --pause            wait for Enter between parts (when you drive it yourself)");
    Out.Note("  --destructive      step 8: also drop a scratch table through the injection");
    Out.Note("  --add-index        step 9: add IX_Readings_Gateway_Time (covering, composite)");
    Out.Note("  --add-time-index   step 9: add IX_Readings_Time");
    Out.Note("  --drop-index       step 9: remove both demo indexes");
}
