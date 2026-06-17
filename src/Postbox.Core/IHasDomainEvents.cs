namespace Postbox.Core;

public interface IHasDomainEvents
{
    IReadOnlyList<object> DomainEvents { get; }
    void ClearDomainEvents();
}