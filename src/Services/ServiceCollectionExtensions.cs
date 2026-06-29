using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Services.Agents;
using Services.Gmail;
using Services.Pdf;

namespace Services;

/// <summary>
/// Registra o agente de IA (Anthropic + tools do Gmail) na DI. Compartilhado entre o worker
/// de background (Services) e o WebApp, que chama o agente no próprio processo.
/// </summary>
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddContaAgent(this IServiceCollection services, IConfiguration configuration)
    {
        var gmailOptions = configuration.GetSection("Gmail").Get<GmailOptions>() ?? new GmailOptions();

        var agenteOptions = configuration.GetSection("Agente").Get<AgenteOptions>() ?? new AgenteOptions();
        agenteOptions.ApiKey = configuration["Agente:ApiKey"]
                               ?? Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY")
                               ?? agenteOptions.ApiKey;

        var pdfOptions = configuration.GetSection("Pdf").Get<PdfOptions>() ?? new PdfOptions();

        services.AddSingleton(gmailOptions);
        services.AddSingleton(agenteOptions);
        services.AddSingleton(pdfOptions);
        services.AddSingleton<GmailServiceFactory>();
        services.AddSingleton<FaturaPdfExtractor>();
        services.AddSingleton<ContaAgentFactory>();
        services.AddSingleton<AgentAccessor>();

        return services;
    }
}
