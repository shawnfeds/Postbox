using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Postbox.Sample.WebApi.Migrations.PostgreSQL
{
    /// <inheritdoc />
    public partial class SyncSnapshot : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // No-op: snapshot sync migration.
            // InitialPostgres already created tables with correct Postgres types.
            // This migration exists only to satisfy EF's snapshot consistency check.
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
        }
    }
}
