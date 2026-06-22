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
            "RetryCount"     INT           NOT NULL DEFAULT 0,
            "LockedUntil"    TIMESTAMPTZ   NULL
        );
        CREATE INDEX IF NOT EXISTS "IX_OutboxMessages_Unprocessed"
            ON postbox."OutboxMessages" ("ProcessedOnUtc")
            WHERE "ProcessedOnUtc" IS NULL;
        CREATE TABLE IF NOT EXISTS postbox."OutboxDeadLetters" (
            "Id"             UUID          NOT NULL PRIMARY KEY,
            "Type"           VARCHAR(500)  NOT NULL,
            "Payload"        TEXT          NOT NULL,
            "OccurredOnUtc"  TIMESTAMPTZ   NOT NULL,
            "AbandonedOnUtc" TIMESTAMPTZ   NOT NULL,
            "LastError"      TEXT          NULL,
            "RetryCount"     INT           NOT NULL
        );
        """;

    public string GetClaimMessagesSql(int batchSize, int lockDurationSeconds) => $"""
        UPDATE postbox."OutboxMessages"
        SET "LockedUntil" = NOW() + interval '{lockDurationSeconds} seconds'
        WHERE "Id" IN (
            SELECT "Id" FROM postbox."OutboxMessages"
            WHERE "ProcessedOnUtc" IS NULL
            AND ("LockedUntil" IS NULL OR "LockedUntil" < NOW())
            ORDER BY "OccurredOnUtc"
            LIMIT {batchSize}
            FOR UPDATE SKIP LOCKED
        )
        RETURNING "Id", "Type", "Payload", "OccurredOnUtc", "ProcessedOnUtc", "Error", "RetryCount", "LockedUntil";
        """;

    public string GetMarkProcessedSql() => """
        UPDATE postbox."OutboxMessages"
        SET "ProcessedOnUtc" = NOW(),
            "LockedUntil" = NULL
        WHERE "Id" = @p0
        """;

    public string GetMarkFailedSql() => """
        UPDATE postbox."OutboxMessages"
        SET "Error" = @p0,
            "RetryCount" = "RetryCount" + 1,
            "LockedUntil" = NULL
        WHERE "Id" = @p1
        """;

    public string GetDeadLetterSql() => """
        INSERT INTO postbox."OutboxDeadLetters"
            ("Id", "Type", "Payload", "OccurredOnUtc", "AbandonedOnUtc", "LastError", "RetryCount")
        SELECT "Id", "Type", "Payload", "OccurredOnUtc", NOW(), @p0, "RetryCount" + 1
        FROM postbox."OutboxMessages"
        WHERE "Id" = @p1;
        DELETE FROM postbox."OutboxMessages" WHERE "Id" = @p1;
        """;
}