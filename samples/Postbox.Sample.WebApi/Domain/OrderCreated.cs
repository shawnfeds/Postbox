namespace Postbox.Sample.WebApi.Domain;

public class OrderCreated
{
    public Guid OrderId { get; init; }
    public string CustomerEmail { get; init; } = default!;
    public decimal TotalAmount { get; init; }
    public DateTime OccurredOnUtc { get; init; }
}