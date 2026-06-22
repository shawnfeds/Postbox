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
                [RetryCount]     INT              NOT NULL DEFAULT 0,
                [LockedUntil]    DATETIME2        NULL
            );
            CREATE INDEX [IX_OutboxMessages_Unprocessed]
                ON [postbox].[OutboxMessages] ([ProcessedOnUtc])
                WHERE [ProcessedOnUtc] IS NULL;
        END

        IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'OutboxDeadLetters' AND schema_id = SCHEMA_ID('postbox'))
        BEGIN
            CREATE TABLE [postbox].[OutboxDeadLetters] (
                [Id]             UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
                [Type]           NVARCHAR(500)    NOT NULL,
                [Payload]        NVARCHAR(MAX)    NOT NULL,
                [OccurredOnUtc]  DATETIME2        NOT NULL,
                [AbandonedOnUtc] DATETIME2        NOT NULL,
                [LastError]      NVARCHAR(MAX)    NULL,
                [RetryCount]     INT              NOT NULL
            );
        END
        """;

    public string GetClaimMessagesSql(int batchSize, int lockDurationSeconds) => $"""
        UPDATE TOP ({batchSize}) m
        SET m.[LockedUntil] = DATEADD(second, {lockDurationSeconds}, GETUTCDATE())
        OUTPUT inserted.[Id], inserted.[Type], inserted.[Payload],
               inserted.[OccurredOnUtc], inserted.[ProcessedOnUtc],
               inserted.[Error], inserted.[RetryCount], inserted.[LockedUntil]
        FROM [postbox].[OutboxMessages] m WITH (UPDLOCK, READPAST)
        WHERE m.[ProcessedOnUtc] IS NULL
        AND (m.[LockedUntil] IS NULL OR m.[LockedUntil] < GETUTCDATE())
        """;

    public string GetMarkProcessedSql() => """
        UPDATE [postbox].[OutboxMessages]
        SET [ProcessedOnUtc] = GETUTCDATE(),
            [LockedUntil] = NULL
        WHERE [Id] = @p0
        """;

    public string GetMarkFailedSql() => """
        UPDATE [postbox].[OutboxMessages]
        SET [Error] = @p0,
            [RetryCount] = [RetryCount] + 1,
            [LockedUntil] = NULL
        WHERE [Id] = @p1
        """;

    public string GetDeadLetterSql() => """
        INSERT INTO [postbox].[OutboxDeadLetters]
            ([Id], [Type], [Payload], [OccurredOnUtc], [AbandonedOnUtc], [LastError], [RetryCount])
        SELECT [Id], [Type], [Payload], [OccurredOnUtc], GETUTCDATE(), @p0, [RetryCount] + 1
        FROM [postbox].[OutboxMessages]
        WHERE [Id] = @p1;
        DELETE FROM [postbox].[OutboxMessages] WHERE [Id] = @p1;
        """;
}