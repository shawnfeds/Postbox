using Microsoft.EntityFrameworkCore;
using Postbox.Core;
using Postbox.EFCore;
using Postbox.PostgreSQL;
using Postbox.Sample.WebApi.Domain;
using Postbox.Sample.WebApi.Infrastructure;
using Postbox.Transport.InMemory;
using Postbox.Transport.RabbitMQ;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

builder.Services.AddSingleton<OutboxInterceptor>();

builder.Services.AddDbContext<AppDbContext>((sp, options) =>
    options
        .UseNpgsql(builder.Configuration.GetConnectionString("Postbox"))
        .AddInterceptors(sp.GetRequiredService<OutboxInterceptor>()));

builder.Services.AddScoped<DbContext>(sp => sp.GetRequiredService<AppDbContext>());

builder.Services.AddSingleton<IOutboxSchemaProvider, PostgreSqlSchemaProvider>();
builder.Services.AddRabbitMQTransport(
    hostName: "127.0.0.1",
    port: 5672);
builder.Services.AddHostedService<OutboxProcessor>();

var app = builder.Build();

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
