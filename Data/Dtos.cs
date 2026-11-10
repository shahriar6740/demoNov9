namespace QueryDemos.Data;

// DTOs: small, read-only shapes for one consumer (a report, an API response, a UI card).
// Positional records give a constructor, read-only properties and value equality in one line.

public sealed record ZoneReportDto(string Zone, int Readings, double AvgTempC, double MaxTempC, double AvgHumidity);

public sealed record DeviceStatusDto(string DeviceCode, string Zone, int Readings, DateTime? LastSeenUtc);

public sealed record GatewayCount(string DeviceCode, int Readings24h);
