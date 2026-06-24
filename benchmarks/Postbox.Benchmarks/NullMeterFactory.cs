using System.Diagnostics.Metrics;

namespace Postbox.Benchmarks;

public sealed class NullMeterFactory : IMeterFactory
{
    private readonly Meter _meter = new("Postbox.EFCore");
    public Meter Create(MeterOptions options) => _meter;
    public void Dispose() => _meter.Dispose();
}