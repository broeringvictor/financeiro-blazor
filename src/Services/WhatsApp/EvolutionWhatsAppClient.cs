using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Services.WhatsApp;

/// <summary>
/// Cliente da Evolution API: envio de mensagens (outbound — alertas de vencimento) e download de mídia
/// (inbound — PDFs de boleto recebidos no grupo). A autenticação é o header "apikey" com o segredo
/// compartilhado; a rede interna (Aspire/Docker) garante que só o Services alcança a Evolution.
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

    /// <summary>
    /// Baixa os bytes da mídia (ex.: PDF de boleto) de uma mensagem recebida, via
    /// POST /chat/getBase64FromMediaMessage/{instance}. Retorna null se não configurada, sem mídia ou em erro.
    /// </summary>
    public async Task<byte[]?> BaixarMidiaBase64Async(string messageId, CancellationToken cancellationToken = default)
    {
        if (!options.IsConfigured || string.IsNullOrWhiteSpace(messageId))
        {
            return null;
        }

        using var request = new HttpRequestMessage(
            HttpMethod.Post, $"chat/getBase64FromMediaMessage/{options.Instance}")
        {
            Content = JsonContent.Create(new { message = new { key = new { id = messageId } }, convertToMp4 = false }),
        };
        request.Headers.Add("apikey", options.ApiKey);

        try
        {
            using var response = await http.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var erro = await response.Content.ReadAsStringAsync(cancellationToken);
                logger.LogError("Falha ao baixar mídia {MessageId} ({Status}): {Body}",
                    messageId, (int)response.StatusCode, erro);
                return null;
            }

            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("base64", out var b64) && b64.ValueKind == JsonValueKind.String)
            {
                return Convert.FromBase64String(b64.GetString()!);
            }

            logger.LogWarning("Resposta de mídia {MessageId} sem campo 'base64'.", messageId);
            return null;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Erro ao baixar mídia {MessageId} da Evolution.", messageId);
            return null;
        }
    }
}
