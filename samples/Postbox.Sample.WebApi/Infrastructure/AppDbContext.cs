using Microsoft.EntityFrameworkCore;
using Postbox.Core;
using Postbox.Sample.WebApi.Domain;

namespace Postbox.Sample.WebApi.Infrastructure;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Order>(b =>
        {
            b.HasKey(o => o.Id);
            b.Property(o => o.CustomerEmail).IsRequired().HasMaxLength(320);
            b.Property(o => o.TotalAmount).HasPrecision(18, 2);
        });

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