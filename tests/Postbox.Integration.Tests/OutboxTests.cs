using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
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
            NullLogger<OutboxProcessor>.Instance);

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
            NullLogger<OutboxProcessor>.Instance);

        await processor.ProcessOnceAsync(db, CancellationToken.None);

        // Use a FRESH DbContext to bypass EF's change tracker cache
        await using var freshDb = fixture.CreateDbContext();
        var message = await freshDb.Set<OutboxMessage>()
            .FirstAsync(m => m.ProcessedOnUtc == null);

        Assert.Equal(1, message.RetryCount);
        Assert.NotNull(message.Error);
    }
}