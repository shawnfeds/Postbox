namespace Postbox.EFCore;

public sealed class OutboxOptions
{
    public int MaxRetryCount { get; set; } = 5;
    public int MaxPayloadBytes { get; set; } = 65536; // 64KB default
    public int MaxDegreeOfParallelism { get; set; } = 4;
}