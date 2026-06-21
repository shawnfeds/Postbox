using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Options;
using Postbox.Core;

namespace Postbox.EFCore;

public sealed class OutboxInterceptor(
    TimeProvider timeProvider,
    IOptions<OutboxOptions> options) : SaveChangesInterceptor
{
    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        var context = eventData.Context;
        if (context is null) return new ValueTask<InterceptionResult<int>>(result);

        var events = context.ChangeTracker
            .Entries<IHasDomainEvents>()
            .SelectMany(e => e.Entity.DomainEvents)
            .ToList();

        foreach (var domainEvent in events)
        {
            var payload = JsonSerializer.Serialize(domainEvent, domainEvent.GetType());
            var payloadBytes = Encoding.UTF8.GetByteCount(payload);

            if (payloadBytes > options.Value.MaxPayloadBytes)
                throw new InvalidOperationException(
                    $"Domain event '{domainEvent.GetType().FullName}' payload is {payloadBytes} bytes, " +
                    $"which exceeds the configured maximum of {options.Value.MaxPayloadBytes} bytes.");

            var message = new OutboxMessage
            {
                Id = Guid.NewGuid(),
                Type = domainEvent.GetType().FullName!,
                Payload = payload,
                OccurredOnUtc = timeProvider.GetUtcNow().UtcDateTime,
                RetryCount = 0
            };
            context.Set<OutboxMessage>().Add(message);
        }

        foreach (var entry in context.ChangeTracker.Entries<IHasDomainEvents>())
            entry.Entity.ClearDomainEvents();

        return new ValueTask<InterceptionResult<int>>(result);
    }
}