using Microsoft.EntityFrameworkCore;
using QueryDemos.Data;
using QueryDemos.Infrastructure;

namespace QueryDemos.Demos;

/// <summary>Slide 11 — cartesian explosion with two sibling Includes, and AsSplitQuery().</summary>
public static class Demo07_SplitQueries
{
    public static async Task RunAsync()
    {
        Out.Title("Demo 7 · Cartesian explosion and AsSplitQuery()", "Slide 11");

        DateTime since;
        await using (var db = Db.Create())
        {
            since = (await DemoClock.NowAsync(db)).AddHours(-24);
        }

        Out.Part("A. Single query: Include(Readings) + Include(MaintenanceLogs) in one SQL statement");
        var single = await Probe.RunAsync(() => LoadAsync(since, split: false), echoFirst: 1);

        Out.Part("B. Split query: the same Includes with AsSplitQuery()");
        var split = await Probe.RunAsync(() => LoadAsync(since, split: true), echoFirst: 3);

        // Rows the database sends back. Single query: LEFT JOINs to two sibling collections multiply.
        long singleRows = single.Result.Sum(g => (long)Math.Max(1, g.Readings.Count) * Math.Max(1, g.MaintenanceLogs.Count));
        long splitRows = split.Result.Count
                         + split.Result.Sum(g => (long)g.Readings.Count)
                         + split.Result.Sum(g => (long)g.MaintenanceLogs.Count);

        Console.WriteLine();
        Out.Table(
            ["Strategy", "Commands", "Rows sent by SQL Server", "Elapsed", "Memory allocated"],
            [
                ["Single query (default)", Out.Format(single.Commands), Out.Format(singleRows), Out.Format(single.Elapsed), single.Allocated],
                ["AsSplitQuery()", Out.Format(split.Commands), Out.Format(splitRows), Out.Format(split.Elapsed), split.Allocated],
            ]);

        var sample = single.Result.First(g => g.Readings.Count > 0);
        Out.Note($"Example: {sample.DeviceCode} has {sample.Readings.Count} readings × {sample.MaintenanceLogs.Count} logs " +
                 $"= {sample.Readings.Count * Math.Max(1, sample.MaintenanceLogs.Count):N0} rows in the single query.");
        Out.Note("Trade-off: split = more round trips and no single snapshot; single = duplicated rows. Measure both.");
    }

    private static async Task<List<GatewayDevice>> LoadAsync(DateTime since, bool split)
    {
        await using var db = Db.Create();
        var query = db.Gateways.AsNoTracking()
            .Include(g => g.Readings.Where(r => r.RecordedAtUtc >= since))
            .Include(g => g.MaintenanceLogs)
            .OrderBy(g => g.DeviceCode);

        return split
            ? await query.AsSplitQuery().ToListAsync()
            : await query.AsSingleQuery().ToListAsync();
    }
}
