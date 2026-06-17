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
    SET "ProcessedOnUtc" = {0}
    WHERE "Id" = {1};
    """;

    public string GetMarkFailedSql() => """
    UPDATE "OutboxMessages"
    SET "Error" = {0},
        "RetryCount" = "RetryCount" + 1
    WHERE "Id" = {1};
    """;
}