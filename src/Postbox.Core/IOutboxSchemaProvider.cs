namespace Postbox.Core;

public interface IOutboxSchemaProvider
{
    string GetClaimMessagesSql(int batchSize, int lockDurationSeconds);
    string GetMarkProcessedSql();
    string GetMarkFailedSql();
    string GetCreateSchemaSql();
    string GetDeadLetterSql();
}