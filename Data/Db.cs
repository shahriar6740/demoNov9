using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using QueryDemos.Infrastructure;

namespace QueryDemos.Data;

public static class Db
{
    // Demo-only default that matches docker-compose.yml. Real applications keep connection strings
    // and passwords out of source code (user secrets, environment variables or a vault).
    private const string DockerDefault =
        "Server=localhost,1433;Database=QueryDemos;User Id=sa;Password=Demo_Passw0rd!;TrustServerCertificate=True";

    // Override with an environment variable, e.g. for LocalDB on Windows:
    //   setx QUERYDEMOS_DB "Server=(localdb)\MSSQLLocalDB;Database=QueryDemos;Trusted_Connection=True;TrustServerCertificate=True"
    public static string ConnectionString =>
        Environment.GetEnvironmentVariable("QUERYDEMOS_DB") is { Length: > 0 } fromEnv ? fromEnv : DockerDefault;

    /// <summary>When true, use EF Core's built-in LogTo output instead of the demo's SQL printer.</summary>
    public static bool EfLog { get; set; }

    public static WarehouseDb Create()
    {
        var builder = new DbContextOptionsBuilder<WarehouseDb>()
            .UseSqlServer(ConnectionString, sql => sql.CommandTimeout(300))
            .AddInterceptors(CommandCounter.Instance);

        if (EfLog)
        {
            builder.LogTo(Console.WriteLine, LogLevel.Information)
                   .EnableSensitiveDataLogging(); // development only: logs parameter values
        }

        return new WarehouseDb(builder.Options);
    }
}
