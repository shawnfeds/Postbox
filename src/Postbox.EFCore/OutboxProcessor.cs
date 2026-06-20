using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Postbox.Core;

namespace Postbox.EFCore;

public sealed class OutboxProcessor(
    IServiceScopeFactory scopeFactory,
    IOutboxSchemaProvider schemaProvider,
    IOutboxTransport transport,
    ILogger<OutboxProcessor> logger) : BackgroundService
{
    private static readonly TimeSpan BusyInterval = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan IdleInterval = TimeSpan.FromSeconds(30);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = BusyInterval;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<DbContext>();
                var processed = await ProcessOnceAsync(db, stoppingToken);
                interval = processed > 0 ? BusyInterval : IdleInterval;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Outbox processor encountered an error");
                interval = IdleInterval;
            }

            await Task.Delay(interval, stoppingToken);
        }
    }

    public async Task<int> ProcessOnceAsync(DbContext db, CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database
            .BeginTransactionAsync(cancellationToken);

        var messages = await db.Set<OutboxMessage>()
            .FromSqlRaw(schemaProvider.GetPendingMessagesSql())
            .ToListAsync(cancellationToken);

        if (messages.Count == 0)
        {
            await transaction.RollbackAsync(cancellationToken);
            return 0;
        }

        foreach (var message in messages)
        {
            try
            {
                await transport.SendAsync(message, cancellationToken);

                await db.Database.ExecuteSqlRawAsync(
                    schemaProvider.GetMarkProcessedSql(),
                    message.Id);

                logger.LogInformation(
                    "Outbox message {Id} of type {Type} processed successfully",
                    message.Id, message.Type);
            }
            catch (Exception ex)                
            {
                logger.LogError(ex,
                    "Failed to process outbox message {Id} of type {Type}",
                     message.Id, message.Type);

                await db.Database.ExecuteSqlRawAsync(
     schemaProvider.GetMarkFailedSql(),
     ex.Message, message.Id);
            }
        }

        await transaction.CommitAsync(cancellationToken);
        return messages.Count;
    }
}