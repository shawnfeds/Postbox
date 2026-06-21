using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Internal;
using System.Reflection;

namespace Postbox.Sample.WebApi.Infrastructure;

public class PostgresMigrationsAssembly(
    ICurrentDbContext currentContext,
    IDbContextOptions options,
    IMigrationsIdGenerator idGenerator,
    IDiagnosticsLogger<DbLoggerCategory.Migrations> logger)
    : MigrationsAssembly(currentContext, options, idGenerator, logger)
{
    public override IReadOnlyDictionary<string, TypeInfo> Migrations =>
        base.Migrations
            .Where(m => m.Key.Contains("Postgres") ||
                        m.Value.Namespace?.Contains("PostgreSQL") == true)
            .ToDictionary(m => m.Key, m => m.Value)
            .AsReadOnly();
}