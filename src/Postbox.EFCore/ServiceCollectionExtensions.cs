using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Postbox.EFCore;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddPostbox(this IServiceCollection services)
    {
        services.AddMetrics();
        services.AddOptions<OutboxOptions>();
        services.AddSingleton<OutboxInterceptor>(sp =>
            new OutboxInterceptor(
                TimeProvider.System,
                sp.GetRequiredService<IOptions<OutboxOptions>>()));
        services.AddHostedService<OutboxProcessor>();
        return services;
    }
}