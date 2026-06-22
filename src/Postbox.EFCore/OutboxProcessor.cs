using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Postbox.Core;

namespace Postbox.EFCore;

public sealed class OutboxProcessor(
    IServiceScopeFactory scopeFactory,
    IOutboxSchemaProvider schemaProvider,
    IOutboxTransport transport,
    ILogger<OutboxProcessor> logger,
    IOptions<OutboxOptions> options) : BackgroundService
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
                var processed = await ProcessOnceAsync(stoppingToken);
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

    public async Task<int> ProcessOnceAsync(CancellationToken cancellationToken)
    {
        await using var fetchScope = scopeFactory.CreateAsyncScope();
        var db = fetchScope.ServiceProvider.GetRequiredService<DbContext>();

        var messages = await db.Set<OutboxMessage>()
            .FromSqlRaw(schemaProvider.GetClaimMessagesSql(
                options.Value.BatchSize,
                options.Value.LockDurationSeconds))
            .ToListAsync(cancellationToken);

        if (messages.Count == 0)
            return 0;

        await Parallel.ForEachAsync(
            messages,
            new ParallelOptions
            {
                MaxDegreeOfParallelism = options.Value.MaxDegreeOfParallelism,
                CancellationToken = cancellationToken
            },
            async (message, ct) =>
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var msgDb = scope.ServiceProvider.GetRequiredService<DbContext>();

                try
                {
                    await transport.SendAsync(message, ct);
                    await msgDb.Database.ExecuteSqlRawAsync(
                        schemaProvider.GetMarkProcessedSql(),
                        message.Id);
                    logger.LogInformation(
                        "Outbox message {Id} of type {Type} processed successfully",
                        message.Id, message.Type);
                }
                catch (Exception ex)
                {
                    var nextRetryCount = message.RetryCount + 1;

                    if (nextRetryCount >= options.Value.MaxRetryCount)
                    {
                        await msgDb.Database.ExecuteSqlRawAsync(
                            schemaProvider.GetDeadLetterSql(),
                            ex.Message, message.Id);
                        logger.LogWarning(
                            "Outbox message {Id} of type {Type} moved to dead letter after {RetryCount} retries",
                            message.Id, message.Type, nextRetryCount);
                    }
                    else
                    {
                        await msgDb.Database.ExecuteSqlRawAsync(
                            schemaProvider.GetMarkFailedSql(),
                            ex.Message, message.Id);
                        logger.LogError(ex,
                            "Failed to process outbox message {Id} of type {Type}, retry {RetryCount} of {MaxRetryCount}",
                            message.Id, message.Type, nextRetryCount, options.Value.MaxRetryCount);
                    }
                }
            });

        return messages.Count;
    }

    internal async Task<int> ProcessOnceAsync(DbContext db, CancellationToken cancellationToken)
    {
        var messages = await db.Set<OutboxMessage>()
            .FromSqlRaw(schemaProvider.GetClaimMessagesSql(
                options.Value.BatchSize,
                options.Value.LockDurationSeconds))
            .ToListAsync(cancellationToken);

        if (messages.Count == 0)
            return 0;

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
                var nextRetryCount = message.RetryCount + 1;

                if (nextRetryCount >= options.Value.MaxRetryCount)
                {
                    await db.Database.ExecuteSqlRawAsync(
                        schemaProvider.GetDeadLetterSql(),
                        ex.Message, message.Id);
                    logger.LogWarning(
                        "Outbox message {Id} of type {Type} moved to dead letter after {RetryCount} retries",
                        message.Id, message.Type, nextRetryCount);
                }
                else
                {
                    await db.Database.ExecuteSqlRawAsync(
                        schemaProvider.GetMarkFailedSql(),
                        ex.Message, message.Id);
                    logger.LogError(ex,
                        "Failed to process outbox message {Id} of type {Type}, retry {RetryCount} of {MaxRetryCount}",
                        message.Id, message.Type, nextRetryCount, options.Value.MaxRetryCount);
                }
            }
        }

        return messages.Count;
    }
}