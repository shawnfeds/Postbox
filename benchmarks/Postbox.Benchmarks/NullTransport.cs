using Postbox.Core;

namespace Postbox.Benchmarks;

public sealed class NullTransport : IOutboxTransport
{
    public Task SendAsync(OutboxMessage message, CancellationToken cancellationToken = default)
        => Task.CompletedTask;
}