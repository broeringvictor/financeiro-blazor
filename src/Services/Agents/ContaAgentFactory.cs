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

    /// <summary>Modelo Claude usado pelo agente.</summary>
    public string Model { get; set; } = "claude-opus-4-8";

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

    // TODO: substitua pelo prompt definitivo — você implementará as instruções depois.
    private const string InstrucoesPlaceholder =
        "Você é um assistente que ajuda a localizar contas, faturas e boletos nos e-mails do usuário. " +
        "Você pode chamar as ferramentas BuscarEmailsDeContas e ObterDetalhesEmail para pesquisar e ler e-mails. " +
        "Dessa forma procure por conta de Água relacionadas Águas de Palhoça e contas de de luz relacionadas a Celesc. " +
        "Quando encontrar o e-mail da fatura, chame BaixarAnexosPdf com o ID da mensagem para baixar o PDF. " +
        "Em seguida, chame ExtrairDadosFatura com o caminho do PDF para obter o valor, a data e a data de vencimento, " +
        "e informe esses dados (e o caminho do arquivo) na resposta.";
    

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
            instructions: InstrucoesPlaceholder,
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
