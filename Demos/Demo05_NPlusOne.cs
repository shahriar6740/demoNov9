using Microsoft.EntityFrameworkCore;
using QueryDemos.Data;
using QueryDemos.Infrastructure;

namespace QueryDemos.Demos;

/// <summary>Slide 9 — the N+1 problem: a query inside a loop.</summary>
public static class Demo05_NPlusOne
{
    public static async Task RunAsync()
    {
        Out.Title("Demo 5 · The N+1 problem", "Slide 9");
        await using var db = Db.Create();

        var now = await DemoClock.NowAsync(db);
        var since = now.AddHours(-24);

        Out.Part("A. One query for the gateways, then one more query per gateway inside a foreach");
        var run = await Probe.RunAsync(() => LoopAsync(db, since), echoFirst: 3);

        Out.Rows(run.Result, 5);
        Console.WriteLine();
        Out.Metric("Gateways", run.Result.Count);
        Out.Metric("Database commands (round trips)", run.Commands);
        Out.Metric("Elapsed", run.Elapsed);

        if (CommandCounter.Instance.LatencyMs == 0)
        {
            Out.Note("Each command is fast on a local database — that is why N+1 hides in development.");
            Out.Note("Simulate a cloud database:  dotnet run -- 5 --latency 5");
        }
        else
        {
            Out.Warn($"With {CommandCounter.Instance.LatencyMs} ms of latency per command, " +
                     $"{run.Commands} round trips cost at least {run.Commands * CommandCounter.Instance.LatencyMs:N0} ms.");
        }

        Out.Note("Next: dotnet run -- 6   (the same result in one round trip)");
    }

    /// <summary>The anti-pattern, kept in one place so Demo 6 can compare against it.</summary>
    public static async Task<List<GatewayCount>> LoopAsync(WarehouseDb db, DateTime since)
    {
        var gateways = await db.Gateways.AsNoTracking()
            .OrderBy(g => g.DeviceCode)
            .ToListAsync();                                                  // 1 query

        var results = new List<GatewayCount>();
        foreach (var g in gateways)
        {
            var count = await db.Readings                                   // +1 query, every pass
                .CountAsync(r => r.GatewayDeviceId == g.Id && r.RecordedAtUtc >= since);
            results.Add(new GatewayCount(g.DeviceCode, count));
        }

        return results;
    }
}
