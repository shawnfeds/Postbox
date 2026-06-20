using Postbox.Core;
using RabbitMQ.Client;
using System.Text;
using System.Threading.Channels;

namespace Postbox.Transport.RabbitMQ;

public sealed class RabbitMQTransport : IOutboxTransport, IAsyncDisposable
{
    private readonly IChannel _channel;
    private const string ExchangeName = "outbox";

    private RabbitMQTransport(IChannel channel) => _channel = channel;

    public static async Task<RabbitMQTransport> CreateAsync(IConnection connection)
    {
        var channel = await connection.CreateChannelAsync();
        await channel.ExchangeDeclareAsync(
            exchange: ExchangeName,
            type: ExchangeType.Topic,
            durable: true,
            autoDelete: false);
        return new RabbitMQTransport(channel);
    }

    public async Task SendAsync(OutboxMessage message, CancellationToken cancellationToken = default)
    {
        var body = Encoding.UTF8.GetBytes(message.Payload);
        var props = new BasicProperties
        {
            Persistent = true,
            MessageId = message.Id.ToString(),
            Type = message.Type,
            Timestamp = new AmqpTimestamp(
                new DateTimeOffset(message.OccurredOnUtc).ToUnixTimeSeconds())
        };

        await _channel.BasicPublishAsync(
            exchange: ExchangeName,
            routingKey: message.Type,
            mandatory: false,
            basicProperties: props,
            body: body,
            cancellationToken: cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        await _channel.CloseAsync();
        _channel.Dispose();
    }
}