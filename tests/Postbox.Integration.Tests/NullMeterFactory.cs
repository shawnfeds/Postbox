using System.Diagnostics.Metrics;

namespace Postbox.Integration.Tests;

public sealed class NullMeterFactory : IMeterFactory
{
    public static readonly NullMeterFactory Instance = new();
    private readonly Meter _meter = new("Postbox.EFCore");
    public Meter Create(MeterOptions options) => _meter;
    public void Dispose() => _meter.Dispose();
}