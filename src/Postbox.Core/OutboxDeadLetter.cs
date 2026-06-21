namespace Postbox.Core;

public class OutboxDeadLetter
{
    public Guid Id { get; init; }
    public string Type { get; init; } = default!;
    public string Payload { get; init; } = default!;
    public DateTime OccurredOnUtc { get; init; }
    public DateTime AbandonedOnUtc { get; init; }
    public string? LastError { get; init; }
    public int RetryCount { get; init; }
}