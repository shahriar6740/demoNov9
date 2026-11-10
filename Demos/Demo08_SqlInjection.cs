using Microsoft.EntityFrameworkCore;
using QueryDemos.Data;
using QueryDemos.Infrastructure;

namespace QueryDemos.Demos;

/// <summary>Slide 12 — SQL injection through string concatenation, and the safe alternatives.</summary>
public static class Demo08_SqlInjection
{
    public static async Task RunAsync(bool destructive)
    {
        Out.Title("Demo 8 · SQL injection: concatenation is the vulnerability", "Slide 12");
        await using var db = Db.Create();

        int total;
        using (CommandCounter.Instance.Quiet())
        {
            total = await db.Gateways.CountAsync();
        }

        // Imagine this came from a search box.
        var code = "x' OR '1'='1";
        Out.Note($"User input:  code = \"{code}\"   (no gateway has this code, so the right answer is 0 rows)");

        Out.Part("A. VULNERABLE — user input concatenated into FromSqlRaw");
        // EF Core 10's analyzer flags the next statement at build time. That warning is the lesson — keep it.
        var leaked = await db.Gateways
            .FromSqlRaw("SELECT * FROM Gateways WHERE DeviceCode = '" + code + "'")
            .AsNoTracking()
            .ToListAsync();
        Out.Bad($"Returned {leaked.Count} of {total} gateways — the input rewrote the WHERE clause.");

        Out.Part("B. Safe — LINQ: every C# value is sent as a parameter");
        var viaLinq = await db.Gateways.AsNoTracking().Where(g => g.DeviceCode == code).ToListAsync();
        Report(viaLinq.Count);

        Out.Part("C. Safe — FromSql: the {code} hole in the FormattableString becomes a DbParameter");
        var viaFromSql = await db.Gateways
            .FromSql($"SELECT * FROM Gateways WHERE DeviceCode = {code}")
            .AsNoTracking()
            .ToListAsync();
        Report(viaFromSql.Count);

        Out.Part("D. Safe — FromSqlRaw with a {0} placeholder (never + or $ inside it)");
        var viaPlaceholder = await db.Gateways
            .FromSqlRaw("SELECT * FROM Gateways WHERE DeviceCode = {0}", code)
            .AsNoTracking()
            .ToListAsync();
        Report(viaPlaceholder.Count);

        Console.WriteLine();
        var verified = viaLinq.Count == 0 && viaFromSql.Count == 0 && viaPlaceholder.Count == 0;
        if (verified) Out.Good("Verified: all three parameterized versions return 0 rows for the attack string.");
        else Out.Bad("Unexpected: a parameterized version returned rows.");

        Out.Part("E. Destructive payload against a scratch table");
        if (!destructive)
        {
            Out.Note("Skipped. Run  dotnet run -- 8 --destructive  to watch  x'; DROP TABLE InjectionVictim;--");
            Out.Note("drop a throwaway table through the same vulnerable line (the demo tables are not touched).");
            return;
        }

        await db.Database.ExecuteSqlRawAsync(
            "IF OBJECT_ID(N'dbo.InjectionVictim') IS NULL CREATE TABLE dbo.InjectionVictim (Id int NOT NULL);");
        Out.Note($"InjectionVictim exists before the attack: {await VictimExistsAsync(db)}");

        var dropPayload = "x'; DROP TABLE InjectionVictim;--";
        Out.Note($"User input:  code = \"{dropPayload}\"");
        try
        {
            await db.Gateways
                .FromSqlRaw("SELECT * FROM Gateways WHERE DeviceCode = '" + dropPayload + "'")
                .AsNoTracking()
                .ToListAsync();
        }
        catch (Exception ex)
        {
            Out.Dim($"    ({ex.GetType().Name}: {ex.Message.Split('\n')[0]})");
        }

        var stillThere = await VictimExistsAsync(db);
        if (stillThere) Out.Note("InjectionVictim still exists.");
        else Out.Bad("InjectionVictim is gone — dropped by text typed into a search box.");
        Out.Note("Defence in depth: the app's database login should not have permission to DROP anything.");
    }

    private static void Report(int rows)
    {
        if (rows == 0) Out.Good("0 rows — the input was treated as data, not as SQL.");
        else Out.Bad($"{rows} rows returned.");
    }

    private static async Task<bool> VictimExistsAsync(WarehouseDb db)
    {
        using var _ = CommandCounter.Instance.Quiet();
        var exists = await db.Database
            .SqlQuery<int>($"SELECT CASE WHEN OBJECT_ID(N'dbo.InjectionVictim') IS NULL THEN 0 ELSE 1 END AS [Value]")
            .SingleAsync();
        return exists == 1;
    }
}
