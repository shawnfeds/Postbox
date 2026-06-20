using Postbox.Core;

namespace Postbox.PostgreSQL;

public sealed class PostgreSqlSchemaProvider : IOutboxSchemaProvider
{
    public string GetCreateSchemaSql() => """
        CREATE SCHEMA IF NOT EXISTS postbox;
        CREATE TABLE IF NOT EXISTS postbox."OutboxMessages" (
            "Id"             UUID          NOT NULL PRIMARY KEY,
            "Type"           VARCHAR(500)  NOT NULL,
            "Payload"        TEXT          NOT NULL,
            "OccurredOnUtc"  TIMESTAMPTZ   NOT NULL,
            "ProcessedOnUtc" TIMESTAMPTZ   NULL,
            "Error"          TEXT          NULL,
            "RetryCount"     INT           NOT NULL DEFAULT 0
        );
        CREATE INDEX IF NOT EXISTS "IX_OutboxMessages_Unprocessed"
            ON postbox."OutboxMessages" ("ProcessedOnUtc")
            WHERE "ProcessedOnUtc" IS NULL;
        """;

    public string GetPendingMessagesSql() => """
        SELECT "Id", "Type", "Payload", "OccurredOnUtc", "RetryCount", "Error", "ProcessedOnUtc"
        FROM postbox."OutboxMessages"
        WHERE "ProcessedOnUtc" IS NULL
        ORDER BY "OccurredOnUtc"
        LIMIT 10
        FOR UPDATE SKIP LOCKED;
        """;

    public string GetMarkProcessedSql() => """
        UPDATE postbox."OutboxMessages"
        SET "ProcessedOnUtc" = NOW()
        WHERE "Id" = @p0
        """;

    public string GetMarkFailedSql() => """
        UPDATE postbox."OutboxMessages"
        SET "Error" = @p0,
            "RetryCount" = "RetryCount" + 1
        WHERE "Id" = @p1
        """;
}