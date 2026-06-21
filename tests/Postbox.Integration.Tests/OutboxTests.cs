using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Postbox.Core;
using Postbox.EFCore;
using Postbox.PostgreSQL;
using Postbox.Sample.WebApi.Domain;

namespace Postbox.Integration.Tests;

[Collection("Postgres")]
public class OutboxTests(PostgresFixture fixture) : IClassFixture<PostgresFixture>
{
    private readonly IOutboxSchemaProvider _schema = new PostgreSqlSchemaProvider();

    [Fact]
    public async Task SaveChangesAsync_WithDomainEvent_WritesOutboxMessage()
    {
        await fixture.ResetAsync();
        // Arrange
        await using var db = fixture.CreateDbContext();

        // Act
        var order = Order.Create("test@example.com", 50m);
        db.Orders.Add(order);
        await db.SaveChangesAsync();

        // Assert
        var messages = await db.Set<OutboxMessage>()
            .Where(m => m.ProcessedOnUtc == null)
            .ToListAsync();

        Assert.Single(messages);
        Assert.Equal("Postbox.Sample.WebApi.Domain.OrderCreated", messages[0].Type);
        Assert.Null(messages[0].ProcessedOnUtc);
    }

    [Fact]
    public async Task Processor_WithPendingMessage_CallsTransportAndMarksProcessed()
    {
        // Arrange
        await fixture.ResetAsync();
        await using var db = fixture.CreateDbContext();
        var order = Order.Create("test2@example.com", 75m);
        db.Orders.Add(order);
        await db.SaveChangesAsync();

        var transport = new CapturingTransport();
        var processor = new OutboxProcessor(
            null!,
            _schema,
            transport,
            NullLogger<OutboxProcessor>.Instance,
            Options.Create(new OutboxOptions()));

        // Act
        await processor.ProcessOnceAsync(db, CancellationToken.None);

        // Assert
        Assert.Single(transport.Messages);

        var processed = await db.Set<OutboxMessage>()
            .Where(m => m.ProcessedOnUtc != null)
            .ToListAsync();

        Assert.Single(processed);
    }

    [Fact]
    public async Task Processor_WhenTransportFails_LeavesMessagePendingForRetry()
    {
        await fixture.ResetAsync();

        await using var db = fixture.CreateDbContext();
        var order = Order.Create("test3@example.com", 100m);
        db.Orders.Add(order);
        await db.SaveChangesAsync();

        var transport = new FailingTransport();
        var processor = new OutboxProcessor(
            null!,
            _schema,
            transport,
            NullLogger<OutboxProcessor>.Instance,
            Options.Create(new OutboxOptions()));

        await processor.ProcessOnceAsync(db, CancellationToken.None);

        // Use a FRESH DbContext to bypass EF's change tracker cache
        await using var freshDb = fixture.CreateDbContext();
        var message = await freshDb.Set<OutboxMessage>()
            .FirstAsync(m => m.ProcessedOnUtc == null);

        Assert.Equal(1, message.RetryCount);
        Assert.NotNull(message.Error);
    }

    [Fact]
    public async Task Processor_TwoConcurrentProcessors_EachMessageProcessedOnlyOnce()
    {
        await fixture.ResetAsync();

        // Arrange — write 20 orders
        await using var setupDb = fixture.CreateDbContext();
        for (int i = 0; i < 20; i++)
        {
            var order = Order.Create($"concurrent{i}@example.com", 10m * i);
            setupDb.Orders.Add(order);
        }
        await setupDb.SaveChangesAsync();

        var transport = new CapturingTransport();

        // Act — two processors running simultaneously
        var tasks = Enumerable.Range(0, 2).Select(async _ =>
        {
            await using var db = fixture.CreateDbContext();
            var processor = new OutboxProcessor(
                null!,
                _schema,
                transport,
                NullLogger<OutboxProcessor>.Instance,
                Options.Create(new OutboxOptions()));
            await processor.ProcessOnceAsync(db, CancellationToken.None);
        });

        await Task.WhenAll(tasks);

        // Assert — 20 messages total, none duplicated
        Assert.Equal(20, transport.Messages.Count);

        var ids = transport.Messages.Select(m => m.Id).ToList();
        Assert.Equal(ids.Count, ids.Distinct().Count());
    }

