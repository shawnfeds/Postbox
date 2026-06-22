using System.Text;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using Postbox.Core;

namespace Postbox.Transport.RabbitMQ;

public sealed class RabbitMQTransport : IOutboxTransport, IAsyncDisposable
{
    private readonly IChannel _channel;
    private readonly IOptions<RabbitMQTransportOptions> _options;
    private const string ExchangeName = "outbox";

    private RabbitMQTransport(IChannel channel, IOptions<RabbitMQTransportOptions> options)
    {
        _channel = channel;
        _options = options;
    }

    public static async Task<RabbitMQTransport> CreateAsync(
        IConnection connection,
        IOptions<RabbitMQTransportOptions> options)
    {
        var channel = await connection.CreateChannelAsync();
        await channel.ExchangeDeclareAsync(
            exchange: ExchangeName,
            type: ExchangeType.Topic,
            durable: true,
            autoDelete: false);
        return new RabbitMQTransport(channel, options);
    }

    public async Task SendAsync(OutboxMessage message, CancellationToken cancellationToken = default)
    {
        var timeout = TimeSpan.FromSeconds(_options.Value.PublishTimeoutSeconds);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(timeout);

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
            cancellationToken: cts.Token);
    }

    public async ValueTask DisposeAsync()
    {
        await _channel.CloseAsync();
        _channel.Dispose();
    }
}