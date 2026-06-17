namespace Postbox.Core;

public interface IOutboxTransport
{
    Task SendAsync(OutboxMessage message, CancellationToken cancellationToken = default);
}