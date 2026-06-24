# Postbox

A transactional outbox implementation for EF Core that guarantees at-least-once message delivery by writing domain events to your database within the same transaction as your business data, then reliably dispatching them to your message broker via a background processor.

## The Problem

When a backend saves data to a database and publishes a message to a broker, these are two separate I/O operations that cannot share a transaction. A crash between them causes silent data loss.

```csharp
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
  ├── claims a batch of pending OutboxMessages
  ├── publishes to broker in parallel
  └── marks rows processed
```

## Delivery Guarantee

At-least-once. Duplicates are possible if the processor publishes successfully but crashes before marking the row processed. Consumers must be idempotent.

## Supported Providers

| Database   | Transport    | Status       |
|------------|--------------|--------------|
| PostgreSQL | RabbitMQ     | ✅ Supported |
| SQL Server | RabbitMQ     | ✅ Supported |

## Getting Started

### 1. Install packages

```bash
dotnet add package Postbox.EFCore
dotnet add package Postbox.PostgreSQL        # or Postbox.SqlServer
dotnet add package Postbox.Transport.RabbitMQ
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

### 3. Register `OutboxMessage` in your `DbContext`

```csharp
protected override void OnModelCreating(ModelBuilder modelBuilder)
{
    modelBuilder.Entity<OutboxMessage>(b =>
    {
        b.HasKey(o => o.Id);
        b.ToTable("OutboxMessages", "postbox");
    });

    modelBuilder.Entity<OutboxDeadLetter>(b =>
    {
        b.HasKey(o => o.Id);
        b.ToTable("OutboxDeadLetters", "postbox");
    });
}
```

### 4. Register Postbox in `Program.cs`

```csharp
builder.Services.AddPostbox();

builder.Services.AddDbContext<AppDbContext>((sp, options) =>
    options
        .UseNpgsql(connectionString)
        .AddInterceptors(sp.GetRequiredService<OutboxInterceptor>()));

builder.Services.AddScoped<DbContext>(sp => sp.GetRequiredService<AppDbContext>());
builder.Services.AddSingleton<IOutboxSchemaProvider, PostgreSqlSchemaProvider>();
builder.Services.AddRabbitMQTransport(hostName: "localhost");
```

### 5. Create the schema at startup

```csharp
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    var schema = scope.ServiceProvider.GetRequiredService<IOutboxSchemaProvider>();
    await db.Database.MigrateAsync();
    await db.Database.ExecuteSqlRawAsync(schema.GetCreateSchemaSql());
}
```

That's it. Every `SaveChangesAsync` call on an entity with domain events will automatically write to the outbox. The background processor handles the rest.

## Configuration

```csharp
builder.Services.AddOptions<OutboxOptions>()
    .BindConfiguration("Outbox");
