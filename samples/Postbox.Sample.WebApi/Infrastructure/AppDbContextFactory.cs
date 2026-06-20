using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Postbox.Sample.WebApi.Infrastructure;

public class AppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var provider = args.FirstOrDefault(a => a.StartsWith("--DbProvider="))
            ?.Replace("--DbProvider=", "") ?? "Postgres";

        var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>();

        if (provider == "SqlServer")
            optionsBuilder.UseSqlServer(
                "Server=localhost,1433;Database=Postbox;User Id=sa;Password=Post@20261406;TrustServerCertificate=True");
        else
            optionsBuilder.UseNpgsql(
                "Host=localhost;Port=5432;Database=Postbox;Username=postgres;Password=Post@20261406");

        return new AppDbContext(optionsBuilder.Options);
    }
}