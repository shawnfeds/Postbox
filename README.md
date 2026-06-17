# Postbox

A transactional outbox implementation for EF Core that guarantees at-least-once message delivery by writing domain events to your database within the same transaction as your business data, then reliably dispatching them to your message broker via a background processor.

## The Problem

When a backend saves data to a database and publishes a message to a broker, these are two separate I/O operations that cannot share a transaction. A crash between them causes silent data loss.

```
// Dangerous — two separate operations, no atomicity
await db.SaveChangesAsync();        // succeeds
await broker.PublishAsync(message); // crashes — message lost forever
```

## The Solution

Postbox writes messages into an `OutboxMessages` table inside the same database transaction as your business data. A background processor then reads pending messages and dispatches them to your broker. If the processor crashes, it retries — the row is still pending.

```
SaveChangesAsync()
  ├── writes Order row
  └── writes OutboxMessage row   ← same transaction, guaranteed atomic

BackgroundProcessor (every 2s)
  ├── reads pending OutboxMessages
  ├── publishes to broker
  └── marks row processed
```

## Delivery Guarantee

At-least-once. Duplicates are possible if the processor publishes successfully but crashes before marking the row processed. Consumers must be idempotent.

## Supported Providers

| Database   | Status |
|------------|--------|
| PostgreSQL | ✅ Supported |
| SQL Server | 🚧 Coming soon |

## Getting Started

### 1. Install packages

```bash
dotnet add package Postbox.EFCore
dotnet add package Postbox.PostgreSQL
dotnet add package Postbox.Transport.InMemory  # or RabbitMQ
```

### 2. Implement `IHasDomainEvents` on your entities

```csharp
public class Order : IHasDomainEvents
{
    private readonly List<object> _domainEvents = [];

    public IReadOnlyList<object> DomainEvents => _domainEvents.AsReadOnly();
    public void ClearDomainEvents() => _domainEvents.Clear();

    public static Order Create(string email, decimal amount)
    {
        var order = new Order { ... };
        order._domainEvents.Add(new OrderCreated { ... });
        return order;
    }
}
```

### 3. Add `OutboxMessages` to your `DbContext`

```csharp
public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<OutboxMessage>(b =>
        {
            b.HasKey(o => o.Id);
            b.Property(o => o.Type).IsRequired().HasMaxLength(500);
            b.Property(o => o.Payload).IsRequired();
            b.HasIndex(o => o.ProcessedOnUtc)
             .HasFilter("\"ProcessedOnUtc\" IS NULL");
        });
    }
}
```

### 4. Register Postbox in `Program.cs`

```csharp
builder.Services.AddSingleton<OutboxInterceptor>();

builder.Services.AddDbContext<AppDbContext>((sp, options) =>
    options
        .UseNpgsql(connectionString)
        .AddInterceptors(sp.GetRequiredService<OutboxInterceptor>()));

builder.Services.AddScoped<DbContext>(sp => sp.GetRequiredService<AppDbContext>());
builder.Services.AddSingleton<IOutboxSchemaProvider, PostgreSqlSchemaProvider>();
builder.Services.AddSingleton<IOutboxTransport, InMemoryTransport>();
builder.Services.AddHostedService<OutboxProcessor>();
```

### 5. Create the migration

```bash
dotnet ef migrations add AddOutbox
dotnet ef database update
```

That's it. Every `SaveChangesAsync` call on an entity with domain events will automatically write to the outbox. The background processor handles the rest.

## How It Works

### The Interceptor

`OutboxInterceptor` hooks into EF Core's `SaveChangesAsync` pipeline. Before the transaction commits, it scans the change tracker for entities implementing `IHasDomainEvents`, serializes their events as JSON, and adds `OutboxMessage` rows to the same `DbContext`. No manual publish calls needed.

### The Processor

`OutboxProcessor` is an `IHostedService` that polls the `OutboxMessages` table using `SELECT ... FOR UPDATE SKIP LOCKED`. This allows multiple app instances to process messages concurrently without duplicating work — each instance locks and skips rows already being processed by another instance.

### Adaptive Polling

The processor backs off when the queue is empty (30s interval) and speeds up when messages are pending (2s interval), minimizing unnecessary database load.

## Schema

```sql
CREATE TABLE "OutboxMessages" (
    "Id"             UUID          NOT NULL PRIMARY KEY,
    "Type"           TEXT          NOT NULL,
    "Payload"        TEXT          NOT NULL,
    "OccurredOnUtc"  TIMESTAMPTZ   NOT NULL,
    "ProcessedOnUtc" TIMESTAMPTZ   NULL,
    "Error"          TEXT          NULL,
    "RetryCount"     INT           NOT NULL DEFAULT 0
);

CREATE INDEX "IX_OutboxMessages_ProcessedOnUtc"
    ON "OutboxMessages" ("ProcessedOnUtc")
    WHERE "ProcessedOnUtc" IS NULL;
```

## Project Structure

```
src/
  Postbox.Core                  # Interfaces and domain types
  Postbox.EFCore                # Interceptor and background processor
  Postbox.PostgreSQL            # PostgreSQL-specific SQL provider
  Postbox.SqlServer             # SQL Server provider (coming soon)
  Postbox.Transport.InMemory    # Logs messages to console
  Postbox.Transport.RabbitMQ    # RabbitMQ transport (coming soon)
samples/
  Postbox.Sample.WebApi         # Working example with Orders
tests/
  Postbox.Integration.Tests     # Integration tests via Testcontainers
```

## Roadmap

- [x] PostgreSQL support
- [x] In-memory transport
- [x] Adaptive polling
- [x] Retry on failure
- [ ] RabbitMQ transport
- [ ] SQL Server support
- [ ] Dead letter queue
- [ ] Max retry limit with permanent failure handling
- [ ] OpenTelemetry traces

## License

MIT