```

```json
{
  "Outbox": {
    "MaxRetryCount": 5,
    "MaxPayloadBytes": 65536,
    "MaxDegreeOfParallelism": 4,
    "BatchSize": 10,
    "LockDurationSeconds": 30
  }
}
```

| Option | Default | Description |
|--------|---------|-------------|
| `MaxRetryCount` | 5 | Failed messages are retried up to this limit, then moved to `OutboxDeadLetters` |
| `MaxPayloadBytes` | 65536 (64KB) | Payload exceeding this limit throws before writing to the database |
| `MaxDegreeOfParallelism` | 4 | Number of messages dispatched in parallel per batch |
| `BatchSize` | 10 | Messages claimed per processor cycle |
| `LockDurationSeconds` | 30 | How long a claimed message is locked before another processor can steal it |

## How It Works

### The Interceptor

`OutboxInterceptor` hooks into EF Core's `SaveChangesAsync` pipeline. Before the transaction commits, it scans the change tracker for entities implementing `IHasDomainEvents`, serializes their events as JSON, and adds `OutboxMessage` rows to the same `DbContext`. No manual publish calls needed.

### The Processor

`OutboxProcessor` is an `IHostedService` that polls the `OutboxMessages` table. Each cycle atomically claims a batch of rows by setting `LockedUntil` via a single UPDATE statement — no long-held transactions. Multiple app instances can run concurrently without duplicating work. Messages are dispatched to the broker in parallel via `Parallel.ForEachAsync`.

### Retry and Dead Letter

Failed messages increment `RetryCount` and clear `LockedUntil` so they are retried on the next cycle. Once `RetryCount >= MaxRetryCount`, the message is moved atomically to `OutboxDeadLetters` and removed from `OutboxMessages`.

### Adaptive Polling

The processor backs off when the queue is empty (30s interval) and speeds up when messages are pending (2s interval), minimizing unnecessary database load.

## Schema

```sql
-- OutboxMessages
CREATE TABLE postbox."OutboxMessages" (
    "Id"             UUID         NOT NULL PRIMARY KEY,
    "Type"           VARCHAR(500) NOT NULL,
    "Payload"        TEXT         NOT NULL,
    "OccurredOnUtc"  TIMESTAMPTZ  NOT NULL,
    "ProcessedOnUtc" TIMESTAMPTZ  NULL,
    "Error"          TEXT         NULL,
    "RetryCount"     INT          NOT NULL DEFAULT 0,
    "LockedUntil"    TIMESTAMPTZ  NULL
);

-- OutboxDeadLetters
CREATE TABLE postbox."OutboxDeadLetters" (
    "Id"             UUID         NOT NULL PRIMARY KEY,
    "Type"           VARCHAR(500) NOT NULL,
    "Payload"        TEXT         NOT NULL,
    "OccurredOnUtc"  TIMESTAMPTZ  NOT NULL,
    "AbandonedOnUtc" TIMESTAMPTZ  NOT NULL,
    "LastError"      TEXT         NULL,
    "RetryCount"     INT          NOT NULL
);
```

## Observability

Postbox emits metrics via `System.Diagnostics.Metrics` (no extra dependencies). Subscribe with OpenTelemetry or `dotnet-counters`.

| Metric | Type | Description |
|--------|------|-------------|
| `postbox.messages.processed` | Counter | Successfully dispatched messages |
| `postbox.messages.failed` | Counter | Failed dispatches (will be retried) |
| `postbox.messages.deadlettered` | Counter | Messages moved to dead letter |

All metrics include a `message.type` dimension.

```bash
dotnet-counters monitor --counters Postbox.EFCore
```

## Benchmarks

Measured on .NET 10.0.9, Windows 11, Docker Desktop (WSL2), PostgreSQL 16 via Testcontainers.

| Benchmark | MessageCount | Mean | Allocated |
|-----------|-------------|------|-----------|
| `SaveChanges` without interceptor | — | 2.2 ms | 78 KB |
| `SaveChanges` with interceptor | — | 3.5 ms | 99 KB |
| Processor throughput | 100 | 89 ms (~1,100 msg/s) | 3.5 MB |
| Processor throughput | 1,000 | 880 ms (~1,135 msg/s) | 33 MB |

The interceptor adds approximately 1–2ms overhead per `SaveChangesAsync` call. Throughput is bounded by database round-trips — numbers reflect a local containerized database, not production hardware.

## Project Structure

```
src/
  Postbox.Core                  # Interfaces and domain types
  Postbox.EFCore                # Interceptor, processor, options
  Postbox.PostgreSQL            # PostgreSQL SQL provider
  Postbox.SqlServer             # SQL Server SQL provider
  Postbox.Transport.RabbitMQ    # RabbitMQ transport
samples/
  Postbox.Sample.WebApi         # Working example with Orders
tests/
  Postbox.Integration.Tests     # Integration tests via Testcontainers
benchmarks/
  Postbox.Benchmarks            # BenchmarkDotNet benchmarks
```

## License

MIT