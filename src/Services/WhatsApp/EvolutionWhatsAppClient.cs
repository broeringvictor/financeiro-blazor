using System.Net.Http.Json;
using Microsoft.Extensions.Logging;

namespace Services.WhatsApp;

/// <summary>
/// Cliente da Evolution API para envio de mensagens de WhatsApp (só outbound — alertas de
/// vencimento). A autenticação é o header "apikey" com o segredo compartilhado; a rede interna
/// (Aspire/Docker) garante que só o Services alcança a Evolution.
/// </summary>
public sealed class EvolutionWhatsAppClient(
    HttpClient http,
    EvolutionOptions options,
    ILogger<EvolutionWhatsAppClient> logger)
{
    /// <summary>Envia uma mensagem de texto para <paramref name="number"/> (DDI+DDD, só dígitos).</summary>
    public async Task<bool> SendTextAsync(string number, string text, CancellationToken cancellationToken = default)
    {
        if (!options.IsConfigured)
        {
            logger.LogWarning("Envio de WhatsApp ignorado: Evolution não configurada (BaseUrl/ApiKey vazios).");
            return false;
        }

        if (string.IsNullOrWhiteSpace(number))
        {
            logger.LogWarning("Envio de WhatsApp ignorado: número do destinatário vazio.");
            return false;
        }

        // Evolution v2: POST /message/sendText/{instance} com header "apikey".
        using var request = new HttpRequestMessage(HttpMethod.Post, $"message/sendText/{options.Instance}")
        {
            Content = JsonContent.Create(new { number, text })
        };
        request.Headers.Add("apikey", options.ApiKey);

        try
        {
            using var response = await http.SendAsync(request, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                logger.LogInformation("WhatsApp enviado para {Number} via instância {Instance}.", number, options.Instance);
                return true;
            }

            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            logger.LogError("Falha ao enviar WhatsApp ({Status}): {Body}", (int)response.StatusCode, body);
            return false;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Erro de rede ao enviar WhatsApp para {Number}.", number);
            return false;
        }
    }

    /// <summary>Envia para o destinatário padrão configurado em <see cref="EvolutionOptions.RecipientNumber"/>.</summary>
    public Task<bool> SendAlertAsync(string text, CancellationToken cancellationToken = default) =>
        SendTextAsync(options.RecipientNumber, text, cancellationToken);
}
