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

    /// <summary>Número que recebe os alertas, com DDI+DDD (ex.: 5548999999999). Sem "+" nem espaços.</summary>
    public string RecipientNumber { get; set; } = "5548991668808";

    /// <summary>True quando BaseUrl e ApiKey estão preenchidos (evita disparar sem configuração).</summary>
    public bool IsConfigured => !string.IsNullOrWhiteSpace(BaseUrl) && !string.IsNullOrWhiteSpace(ApiKey);
}
