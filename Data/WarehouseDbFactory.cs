using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace QueryDemos.Data;

// Used only by `dotnet ef` (migrations add, database update, …), never at runtime.
// A console app has no dependency injection for the tools to find WarehouseDb through, so this
// factory tells them how to build it. It leaves out the demo's SQL-printing interceptor, so
// migration commands stay quiet. It uses the same connection string as the app (QUERYDEMOS_DB or
// the Docker default in Db.cs).
public class WarehouseDbFactory : IDesignTimeDbContextFactory<WarehouseDb>
{
    public WarehouseDb CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<WarehouseDb>()
            .UseSqlServer(Db.ConnectionString)
            .Options);
}
