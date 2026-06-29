using Services.Gmail;
using Services.Pdf;
using WebApp.Models;

namespace WebApp.Services;

/// <summary>
/// Fluxo determinístico de busca de fatura: pesquisa no Gmail, baixa o PDF, extrai os dados
/// e registra a <see cref="Invoice"/>. Reutiliza as mesmas ferramentas do agente, mas sem o LLM,
/// para um resultado previsível ao clicar o botão.
/// </summary>
public sealed class BuscaFaturaOrchestrator(
    GmailServiceFactory gmailFactory,
    GmailOptions gmailOptions,
    FaturaPdfExtractor pdfExtractor,
    IngestaoFaturaService ingestao,
    ILoggerFactory loggerFactory)
{
    private readonly ILogger _logger = loggerFactory.CreateLogger<BuscaFaturaOrchestrator>();

    /// <summary>
    /// Procura o e-mail mais recente que casa com <paramref name="consultaGmail"/>, baixa/extrai o PDF
    /// e cria/atualiza a fatura. Retorna null se nenhum e-mail for encontrado.
    /// </summary>
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

        var dados = new FaturaExtraida(
            BillerName: billerName,
            Valor: info.Valor,
            Emissao: info.Data,
            Vencimento: info.Vencimento,
            SourceEmailMessageId: email.Id,
            PdfPath: pdfPath,
            TextoBruto: detalhe.Corpo);

        var invoice = await ingestao.UpsertAsync(userId, dados, ct);
        _logger.LogInformation("Fatura registrada: {Id} valor={Valor} venc={Venc}",
            invoice.Id, invoice.Amount, invoice.DueDate);

        return invoice;
    }
}
