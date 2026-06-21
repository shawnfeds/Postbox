using Postbox.Core;

namespace Postbox.Integration.Tests;

public sealed class CapturingTransport : IOutboxTransport
{
    private readonly List<OutboxMessage> _messages = new();
    private readonly Lock _lock = new();

    public IReadOnlyList<OutboxMessage> Messages
    {
        get { lock (_lock) { return _messages.ToList(); } }
    }

    public Task SendAsync(OutboxMessage message, CancellationToken cancellationToken = default)
    {
        lock (_lock) { _messages.Add(message); }
        return Task.CompletedTask;
    }
}