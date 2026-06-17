using Postbox.Core;

namespace Postbox.Integration.Tests;

public class FailingTransport : IOutboxTransport
{
    public Task SendAsync(OutboxMessage message, CancellationToken cancellationToken = default)
    {
        throw new InvalidOperationException("Transport failure simulated");
    }
}