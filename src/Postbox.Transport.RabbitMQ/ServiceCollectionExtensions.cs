using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using Postbox.Core;

namespace Postbox.Transport.RabbitMQ;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddRabbitMQTransport(
        this IServiceCollection services,
        string hostName,
        int port = 5672,
        string userName = "guest",
        string password = "guest")
    {
        services.AddOptions<RabbitMQTransportOptions>();

        services.AddSingleton<IConnection>(sp =>
        {
            var factory = new ConnectionFactory
            {
                HostName = hostName,
                Port = port,
                UserName = userName,
                Password = password
            };
            return factory.CreateConnectionAsync().GetAwaiter().GetResult();
        });

        services.AddSingleton<IOutboxTransport>(sp =>
        {
            var connection = sp.GetRequiredService<IConnection>();
            var options = sp.GetRequiredService<IOptions<RabbitMQTransportOptions>>();
            return RabbitMQTransport.CreateAsync(connection, options).GetAwaiter().GetResult();
        });

        return services;
    }
}