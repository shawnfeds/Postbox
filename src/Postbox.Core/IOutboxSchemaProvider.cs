namespace Postbox.Core;

public interface IOutboxSchemaProvider
{
    string GetPendingMessagesSql();
    string GetMarkProcessedSql();
    string GetMarkFailedSql();
}