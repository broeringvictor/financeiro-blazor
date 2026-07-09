using System.Globalization;
using System.Text;
using System.Text.Json;
using Anthropic;
using Microsoft.Extensions.Logging;

namespace Services.Agents;

/// <summary>Uma conta candidata apresentada à IA supervisora (dados mínimos, sem acoplar aos modelos do WebApp).</summary>
public sealed record ContaCandidata(string Id, string Nome, string Fornecedor, string Categoria);

/// <summary>Contexto para a IA revisar a classificação determinística de uma fatura.</summary>
public sealed record ContextoClassificacao(
    decimal? Valor,
    DateOnly? Emissao,
    DateOnly? Vencimento,
    string? TextoPdf,
    string? ContaSugeridaId,
    IReadOnlyList<ContaCandidata> Contas);

/// <summary>
/// Decisão da IA supervisora. <see cref="Aprovado"/> = confia que a fatura é da conta <see cref="ContaId"/> e
/// pode anexar automaticamente. <see cref="PrecisaHumano"/> = deixar para revisão humana (salvar avulsa e perguntar).
/// </summary>
public sealed record ClassificacaoSupervisionada(string? ContaId, bool Aprovado, bool PrecisaHumano, string? Motivo);

/// <summary>Revisa (supervisiona) a classificação de uma fatura numa conta antes de gravar automaticamente.</summary>
public interface IFaturaClassificadorSupervisor
{
    Task<ClassificacaoSupervisionada> RevisarAsync(ContextoClassificacao contexto, CancellationToken ct = default);
}

