using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;
using Postbox.EFCore;
using Postbox.Sample.WebApi.Infrastructure;
using Testcontainers.PostgreSql;

namespace Postbox.Integration.Tests;

public class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:16")
        .WithDatabase("postbox_tests")
        .WithUsername("postgres")
        .WithPassword("outbox")
        .Build();

    public AppDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(_container.GetConnectionString(),
                b => b.MigrationsAssembly("Postbox.Sample.WebApi")
                      .MigrationsHistoryTable("__EFMigrationsHistory", "postbox"))
            .ReplaceService<IMigrationsAssembly, PostgresMigrationsAssembly>()
            .AddInterceptors(new OutboxInterceptor())
            .Options;

        return new AppDbContext(options);
    }

    public string ConnectionString => _container.GetConnectionString();

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        await using var db = CreateDbContext();
        await db.Database.MigrateAsync();
    }

    public async Task ResetAsync()
    {
        await using var db = CreateDbContext();
        await db.Database.ExecuteSqlRawAsync(
            """
        DELETE FROM postbox."OutboxMessages";
        DELETE FROM postbox."Orders";
        """);
    }

    public async Task DisposeAsync()
    {
        await _container.StopAsync();
    }
}