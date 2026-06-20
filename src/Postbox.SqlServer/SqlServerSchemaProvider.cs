using Postbox.Core;

namespace Postbox.SqlServer;

public sealed class SqlServerSchemaProvider : IOutboxSchemaProvider
{
    public string GetCreateSchemaSql() => """
        IF NOT EXISTS (SELECT 1 FROM sys.schemas WHERE name = 'postbox')
            EXEC('CREATE SCHEMA [postbox]');

        IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'OutboxMessages' AND schema_id = SCHEMA_ID('postbox'))
        BEGIN
            CREATE TABLE [postbox].[OutboxMessages] (
                [Id]             UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
                [Type]           NVARCHAR(500)    NOT NULL,
                [Payload]        NVARCHAR(MAX)    NOT NULL,
                [OccurredOnUtc]  DATETIME2        NOT NULL,
                [ProcessedOnUtc] DATETIME2        NULL,
                [Error]          NVARCHAR(MAX)    NULL,
                [RetryCount]     INT              NOT NULL DEFAULT 0
            );
            CREATE INDEX [IX_OutboxMessages_Unprocessed]
                ON [postbox].[OutboxMessages] ([ProcessedOnUtc])
                WHERE [ProcessedOnUtc] IS NULL;
        END
        """;

    public string GetPendingMessagesSql() => """
        SELECT TOP 10 [Id], [Type], [Payload], [OccurredOnUtc], [RetryCount], [Error], [ProcessedOnUtc]
        FROM [postbox].[OutboxMessages] WITH (UPDLOCK, READPAST)
        WHERE [ProcessedOnUtc] IS NULL
        ORDER BY [OccurredOnUtc]
        """;

    public string GetMarkProcessedSql() => """
        UPDATE [postbox].[OutboxMessages]
        SET [ProcessedOnUtc] = GETUTCDATE()
        WHERE [Id] = @p0
        """;

    public string GetMarkFailedSql() => """
        UPDATE [postbox].[OutboxMessages]
        SET [Error] = @p0,
            [RetryCount] = [RetryCount] + 1
        WHERE [Id] = @p1
        """;
}