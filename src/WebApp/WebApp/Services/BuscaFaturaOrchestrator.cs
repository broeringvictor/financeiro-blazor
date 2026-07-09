using Services.Agents;
using Services.Gmail;
using Services.Pdf;
using WebApp.Models;

namespace WebApp.Services;

/// <summary>
/// Fluxo determinístico de busca de fatura: pesquisa no Gmail, baixa o PDF, extrai os dados
/// e registra a <see cref="Invoice"/>. Reutiliza as mesmas ferramentas do agente, mas sem o LLM,
/// para um resultado previsível ao clicar o botão. Só recorre ao agente de IA (<see cref="FaturaLlmFallbackExtractor"/>)
/// como último recurso, quando a extração por regex não acha valor/vencimento.
/// </summary>
public sealed class BuscaFaturaOrchestrator(
    GmailServiceFactory gmailFactory,
    GmailOptions gmailOptions,
    FaturaPdfExtractor pdfExtractor,
    FaturaLlmFallbackExtractor llmFallback,
    IngestaoFaturaService ingestao,
    ILoggerFactory loggerFactory)
{
    private readonly ILogger _logger = loggerFactory.CreateLogger<BuscaFaturaOrchestrator>();

    /// <summary>
    /// Procura o e-mail mais recente que casa com <paramref name="consultaGmail"/>, baixa/extrai o PDF
    /// e cria/atualiza a fatura. Retorna null se nenhum e-mail for encontrado.
    /// </summary>
    /// <summary>Busca a fatura de uma conta usando a consulta configurada nela (SearchQuery) como contexto.</summary>
    public Task<Invoice?> BuscarPorContaAsync(string userId, Bill bill, CancellationToken ct = default)
    {
        var consulta = string.IsNullOrWhiteSpace(bill.SearchQuery) ? bill.BillerName : bill.SearchQuery;
        return BuscarERegistrarAsync(userId, consulta, bill.BillerName, ct);
    }

    public async Task<Invoice?> BuscarERegistrarAsync(
        string userId,
        string consultaGmail,
        string billerName,
        CancellationToken ct = default)
    {
        var gmail = await gmailFactory.CreateAsync(ct);
        var tools = new GmailTools(
            gmail,
            gmailOptions.User,
            loggerFactory.CreateLogger<GmailTools>(),
            gmailOptions.DownloadDirectory);

        var resumos = await tools.BuscarEmailsDeContas(consultaGmail, maxResultados: 5, ct);
        if (resumos.Count == 0)
        {
            _logger.LogInformation("Nenhum e-mail encontrado para a consulta '{Consulta}'.", consultaGmail);
            return null;
        }

        var email = resumos[0];
        var detalhe = await tools.ObterDetalhesEmail(email.Id, ct);
        var pdfs = await tools.BaixarAnexosPdf(email.Id, ct);
        var pdfPath = pdfs.FirstOrDefault();

        var info = pdfPath is not null
            ? pdfExtractor.ExtrairDadosFatura(pdfPath)
            : new FaturaInfo(null, null, null, "Sem anexo PDF.");

        // Fallback 1: se o PDF não rendeu valor/vencimento, tenta o corpo do e-mail.
        if (info.Valor is null || info.Vencimento is null)
        {
            var doCorpo = pdfExtractor.ExtrairDeTexto(detalhe.Corpo);
            info = info with
            {
                Valor = info.Valor ?? doCorpo.Valor,
                Data = info.Data ?? doCorpo.Data,
                Vencimento = info.Vencimento ?? doCorpo.Vencimento,
                Competencia = info.Competencia ?? doCorpo.Competencia,
            };
        }

        // Fallback 2 (último recurso): regex não achou tudo — pede pro agente de IA ler o texto bruto.
        if (info.Valor is null || info.Vencimento is null)
        {
            var textoPdf = pdfPath is not null ? TentarLerTextoBruto(pdfPath) : null;
            var textoCombinado = string.Join("\n\n", new[] { textoPdf, detalhe.Corpo }
                .Where(t => !string.IsNullOrWhiteSpace(t)));

            if (!string.IsNullOrWhiteSpace(textoCombinado))
            {
                _logger.LogInformation(
                    "Extração determinística incompleta (e-mail {Id}); acionando o agente de IA como fallback.",
                    email.Id);

                var doAgente = await llmFallback.ExtrairAsync(textoCombinado, ct);
                info = info with
                {
                    Valor = info.Valor ?? doAgente.Valor,
                    Data = info.Data ?? doAgente.Data,
                    Vencimento = info.Vencimento ?? doAgente.Vencimento,
                    Erro = doAgente.Erro ?? info.Erro,
                };
            }
        }

        if (info.Valor is null)
        {
            _logger.LogWarning("Não foi possível extrair o valor da fatura (e-mail {Id}, pdf {Pdf}).",
                email.Id, pdfPath ?? "sem PDF");
        }

        var dados = new FaturaExtraida(
            BillerName: billerName,
            Valor: info.Valor,
            Emissao: info.Data,
            Vencimento: info.Vencimento,
            SourceEmailMessageId: email.Id,
            PdfPath: pdfPath,
            TextoBruto: detalhe.Corpo,
            Competencia: info.Competencia);

        var invoice = await ingestao.UpsertAsync(userId, dados, ct);
        _logger.LogInformation("Fatura registrada: {Id} valor={Valor} venc={Venc}",
            invoice.Id, invoice.Amount, invoice.DueDate);

        return invoice;
    }

    private string? TentarLerTextoBruto(string pdfPath)
    {
        try
        {
            return pdfExtractor.ObterTextoBruto(pdfPath);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Falha ao reler o texto do PDF para o fallback de IA ({Caminho}).", pdfPath);
            return null;
        }
    }
}
