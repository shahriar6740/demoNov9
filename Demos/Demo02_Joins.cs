using Microsoft.EntityFrameworkCore;
using QueryDemos.Data;
using QueryDemos.Infrastructure;

namespace QueryDemos.Demos;

/// <summary>Slides 4–5 — inner join (navigation and Join), LeftJoin (.NET 10), group-join shape.</summary>
public static class Demo02_Joins
{
    public static async Task RunAsync()
    {
        Out.Title("Demo 2 · Inner, left and group joins", "Slides 4–5");
        await using var db = Db.Create();

        var now = await DemoClock.NowAsync(db);
        var since = now.AddHours(-1);
        var zone = "Cold-Vault-01";

        Out.Part("A. Inner join through the navigation property r.Gateway — EF Core writes the JOIN");
        var viaNavigation = await db.Readings
            .Where(r => r.Gateway.InstallationZone == zone && r.RecordedAtUtc >= since)
            .OrderBy(r => r.Gateway.DeviceCode).ThenBy(r => r.RecordedAtUtc)
            .Select(r => new { r.Gateway.DeviceCode, r.RecordedAtUtc, r.TemperatureCelsius })
            .ToListAsync();
        Out.Rows(viaNavigation, 5);

        Out.Part("B. The same inner join with an explicit Join()");
        var viaJoin = await db.Readings
            .Where(r => r.RecordedAtUtc >= since)
            .Join(db.Gateways.Where(g => g.InstallationZone == zone),
                  r => r.GatewayDeviceId,   // outer key
                  g => g.Id,                // inner key
                  (r, g) => new { g.DeviceCode, r.RecordedAtUtc, r.TemperatureCelsius })
            .OrderBy(x => x.DeviceCode).ThenBy(x => x.RecordedAtUtc)
            .ToListAsync();
        Out.Rows(viaJoin, 5);
        Out.Metric("Same number of rows as A", viaJoin.Count == viaNavigation.Count ? "yes" : "no");

        Out.Part("C. LeftJoin (new in .NET 10) — keeps gateways that have no matching readings");
        var leftJoined = await db.Gateways
            .Where(g => g.InstallationZone == zone || g.InstallationZone == Seeder.EmptyZone)
            .LeftJoin(db.Readings.Where(r => r.RecordedAtUtc >= since),
                g => g.Id,
                r => r.GatewayDeviceId,
                (g, r) => new
                {
                    g.DeviceCode,
                    g.InstallationZone,
                    // `?.` is not allowed in expression trees (CS8072), so test for null explicitly.
                    Temp = r == null ? (double?)null : r.TemperatureCelsius
                })
            .ToListAsync();

        // Order in memory just for display: unmatched (NULL) rows first.
        Out.Rows(leftJoined.OrderBy(x => x.Temp.HasValue).ThenBy(x => x.DeviceCode).ToList(), 6);
        Out.Metric("Rows where Temp is NULL (no matching reading)", leftJoined.Count(x => x.Temp is null));

        Out.Part("D. Group-join shape — each gateway with its readings, aggregated in SQL");
        var grouped = await db.Gateways
            .Where(g => g.InstallationZone == zone || g.InstallationZone == Seeder.EmptyZone)
            .OrderBy(g => g.DeviceCode)
            .Select(g => new
            {
                g.DeviceCode,
                Readings = g.Readings.Count(),
                LastTemp = g.Readings
                    .OrderByDescending(r => r.RecordedAtUtc)
                    .Select(r => (double?)r.TemperatureCelsius)
                    .FirstOrDefault()
            })
            .ToListAsync();
        Out.Rows(grouped, 6);
        Out.Note("One SQL statement: the counts and the latest reading are subqueries, not extra round trips.");
    }
}