    [Fact]
    public async Task Processor_CrashMidBatch_MessagesRemainPendingForRetry()
    {
        await fixture.ResetAsync();

        await using var setupDb = fixture.CreateDbContext();
        for (int i = 0; i < 5; i++)
        {
            var order = Order.Create($"crash{i}@example.com", 10m);
            setupDb.Orders.Add(order);
        }
        await setupDb.SaveChangesAsync();

        // Transport fails on every message — simulates crash mid-flight
        var transport = new FailingTransport();
        await using var db = fixture.CreateDbContext();
        var processor = new OutboxProcessor(
            null!,
            _schema,
            transport,
            NullLogger<OutboxProcessor>.Instance,
            Options.Create(new OutboxOptions()));

        await processor.ProcessOnceAsync(db, CancellationToken.None);

        // All messages still pending, all have RetryCount = 1
        await using var freshDb = fixture.CreateDbContext();
        var messages = await freshDb.Set<OutboxMessage>()
            .Where(m => m.ProcessedOnUtc == null)
            .ToListAsync();

        Assert.Equal(5, messages.Count);
        Assert.All(messages, m => Assert.Equal(1, m.RetryCount));
        Assert.All(messages, m => Assert.NotNull(m.Error));
    }

    [Fact]
    public async Task Processor_LargeBacklog_DrainsCompletelyAcrossMultipleRuns()
    {
        await fixture.ResetAsync();

        // Arrange — 35 messages (processor batch size is 10, needs 4 runs)
        await using var setupDb = fixture.CreateDbContext();
        for (int i = 0; i < 35; i++)
        {
            var order = Order.Create($"backlog{i}@example.com", 10m);
            setupDb.Orders.Add(order);
        }
        await setupDb.SaveChangesAsync();

        var transport = new CapturingTransport();

        // Act — run processor until no messages remain
        int totalProcessed = 0;
        for (int run = 0; run < 10; run++) // cap at 10 runs to avoid infinite loop
        {
            await using var db = fixture.CreateDbContext();
            var processor = new OutboxProcessor(
                null!,
                _schema,
                transport,
                NullLogger<OutboxProcessor>.Instance,
                Options.Create(new OutboxOptions()));

            var processed = await processor.ProcessOnceAsync(db, CancellationToken.None);
            totalProcessed += processed;
            if (processed == 0) break;
        }

        // Assert
        Assert.Equal(35, totalProcessed);
        Assert.Equal(35, transport.Messages.Count);
    }

    [Fact]
    public async Task Processor_MessageExceedsMaxRetryCount_MovesToDeadLetter()
    {
        await fixture.ResetAsync();
        await using var setupDb = fixture.CreateDbContext();
        var order = Order.Create("deadletter@example.com", 50m);
        setupDb.Orders.Add(order);
        await setupDb.SaveChangesAsync();

        var transport = new FailingTransport();
        var opts = Options.Create(new OutboxOptions { MaxRetryCount = 3 });

        // run processor MaxRetryCount times to exhaust retries
        for (int i = 0; i < opts.Value.MaxRetryCount; i++)
        {
            await using var db = fixture.CreateDbContext();
            var processor = new OutboxProcessor(null!, _schema, transport, NullLogger<OutboxProcessor>.Instance, opts);
            await processor.ProcessOnceAsync(db, CancellationToken.None);
        }

        await using var freshDb = fixture.CreateDbContext();

        var deadLetters = await freshDb.Set<OutboxDeadLetter>().ToListAsync();
        Assert.Single(deadLetters);
        Assert.NotNull(deadLetters[0].LastError);
        Assert.Equal(3, deadLetters[0].RetryCount);

        var pending = await freshDb.Set<OutboxMessage>()
            .Where(m => m.ProcessedOnUtc == null)
            .ToListAsync();
        Assert.Empty(pending);
    }
}