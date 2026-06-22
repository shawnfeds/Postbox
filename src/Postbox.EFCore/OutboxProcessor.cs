using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Postbox.Core;
using Postbox.EFCore;
using System.Diagnostics.Metrics;

public sealed class OutboxProcessor : BackgroundService
{
    private static readonly TimeSpan BusyInterval = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan IdleInterval = TimeSpan.FromSeconds(30);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOutboxSchemaProvider _schemaProvider;
    private readonly IOutboxTransport _transport;
    private readonly ILogger<OutboxProcessor> _logger;
    private readonly IOptions<OutboxOptions> _options;
    private readonly Counter<long> _processedCounter;
    private readonly Counter<long> _failedCounter;
    private readonly Counter<long> _deadLetteredCounter;

    public OutboxProcessor(
        IServiceScopeFactory scopeFactory,
        IOutboxSchemaProvider schemaProvider,
        IOutboxTransport transport,
        ILogger<OutboxProcessor> logger,
        IOptions<OutboxOptions> options,
        IMeterFactory meterFactory)
    {
        _scopeFactory = scopeFactory;
        _schemaProvider = schemaProvider;
        _transport = transport;
        _logger = logger;
        _options = options;

        var meter = meterFactory.Create("Postbox.EFCore");
        _processedCounter = meter.CreateCounter<long>("postbox.messages.processed");
        _failedCounter = meter.CreateCounter<long>("postbox.messages.failed");
        _deadLetteredCounter = meter.CreateCounter<long>("postbox.messages.deadlettered");
    }

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
                _logger.LogError(ex, "Outbox processor encountered an error");
                interval = IdleInterval;
            }
            await Task.Delay(interval, stoppingToken);
        }
    }

    public async Task<int> ProcessOnceAsync(CancellationToken cancellationToken)
    {
        await using var fetchScope = _scopeFactory.CreateAsyncScope();
        var db = fetchScope.ServiceProvider.GetRequiredService<DbContext>();

        var messages = await db.Set<OutboxMessage>()
            .FromSqlRaw(_schemaProvider.GetClaimMessagesSql(
                _options.Value.BatchSize,
                _options.Value.LockDurationSeconds))
            .ToListAsync(cancellationToken);

        if (messages.Count == 0)
            return 0;

        await Parallel.ForEachAsync(
            messages,
            new ParallelOptions
            {
                MaxDegreeOfParallelism = _options.Value.MaxDegreeOfParallelism,
                CancellationToken = cancellationToken
            },
            async (message, ct) =>
            {
                await using var scope = _scopeFactory.CreateAsyncScope();
                var msgDb = scope.ServiceProvider.GetRequiredService<DbContext>();

                try
                {
                    await _transport.SendAsync(message, ct);
                    await msgDb.Database.ExecuteSqlRawAsync(
                        _schemaProvider.GetMarkProcessedSql(),
                        message.Id);
                    _processedCounter.Add(1, new KeyValuePair<string, object?>("message.type", message.Type));
                    _logger.LogInformation(
                        "Outbox message {Id} of type {Type} processed successfully",
                        message.Id, message.Type);
                }
                catch (Exception ex)
                {
                    var nextRetryCount = message.RetryCount + 1;

                    if (nextRetryCount >= _options.Value.MaxRetryCount)
                    {
                        await msgDb.Database.ExecuteSqlRawAsync(
                            _schemaProvider.GetDeadLetterSql(),
                            ex.Message, message.Id);
                        _deadLetteredCounter.Add(1, new KeyValuePair<string, object?>("message.type", message.Type));
                        _logger.LogWarning(
                            "Outbox message {Id} of type {Type} moved to dead letter after {RetryCount} retries",
                            message.Id, message.Type, nextRetryCount);
                    }
                    else
                    {
                        await msgDb.Database.ExecuteSqlRawAsync(
                            _schemaProvider.GetMarkFailedSql(),
                            ex.Message, message.Id);
                        _failedCounter.Add(1, new KeyValuePair<string, object?>("message.type", message.Type));
                        _logger.LogError(ex,
                            "Failed to process outbox message {Id} of type {Type}, retry {RetryCount} of {MaxRetryCount}",
                            message.Id, message.Type, nextRetryCount, _options.Value.MaxRetryCount);
                    }
                }
            });

        return messages.Count;
    }

    internal async Task<int> ProcessOnceAsync(DbContext db, CancellationToken cancellationToken)
    {
        var messages = await db.Set<OutboxMessage>()
            .FromSqlRaw(_schemaProvider.GetClaimMessagesSql(
                _options.Value.BatchSize,
                _options.Value.LockDurationSeconds))
            .ToListAsync(cancellationToken);

        if (messages.Count == 0)
            return 0;

        foreach (var message in messages)
        {
            try
            {
                await _transport.SendAsync(message, cancellationToken);
                await db.Database.ExecuteSqlRawAsync(
                    _schemaProvider.GetMarkProcessedSql(),
                    message.Id);
                _processedCounter.Add(1, new KeyValuePair<string, object?>("message.type", message.Type));
                _logger.LogInformation(
                    "Outbox message {Id} of type {Type} processed successfully",
                    message.Id, message.Type);
            }
            catch (Exception ex)
            {
                var nextRetryCount = message.RetryCount + 1;

                if (nextRetryCount >= _options.Value.MaxRetryCount)
                {
                    await db.Database.ExecuteSqlRawAsync(
                        _schemaProvider.GetDeadLetterSql(),
                        ex.Message, message.Id);
                    _deadLetteredCounter.Add(1, new KeyValuePair<string, object?>("message.type", message.Type));
                    _logger.LogWarning(
                        "Outbox message {Id} of type {Type} moved to dead letter after {RetryCount} retries",
                        message.Id, message.Type, nextRetryCount);
                }
                else
                {
                    await db.Database.ExecuteSqlRawAsync(
                        _schemaProvider.GetMarkFailedSql(),
                        ex.Message, message.Id);
                    _failedCounter.Add(1, new KeyValuePair<string, object?>("message.type", message.Type));
                    _logger.LogError(ex,
                        "Failed to process outbox message {Id} of type {Type}, retry {RetryCount} of {MaxRetryCount}",
                        message.Id, message.Type, nextRetryCount, _options.Value.MaxRetryCount);
                }
            }
        }

        return messages.Count;
    }
}