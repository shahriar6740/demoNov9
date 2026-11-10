using Microsoft.EntityFrameworkCore;

namespace QueryDemos.Data;

public class WarehouseDb(DbContextOptions<WarehouseDb> options) : DbContext(options)
{
    public DbSet<GatewayDevice> Gateways => Set<GatewayDevice>();
    public DbSet<TelemetryRecord> Readings => Set<TelemetryRecord>();
    public DbSet<MaintenanceLog> MaintenanceLogs => Set<MaintenanceLog>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Everything is configured with the Fluent API (no data annotations), as in Lab 2.
        modelBuilder.Entity<GatewayDevice>(b =>
        {
            b.ToTable("Gateways");
            b.HasKey(g => g.Id);
            b.Property(g => g.DeviceCode).HasMaxLength(50).IsRequired();
            b.HasIndex(g => g.DeviceCode).IsUnique();
            b.Property(g => g.InstallationZone).HasMaxLength(50).IsRequired();

            b.HasMany(g => g.Readings)
             .WithOne(r => r.Gateway)
             .HasForeignKey(r => r.GatewayDeviceId)
             .OnDelete(DeleteBehavior.Cascade);

            b.HasMany(g => g.MaintenanceLogs)
             .WithOne(m => m.Gateway)
             .HasForeignKey(m => m.GatewayDeviceId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<TelemetryRecord>(b =>
        {
            b.ToTable("Readings");
            b.HasKey(r => r.Id);

            // EF Core creates a nonclustered index on the GatewayDeviceId foreign key by convention.
            //
            // Demo 9 adds the covering index below at runtime (so you can show the plan before and
            // after without recreating a million-row table). In a real project you would put it here
            // and generate a migration:  dotnet ef migrations add AddReadingIndexes
            //
            // b.HasIndex(r => new { r.GatewayDeviceId, r.RecordedAtUtc })
            //  .IsDescending(false, true)
            //  .IncludeProperties(r => new { r.TemperatureCelsius })
            //  .HasDatabaseName("IX_Readings_Gateway_Time");
        });

        modelBuilder.Entity<MaintenanceLog>(b =>
        {
            b.ToTable("MaintenanceLogs");
            b.HasKey(m => m.Id);
            b.Property(m => m.Technician).HasMaxLength(100);
            b.Property(m => m.Notes).HasMaxLength(500);
        });
    }
}
