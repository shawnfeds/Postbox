using Postbox.Core;

namespace Postbox.PostgreSQL;

public sealed class PostgreSqlSchemaProvider : IOutboxSchemaProvider
{
    public string GetPendingMessagesSql() => """
        SELECT "Id", "Type", "Payload", "OccurredOnUtc", "RetryCount", "Error", "ProcessedOnUtc"
        FROM "OutboxMessages"
        WHERE "ProcessedOnUtc" IS NULL
        ORDER BY "OccurredOnUtc"
        LIMIT 10
        FOR UPDATE SKIP LOCKED;
        """;

    public string GetMarkProcessedSql() => """
        UPDATE "OutboxMessages"
        SET "ProcessedOnUtc" = NOW()
        WHERE "Id" = @p0
        """;

    public string GetMarkFailedSql() => """
        UPDATE "OutboxMessages"
        SET "Error" = @p0,
            "RetryCount" = "RetryCount" + 1
        WHERE "Id" = @p1
        """;
}