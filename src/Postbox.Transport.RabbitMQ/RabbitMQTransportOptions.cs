namespace Postbox.Transport.RabbitMQ;

public sealed class RabbitMQTransportOptions
{
    public int PublishTimeoutSeconds { get; set; } = 10;
}