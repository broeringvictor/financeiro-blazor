using System.Globalization;
using System.Text.Json;
using Anthropic;
using Microsoft.Extensions.Logging;
using Services.Pdf;

namespace Services.Agents;

/// <summary>
/// Último recurso quando a extração determinística (regex sobre o texto do PDF/e-mail) não acha
/// valor e/ou vencimento: pede pro modelo ler o texto bruto e devolver os campos em JSON.
/// </summary>
public sealed class FaturaLlmFallbackExtractor(AgenteOptions options, ILogger<FaturaLlmFallbackExtractor> logger)
{
    private const string Instrucoes =
        """
        Você extrai dados de uma fatura/boleto a partir do texto bruto informado (extraído de um PDF ou
        corpo de e-mail, podendo vir desorganizado). Responda SOMENTE com um JSON, sem texto ao redor,
        neste formato exato:
        {"valor": <número com ponto decimal ou null>, "emissao": "dd/MM/yyyy" ou null, "vencimento": "dd/MM/yyyy" ou null}

        Regras:
        - "valor" é o valor total a pagar do boleto/fatura (priorize rótulos como "Valor Documento",
          "Valor Cobrado", "Total a pagar"; ignore valores de itens individuais ou de nota fiscal quando
          divergirem do valor do boleto).
        - Nunca invente números ou datas; se não conseguir identificar com confiança, use null.
        - Não inclua explicação, markdown ou texto fora do JSON.
        """;

    /// <summary>Tenta extrair valor/datas via LLM. Retorna campos null e Erro preenchido em caso de falha.</summary>
    public async Task<FaturaInfo> ExtrairAsync(string texto, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(options.ApiKey))
        {
            return new FaturaInfo(null, null, null, "Agente de IA não configurado (Agente:ApiKey ausente).");
        }

        try
        {
            var client = new AnthropicClient { ApiKey = options.ApiKey };
            var agente = client.AsAIAgent(model: options.Model, instructions: Instrucoes, name: "ExtratorFaturaFallback");

            var resposta = await agente.RunAsync(texto, cancellationToken: ct);
            return ParsearResposta(resposta.Text);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Falha ao usar o agente de IA como fallback de extração de fatura.");
            return new FaturaInfo(null, null, null, $"Falha no fallback de IA: {ex.Message}");
        }
    }

    private FaturaInfo ParsearResposta(string? texto)
    {
        if (string.IsNullOrWhiteSpace(texto))
        {
            return new FaturaInfo(null, null, null, "Agente de IA não retornou resposta.");
        }

        try
        {
            var json = ExtrairJson(texto);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            decimal? valor = root.TryGetProperty("valor", out var v) && v.ValueKind == JsonValueKind.Number
                ? v.GetDecimal()
                : null;

            var emissao = LerData(root, "emissao");
            var vencimento = LerData(root, "vencimento");

            return new FaturaInfo(valor, emissao, vencimento);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Falha ao interpretar a resposta do agente de IA: {Resposta}", texto);
            return new FaturaInfo(null, null, null, "Resposta do agente de IA em formato inesperado.");
        }
    }

    private static DateOnly? LerData(JsonElement root, string propriedade)
    {
        if (!root.TryGetProperty(propriedade, out var el) || el.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        return DateOnly.TryParseExact(el.GetString(), ["dd/MM/yyyy", "dd/MM/yy"], CultureInfo.InvariantCulture,
            DateTimeStyles.None, out var data)
            ? data
            : null;
    }

    /// <summary>O modelo às vezes envolve o JSON em ```json ... ``` apesar da instrução; extrai o objeto puro.</summary>
    private static string ExtrairJson(string texto)
    {
        var inicio = texto.IndexOf('{');
        var fim = texto.LastIndexOf('}');
        return inicio >= 0 && fim > inicio ? texto[inicio..(fim + 1)] : texto;
    }
}
