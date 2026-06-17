using Microsoft.Extensions.Logging;
using Postbox.Core;

namespace Postbox.Transport.InMemory;

public sealed class InMemoryTransport(ILogger<InMemoryTransport> logger) : IOutboxTransport
{
    public Task SendAsync(OutboxMessage message, CancellationToken cancellationToken = default)
    {
        logger.LogInformation(
            "[InMemory Transport] Message: Id={Id} Type={Type} Payload={Payload}",
            message.Id, message.Type, message.Payload);

        return Task.CompletedTask;
    }
}