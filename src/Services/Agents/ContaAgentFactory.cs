using Anthropic;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Services.Gmail;
using Services.Pdf;

namespace Services.Agents;

/// <summary>
/// Configuração do agente de IA. A ApiKey deve vir de user-secrets ou variável de
/// ambiente (ex.: ANTHROPIC_API_KEY), nunca do appsettings.json versionado.
/// </summary>
public sealed class AgenteOptions
{
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>
    /// Modelo Claude. Sonnet 4.6 é o padrão por equilibrar custo/latência e confiabilidade
    /// em extração com tools. Para máxima economia/velocidade, use "claude-haiku-4-5".
    /// </summary>
    public string Model { get; set; } = "claude-sonnet-4-6";

    public string Nome { get; set; } = "AgenteContas";
}

/// <summary>
/// Monta um <see cref="AIAgent"/> do Microsoft Agent Framework usando a Anthropic (Claude)
/// como provider e as ferramentas do Gmail como function tools.
/// </summary>
public sealed class ContaAgentFactory(
    AgenteOptions options,
    GmailServiceFactory gmailFactory,
    GmailOptions gmailOptions,
    FaturaPdfExtractor pdfExtractor,
    ILoggerFactory loggerFactory)
{
    private readonly ILogger _logger = loggerFactory.CreateLogger<ContaAgentFactory>();

    // System prompt focado em eficiência: poucas chamadas de tool, queries enxutas e parada cedo.
    private const string Instrucoes =
        """
        Você é um agente que localiza faturas/boletos no Gmail do usuário, baixa o PDF e extrai os dados.

        Seja EFICIENTE — minimize chamadas de ferramenta:
        1. Monte UMA consulta enxuta para BuscarEmailsDeContas usando operadores do Gmail:
           - prefira o fornecedor + tipo: ex. `Celesc fatura OR boleto`
           - restrinja anexos: `has:attachment filename:pdf`
           - restrinja período: `newer_than:120d`
           - se souber o remetente, use `from:`
           Peça poucos resultados (maxResultados entre 3 e 5).
        2. Escolha apenas o e-mail MAIS provável de ser a fatura mais recente. NÃO abra vários e-mails.
        3. Chame BaixarAnexosPdf nesse e-mail. Se vier um PDF, chame ExtrairDadosFatura com o caminho.
           Só use ObterDetalhesEmail se NÃO houver PDF e você precisar ler o corpo para achar os valores.
        4. Pare assim que tiver valor, vencimento e (se houver) emissão. Não faça buscas extras.

        Regras:
        - Nunca invente valores ou datas; use somente o que vier das ferramentas.
        - Se não encontrar nenhuma fatura, diga isso de forma objetiva, sem chamar mais ferramentas.
        - Responda de forma curta: fornecedor, valor, emissão, vencimento e o caminho do PDF baixado.
        """;


    public async Task<AIAgent> CriarAsync(CancellationToken ct = default)
    {
        _logger.LogInformation("Criando agente '{Nome}' (modelo {Modelo}). Autenticando no Gmail...",
            options.Nome, options.Model);

        var gmail = await gmailFactory.CreateAsync(ct);
        _logger.LogInformation("Gmail autenticado. Agente pronto com as tools do Gmail.");

        var ferramentas = new GmailTools(
            gmail,
            gmailOptions.User,
            loggerFactory.CreateLogger<GmailTools>(),
            gmailOptions.DownloadDirectory);

        var client = new AnthropicClient { ApiKey = options.ApiKey };

        return client.AsAIAgent(
            model: options.Model,
            instructions: Instrucoes,
            name: options.Nome,
            tools:
            [
                AIFunctionFactory.Create(ferramentas.BuscarEmailsDeContas),
                AIFunctionFactory.Create(ferramentas.ObterDetalhesEmail),
                AIFunctionFactory.Create(ferramentas.BaixarAnexosPdf),
                AIFunctionFactory.Create(pdfExtractor.ExtrairDadosFatura),
            ]);
    }
}
