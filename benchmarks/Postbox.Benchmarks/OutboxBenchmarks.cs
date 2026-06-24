using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Postbox.Core;
using Postbox.EFCore;
using Postbox.PostgreSQL;
using Postbox.Sample.WebApi.Domain;
using Postbox.Sample.WebApi.Infrastructure;
using Testcontainers.PostgreSql;

namespace Postbox.Benchmarks;

[MemoryDiagnoser]
public class OutboxBenchmarks
{
    private PostgreSqlContainer _container = null!;
    private string _connectionString = null!;
    private PostgreSqlSchemaProvider _schema = null!;
    private IOptions<OutboxOptions> _options = null!;

    [GlobalSetup]
    public async Task GlobalSetup()
    {
        _container = new PostgreSqlBuilder()
            .WithImage("postgres:16")
            .WithDatabase("postbox_benchmarks")
            .WithUsername("postgres")
            .WithPassword("bench")
            .Build();

        await _container.StartAsync();
        _connectionString = _container.GetConnectionString();
        _schema = new PostgreSqlSchemaProvider();
        _options = Options.Create(new OutboxOptions());

        var db = CreateDbContext();
        await db.Database.MigrateAsync();
        await db.Database.ExecuteSqlRawAsync(_schema.GetCreateSchemaSql());
    }

    [GlobalCleanup]
    public async Task GlobalCleanup()
    {
        await _container.StopAsync();
    }

    [IterationSetup]
    public void IterationSetup()
    {
        var db = CreateDbContext();
        db.Database.ExecuteSqlRaw(
            """
            DELETE FROM postbox."OutboxMessages";
            DELETE FROM postbox."OutboxDeadLetters";
            DELETE FROM postbox."Orders";
            """);
    }

    // -------------------------------------------------------------------------
    // Benchmark 1: Interceptor overhead
    // -------------------------------------------------------------------------

    [Benchmark(Baseline = true)]
    public async Task SaveChanges_WithoutInterceptor()
    {
        var db = CreateDbContext(withInterceptor: false);
        var order = Order.Create("bench@example.com", 0m);
        db.Orders.Add(order);
        await db.SaveChangesAsync();
    }

    [Benchmark]
    public async Task SaveChanges_WithInterceptor()
    {
        var db = CreateDbContext(withInterceptor: true);
        var order = Order.Create("bench@example.com", 99.99m);
        db.Orders.Add(order);
        await db.SaveChangesAsync();
    }

    // -------------------------------------------------------------------------
    // Benchmark 2: Processor throughput
    // -------------------------------------------------------------------------

    [Params(100, 1000)]
    public int MessageCount { get; set; }

    [Benchmark]
    public async Task Processor_Throughput()
    {
        var setupDb = CreateDbContext(withInterceptor: true);
        for (int i = 0; i < MessageCount; i++)
        {
            var order = Order.Create($"bench{i}@example.com", 10m);
            setupDb.Orders.Add(order);
        }
        await setupDb.SaveChangesAsync();

        var transport = new NullTransport();
        var processor = new OutboxProcessor(
            null!,
            _schema,
            transport,
            NullLogger<OutboxProcessor>.Instance,
            _options,
            new NullMeterFactory());

        int processed;
        do
        {
            var db = CreateDbContext();
            processed = await processor.ProcessOnceAsync(db, CancellationToken.None);
        } while (processed > 0);
    }

    private AppDbContext CreateDbContext(bool withInterceptor = false)
    {
        var builder = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(_connectionString,
                b => b.MigrationsAssembly("Postbox.Sample.WebApi")
                      .MigrationsHistoryTable("__EFMigrationsHistory", "postbox"))
            .ReplaceService<IMigrationsAssembly, PostgresMigrationsAssembly>();

        if (withInterceptor)
            builder.AddInterceptors(
                new OutboxInterceptor(TimeProvider.System, _options));

        return new AppDbContext(builder.Options);
    }
}