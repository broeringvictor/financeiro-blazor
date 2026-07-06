using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Services.WhatsApp;

/// <summary>Registra o cliente da Evolution API (envio de WhatsApp) na DI.</summary>
public static class WhatsAppServiceCollectionExtensions
{
    public static IServiceCollection AddEvolutionWhatsApp(this IServiceCollection services, IConfiguration configuration)
    {
        var options = configuration.GetSection("Evolution").Get<EvolutionOptions>() ?? new EvolutionOptions();
        services.AddSingleton(options);

        // BaseUrl precisa terminar com "/" pra o path relativo (message/sendText/...) resolver certo.
        var baseUrl = options.BaseUrl.EndsWith('/') ? options.BaseUrl : options.BaseUrl + "/";

        services.AddHttpClient<EvolutionWhatsAppClient>(client =>
        {
            if (!string.IsNullOrWhiteSpace(baseUrl))
            {
                client.BaseAddress = new Uri(baseUrl);
            }
        });

        return services;
    }
}
