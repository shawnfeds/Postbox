using Postbox.Core;

namespace Postbox.Integration.Tests;

public class CapturingTransport : IOutboxTransport
{
    private readonly List<OutboxMessage> _messages = [];
    public IReadOnlyList<OutboxMessage> Messages => _messages.AsReadOnly();

    public Task SendAsync(OutboxMessage message, CancellationToken cancellationToken = default)
    {
        _messages.Add(message);
        return Task.CompletedTask;
    }
}