using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Options;
using Postbox.Core;
using Postbox.EFCore;
using Postbox.PostgreSQL;
using Postbox.Sample.WebApi.Domain;
using Postbox.Sample.WebApi.Infrastructure;
using Postbox.SqlServer;
using Postbox.Transport.RabbitMQ;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();
builder.Services.AddPostbox();

var dbProvider = builder.Configuration["DbProvider"]; // "Postgres" or "SqlServer"

if (dbProvider == "SqlServer")
{
    builder.Services.AddDbContext<AppDbContext>((sp, options) =>
        options
            .UseSqlServer(builder.Configuration.GetConnectionString("PostboxSqlServer"),
                b => b.MigrationsAssembly("Postbox.Sample.WebApi")
                      .MigrationsHistoryTable("__EFMigrationsHistory", "postbox"))
            .ReplaceService<IMigrationsAssembly, SqlServerMigrationsAssembly>()
            .AddInterceptors(sp.GetRequiredService<OutboxInterceptor>()));
    builder.Services.AddSingleton<IOutboxSchemaProvider, SqlServerSchemaProvider>();
}
else
{
    builder.Services.AddDbContext<AppDbContext>((sp, options) =>
        options
            .UseNpgsql(builder.Configuration.GetConnectionString("Postbox"),
                b => b.MigrationsAssembly("Postbox.Sample.WebApi")
                      .MigrationsHistoryTable("__EFMigrationsHistory", "postbox"))
            .ReplaceService<IMigrationsAssembly, PostgresMigrationsAssembly>()
            .AddInterceptors(sp.GetRequiredService<OutboxInterceptor>()));
    builder.Services.AddSingleton<IOutboxSchemaProvider, PostgreSqlSchemaProvider>();
}

builder.Services.AddScoped<DbContext>(sp => sp.GetRequiredService<AppDbContext>());
builder.Services.AddRabbitMQTransport(
    hostName: "127.0.0.1",
    port: 6572);

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    var schema = scope.ServiceProvider.GetRequiredService<IOutboxSchemaProvider>();
    await db.Database.ExecuteSqlRawAsync(schema.GetCreateSchemaSql());
}

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.MapPost("/orders", async (AppDbContext db) =>
{
    var order = Order.Create("customer@example.com", 99.99m);
    db.Orders.Add(order);
    await db.SaveChangesAsync();
    return Results.Ok(new { order.Id });
});

app.Run();
