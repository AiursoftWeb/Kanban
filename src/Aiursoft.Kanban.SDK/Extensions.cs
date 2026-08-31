using Aiursoft.AiurProtocol;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Aiursoft.Kanban.SDK;

public static class Extensions
{
    public static IServiceCollection AddKanbanSdk(this IServiceCollection services, string endpoint)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(endpoint);
        services.AddAiurProtocolClient();
        services.Configure<KanbanApiOptions>(options => options.Endpoint = endpoint);
        services.TryAddScoped<IKanbanAccessTokenProvider, EmptyKanbanAccessTokenProvider>();
        services.AddScoped<KanbanApiClient>();
        return services;
    }
}
