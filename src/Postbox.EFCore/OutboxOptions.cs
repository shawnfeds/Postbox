namespace Postbox.EFCore;

public sealed class OutboxOptions
{
    public int MaxRetryCount { get; set; } = 5;
    public int MaxPayloadBytes { get; set; } = 65536;
    public int MaxDegreeOfParallelism { get; set; } = 4;
    public int LockDurationSeconds { get; set; } = 30;
    public int BatchSize { get; set; } = 10;
}