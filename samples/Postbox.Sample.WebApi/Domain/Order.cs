using Postbox.Core;

namespace Postbox.Sample.WebApi.Domain;

public class Order : IHasDomainEvents
{
    private readonly List<object> _domainEvents = [];

    public Guid Id { get; private set; }
    public string CustomerEmail { get; private set; } = default!;
    public decimal TotalAmount { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public OrderStatus Status { get; private set; }

    public IReadOnlyList<object> DomainEvents => _domainEvents.AsReadOnly();
    public void ClearDomainEvents() => _domainEvents.Clear();

    private Order() { }

    public static Order Create(string customerEmail, decimal totalAmount)
    {
        var order = new Order
        {
            Id = Guid.NewGuid(),
            CustomerEmail = customerEmail,
            TotalAmount = totalAmount,
            CreatedAtUtc = DateTime.UtcNow,
            Status = OrderStatus.Pending
        };

        order._domainEvents.Add(new OrderCreated
        {
            OrderId = order.Id,
            CustomerEmail = order.CustomerEmail,
            TotalAmount = order.TotalAmount,
            OccurredOnUtc = order.CreatedAtUtc
        });

        return order;
    }
}

public enum OrderStatus { Pending, Confirmed, Cancelled }