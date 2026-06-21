using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore.Migrations;

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
                "Server=localhost,1433;Database=PostBox;User Id=sa;Password=Post@20261406;TrustServerCertificate=True",
                b => b.MigrationsAssembly("Postbox.Sample.WebApi")
              .MigrationsHistoryTable("__EFMigrationsHistory", "postbox"))
        .ReplaceService<IMigrationsAssembly, SqlServerMigrationsAssembly>();
        else
            optionsBuilder.UseNpgsql(
                "Host=localhost;Port=5432;Database=PostBox;Username=postgres;Password=Post@20261406",
                b => b.MigrationsAssembly("Postbox.Sample.WebApi")
              .MigrationsHistoryTable("__EFMigrationsHistory", "postbox"))
        .ReplaceService<IMigrationsAssembly, PostgresMigrationsAssembly>();

        return new AppDbContext(optionsBuilder.Options);
    }
}