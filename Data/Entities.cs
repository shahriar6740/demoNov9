namespace QueryDemos.Data;

// The same model as Lab 2: one gateway (principal) has many telemetry records (dependents).
// MaintenanceLogs is a second collection, used in Demo 7 to show cartesian explosion.

public class GatewayDevice
{
    public Guid Id { get; set; }
    public string DeviceCode { get; set; } = "";
    public string InstallationZone { get; set; } = "";
    public bool IsOnline { get; set; }

    // Collection navigations (EF Core fills them only when you Include or project them).
    public List<TelemetryRecord> Readings { get; } = [];
    public List<MaintenanceLog> MaintenanceLogs { get; } = [];
}

public class TelemetryRecord
{
    public long Id { get; set; }
    public DateTime RecordedAtUtc { get; set; }
    public double TemperatureCelsius { get; set; }
    public double HumidityPercent { get; set; }

    // Foreign key + reference navigation back to the parent.
    public Guid GatewayDeviceId { get; set; }
    public GatewayDevice Gateway { get; set; } = null!;
}

public class MaintenanceLog
{
    public int Id { get; set; }
    public DateTime PerformedAtUtc { get; set; }
    public string Technician { get; set; } = "";
    public string Notes { get; set; } = "";

    public Guid GatewayDeviceId { get; set; }
    public GatewayDevice Gateway { get; set; } = null!;
}
