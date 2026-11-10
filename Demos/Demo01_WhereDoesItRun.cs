using Microsoft.EntityFrameworkCore;
using QueryDemos.Data;
using QueryDemos.Infrastructure;

namespace QueryDemos.Demos;

/// <summary>Slide 3 — an IQueryable is a plan, not a result. See the SQL; materialize last.</summary>
public static class Demo01_WhereDoesItRun
{
    public static async Task RunAsync()
    {
        Out.Title("Demo 1 · A LINQ query is a plan, not a result", "Slide 3");
        await using var db = Db.Create();
        var counter = CommandCounter.Instance;

        Out.Part("A. Build an IQueryable — nothing is sent to SQL Server yet");
        counter.Reset();
        IQueryable<GatewayDevice> query = db.Gateways
            .Where(g => g.IsOnline)
            .OrderBy(g => g.DeviceCode);
        Out.Metric("Commands sent after building the query", counter.Count);

        Out.Note("query.ToQueryString() shows the SQL without running it:");
        Out.Code(query.ToQueryString());
        Out.Metric("Commands sent after ToQueryString()", counter.Count);

        Out.Part("B. Materialize with ToListAsync() — now the SQL runs");
        var online = await query.ToListAsync();
        Out.Metric("Commands sent after ToListAsync()", counter.Count);
        Out.Metric("Online gateways returned", online.Count);

        Out.Part("C. Materialize too early vs. filter in the database");
        var zone = "Dock-East";

        Out.Note("Too early:  (await db.Gateways.ToListAsync()).Where(g => g.InstallationZone == zone)");
        var early = await Probe.RunAsync(async () =>
        {
            var everything = await db.Gateways.AsNoTracking().ToListAsync();     // SELECT … with no WHERE
            var kept = everything.Where(g => g.InstallationZone == zone).ToList(); // filtered in C#
            return (Pulled: everything.Count, Kept: kept.Count);
        });

        Out.Note("Correct:    await db.Gateways.Where(g => g.InstallationZone == zone).ToListAsync()");
        var late = await Probe.RunAsync(async () =>
        {
            var kept = await db.Gateways.AsNoTracking()
                .Where(g => g.InstallationZone == zone)                           // WHERE … = @zone
                .ToListAsync();
            return (Pulled: kept.Count, Kept: kept.Count);
        });

        Console.WriteLine();
        Out.Table(
            ["Variant", "Rows pulled from SQL Server", "Rows kept", "Elapsed"],
            [
                ["ToList() before Where", Out.Format(early.Result.Pulled), Out.Format(early.Result.Kept), Out.Format(early.Elapsed)],
                ["Where before ToList()", Out.Format(late.Result.Pulled), Out.Format(late.Result.Kept), Out.Format(late.Elapsed)],
            ]);
        Out.Note("Same answer — but the first version downloads the whole table and filters in C#.");
        Out.Note("Notice `zone` travels as a parameter (@zone), not as text pasted into the SQL.");
    }
}
