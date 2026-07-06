namespace Services.WhatsApp;

/// <summary>
/// Configuração do cliente da Evolution API (seção "Evolution"). Em dev, BaseUrl/ApiKey vêm do
/// AppHost; em produção, do docker-compose (Evolution__BaseUrl / Evolution__ApiKey).
/// </summary>
public sealed class EvolutionOptions
{
    /// <summary>URL base da Evolution API (ex.: http://localhost:8080).</summary>
    public string BaseUrl { get; set; } = "";

    /// <summary>Segredo compartilhado enviado no header "apikey" (= AUTHENTICATION_API_KEY da Evolution).</summary>
    public string ApiKey { get; set; } = "";

    /// <summary>Nome da instância do WhatsApp já conectada na Evolution (pareada via QR code).</summary>
    public string Instance { get; set; } = "financeiro";

    /// <summary>
    /// Destinatário dos alertas. Contato: número com DDI+DDD (ex.: 5548999999999, sem "+" nem espaços).
    /// Grupo: o JID do grupo com sufixo "@g.us" (ex.: 120363039104311645@g.us). A conta pareada precisa
    /// ser membro do grupo.
    /// </summary>
    public string RecipientNumber { get; set; } = "120363039104311645@g.us";

    /// <summary>True quando BaseUrl e ApiKey estão preenchidos (evita disparar sem configuração).</summary>
    public bool IsConfigured => !string.IsNullOrWhiteSpace(BaseUrl) && !string.IsNullOrWhiteSpace(ApiKey);
}
