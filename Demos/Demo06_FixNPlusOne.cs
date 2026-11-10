using Microsoft.EntityFrameworkCore;
using QueryDemos.Data;
using QueryDemos.Infrastructure;

namespace QueryDemos.Demos;

/// <summary>Slide 10 — fixing N+1: projection, eager (filtered) Include, batch with Contains, explicit loading.</summary>
public static class Demo06_FixNPlusOne
{
    public static async Task RunAsync()
    {
        Out.Title("Demo 6 · Fix N+1 with one round trip", "Slide 10");
        await using var db = Db.Create();

        var now = await DemoClock.NowAsync(db);
        var since = now.AddHours(-24);

        Out.Part("Baseline: the N+1 loop from Demo 5 (run silently, just counted)");
        var loop = await Probe.RunAsync(() => Demo05_NPlusOne.LoopAsync(db, since), echoFirst: 0);
        Out.Metric("Commands", loop.Commands);
        Out.Metric("Elapsed", loop.Elapsed);

        Out.Part("A. Projection — best for read-only views");
        var projection = await Probe.RunAsync(() => db.Gateways
            .OrderBy(g => g.DeviceCode)
            .Select(g => new GatewayCount(
                g.DeviceCode,
                g.Readings.Count(r => r.RecordedAtUtc >= since)))
            .ToListAsync());
        Out.Rows(projection.Result, 4);

        Out.Part("B. Eager loading with a filtered Include — when you need the entities");
        var eager = await Probe.RunAsync(() => db.Gateways.AsNoTracking()
            .Include(g => g.Readings
                .Where(r => r.RecordedAtUtc >= since)
                .OrderByDescending(r => r.RecordedAtUtc)
                .Take(5))                                              // filtered include
            .OrderBy(g => g.DeviceCode)
            .ToListAsync());
        Out.Rows(eager.Result
            .Select(g => new
            {
                g.DeviceCode,
                ReadingsLoaded = g.Readings.Count,
                LatestTemp = g.Readings.FirstOrDefault()?.TemperatureCelsius   // in memory: ?. is fine here
            })
            .ToList(), 4);

        Out.Part("C. Batch load — two queries total, using Contains over the parent ids");
        var batch = await Probe.RunAsync(async () =>
        {
            var gateways = await db.Gateways
                .OrderBy(g => g.DeviceCode)
                .Select(g => new { g.Id, g.DeviceCode })
                .ToListAsync();                                        // query 1
            var ids = gateways.Select(g => g.Id).ToList();

            var counts = await db.Readings
                .Where(r => ids.Contains(r.GatewayDeviceId) && r.RecordedAtUtc >= since)   // IN (@ids1, @ids2, …)
                .GroupBy(r => r.GatewayDeviceId)
                .Select(grp => new { GatewayId = grp.Key, Count = grp.Count() })
                .ToDictionaryAsync(x => x.GatewayId, x => x.Count);    // query 2

            return gateways.Select(g => new GatewayCount(g.DeviceCode, counts.GetValueOrDefault(g.Id))).ToList();
        });
        Out.Rows(batch.Result, 4);
        Out.Note("EF Core 10 sends the id list as individual parameters (padded), which keeps SQL Server's plan cache healthy.");

        Out.Part("D. Explicit loading — the extra query is visible in your code");
        var explicitLoad = await Probe.RunAsync(async () =>
        {
            await using var ctx = Db.Create();                         // tracking query needs a fresh context
            var one = await ctx.Gateways.FirstAsync(g => g.DeviceCode == "GW-001");
            await ctx.Entry(one)
                .Collection(g => g.Readings)
                .Query()
                .Where(r => r.RecordedAtUtc >= since)
                .LoadAsync();                                          // second, explicit round trip
            return one.Readings.Count;
        });
        Out.Metric("GW-001 readings loaded explicitly", explicitLoad.Result);

        Console.WriteLine();
        Out.Table(
            ["Approach", "Commands", "Elapsed"],
            [
                ["N+1 loop (Demo 5)", Out.Format(loop.Commands), Out.Format(loop.Elapsed)],
                ["A. Projection", Out.Format(projection.Commands), Out.Format(projection.Elapsed)],
                ["B. Filtered Include", Out.Format(eager.Commands), Out.Format(eager.Elapsed)],
                ["C. Batch with Contains", Out.Format(batch.Commands), Out.Format(batch.Elapsed)],
                ["D. Explicit loading (1 gateway)", Out.Format(explicitLoad.Commands), Out.Format(explicitLoad.Elapsed)],
            ]);
    }
}