/// <summary>
/// Supervisor determinístico + IA: recebe o palpite determinístico e a lista de contas e decide se a fatura
/// pertence a uma conta com confiança (anexa automaticamente) ou se precisa de revisão humana. Nunca decide
/// valores/datas — só a classificação. Stateless (como <see cref="FaturaLlmFallbackExtractor"/>): não precisa
/// de DbContext nem de escopo por usuário.
/// </summary>
public sealed class FaturaClassificadorSupervisor(AgenteOptions options, ILogger<FaturaClassificadorSupervisor> logger)
    : IFaturaClassificadorSupervisor
{
    private const string Instrucoes =
        """
        Você supervisiona a classificação de uma FATURA/BOLETO em uma das CONTAS recorrentes do usuário.
        Você recebe: os dados já extraídos da fatura, um trecho do texto do PDF, a conta sugerida por um
        matcher automático (pode ser nula ou errada) e a lista de contas disponíveis. Decida a qual conta a
        fatura pertence — ou que não dá para decidir com segurança. NÃO invente nem altere valores ou datas.

        Responda SOMENTE com um JSON, sem texto ao redor, neste formato exato:
        {"contaId": "<id da conta> ou null", "aprovado": <true|false>, "precisaHumano": <true|false>, "motivo": "<curto>"}

        Regras:
        - "aprovado"=true somente quando tiver ALTA confiança de que a fatura é daquela conta (fornecedor/nome
          batem claramente com o texto). Nesse caso preencha "contaId" e "precisaHumano"=false.
        - Se houver ambiguidade, empate entre contas, ou nenhuma conta plausível: "aprovado"=false,
          "precisaHumano"=true, e "contaId" com o melhor palpite (ou null se não houver nenhum).
        - Confie no palpite determinístico quando o texto o confirmar; corrija-o quando estiver claramente errado.
        - "motivo" é uma frase curta em português explicando a decisão.
        """;

    public async Task<ClassificacaoSupervisionada> RevisarAsync(ContextoClassificacao contexto, CancellationToken ct = default)
    {
        // Sem IA configurada: degrada para o determinístico — aprova o palpite se houver, senão pede humano.
        if (string.IsNullOrWhiteSpace(options.ApiKey))
        {
            return contexto.ContaSugeridaId is { } sugerida
                ? new ClassificacaoSupervisionada(sugerida, Aprovado: true, PrecisaHumano: false, "Match determinístico (IA não configurada).")
                : new ClassificacaoSupervisionada(null, Aprovado: false, PrecisaHumano: true, "Sem match determinístico e IA não configurada.");
        }

        try
        {
            var client = new AnthropicClient { ApiKey = options.ApiKey };
            var agente = client.AsAIAgent(model: options.Model, instructions: Instrucoes, name: "SupervisorClassificacaoFatura");

            var resposta = await agente.RunAsync(MontarPrompt(contexto), cancellationToken: ct);
            return Parsear(resposta.Text, contexto);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Falha na IA supervisora de classificação; caindo para revisão humana.");
            return new ClassificacaoSupervisionada(contexto.ContaSugeridaId, Aprovado: false, PrecisaHumano: true,
                $"Falha na IA supervisora: {ex.Message}");
        }
    }

    private static string MontarPrompt(ContextoClassificacao c)
    {
        var sb = new StringBuilder();
        sb.AppendLine("FATURA:");
        sb.AppendLine($"- valor: {(c.Valor is { } v ? v.ToString(CultureInfo.InvariantCulture) : "desconhecido")}");
        sb.AppendLine($"- emissão: {(c.Emissao?.ToString("dd/MM/yyyy") ?? "desconhecida")}");
        sb.AppendLine($"- vencimento: {(c.Vencimento?.ToString("dd/MM/yyyy") ?? "desconhecido")}");
        sb.AppendLine($"- conta sugerida (matcher automático): {c.ContaSugeridaId ?? "nenhuma"}");
        sb.AppendLine();
        sb.AppendLine("CONTAS DISPONÍVEIS (id | nome | fornecedor | categoria):");
        foreach (var conta in c.Contas)
        {
            sb.AppendLine($"- {conta.Id} | {conta.Nome} | {conta.Fornecedor} | {conta.Categoria}");
        }
        sb.AppendLine();
        sb.AppendLine("TEXTO DO PDF (trecho):");
        sb.AppendLine(Truncar(c.TextoPdf, 2000));
        return sb.ToString();
    }

    private ClassificacaoSupervisionada Parsear(string? texto, ContextoClassificacao contexto)
    {
        if (string.IsNullOrWhiteSpace(texto))
        {
            return new ClassificacaoSupervisionada(contexto.ContaSugeridaId, false, true, "IA supervisora não respondeu.");
        }

        try
        {
            using var doc = JsonDocument.Parse(ExtrairJson(texto));
            var root = doc.RootElement;

            var contaId = root.TryGetProperty("contaId", out var idEl) && idEl.ValueKind == JsonValueKind.String
                ? idEl.GetString()
                : null;

            // Só aceita um contaId que exista de fato na lista apresentada (evita alucinação de id).
            if (contaId is not null && contexto.Contas.All(x => x.Id != contaId))
            {
                contaId = null;
            }

            var aprovado = root.TryGetProperty("aprovado", out var apEl) && apEl.ValueKind == JsonValueKind.True;
            var precisaHumano = !root.TryGetProperty("precisaHumano", out var phEl)
                                || phEl.ValueKind != JsonValueKind.False;
            var motivo = root.TryGetProperty("motivo", out var mEl) && mEl.ValueKind == JsonValueKind.String
                ? mEl.GetString()
                : null;

            // Aprovação exige uma conta concreta; sem conta válida, sempre cai em revisão humana.
            if (contaId is null)
            {
                aprovado = false;
                precisaHumano = true;
            }

            return new ClassificacaoSupervisionada(contaId, aprovado && !precisaHumano, precisaHumano, motivo);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Resposta da IA supervisora em formato inesperado: {Resposta}", texto);
            return new ClassificacaoSupervisionada(contexto.ContaSugeridaId, false, true, "Resposta da IA em formato inesperado.");
        }
    }

    private static string ExtrairJson(string texto)
    {
        var inicio = texto.IndexOf('{');
        var fim = texto.LastIndexOf('}');
        return inicio >= 0 && fim > inicio ? texto[inicio..(fim + 1)] : texto;
    }

    private static string Truncar(string? texto, int max) =>
        string.IsNullOrEmpty(texto) ? "(vazio)" : texto.Length <= max ? texto : texto[..max];
}
