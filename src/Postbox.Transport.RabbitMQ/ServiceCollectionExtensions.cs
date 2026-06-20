using Microsoft.Extensions.DependencyInjection;
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
            return RabbitMQTransport.CreateAsync(connection).GetAwaiter().GetResult();
        });

        return services;
    }
}