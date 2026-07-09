using Microsoft.EntityFrameworkCore;
using Services.Agents;
using Services.Pdf;
using WebApp.Data;
using WebApp.Models;

namespace WebApp.Services;

/// <summary>Resumo da importação de um arquivo.</summary>
public sealed record ItemImportacao(
    string Arquivo,
    string Situacao,
    string? Conta,
    decimal? Valor,
    DateOnly? Vencimento,
    bool PrecisaRevisao);

/// <summary>Resultado agregado de um lote de importação (ex.: os PDFs de uma leva do webhook).</summary>
public sealed record ResultadoImportacaoLote(
    int Total,
    int Anexadas,
    int ParaRevisao,
    int Falhas,
    IReadOnlyList<ItemImportacao> Itens)
{
    /// <summary>Itens que precisam de confirmação humana (base para a mensagem única enviada ao grupo).</summary>
    public IReadOnlyList<ItemImportacao> Pendentes => Itens.Where(i => i.PrecisaRevisao).ToList();
}

/// <summary>
/// Importa faturas a partir de caminhos de PDF já baixados (ex.: pelos anexos do grupo de WhatsApp). Para cada
/// arquivo: extrai valor/datas (determinístico), casa com uma conta pelo texto, deixa a IA supervisora confirmar
/// a conta ou marcar para revisão humana, e SEMPRE salva a fatura (anexada quando confiável, avulsa quando não).
/// Os incertos são acumulados para uma única notificação, sem gravar nada duas vezes por PDF.
/// </summary>
public sealed class ImportacaoFaturaOrchestrator(
    ApplicationDbContext db,
    IFaturaLeitorPdf leitorPdf,
    IFaturaClassificadorSupervisor supervisor,
    IngestaoFaturaService ingestao,
    ILogger<ImportacaoFaturaOrchestrator> logger)
{
    public async Task<ResultadoImportacaoLote> ImportarAsync(
        string userId, IReadOnlyList<string> caminhosPdf, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);

        var contas = await db.Bills
            .Include(b => b.Category)
            .Where(b => b.UserId == userId && b.Active && b.DeletedAt == null)
            .ToListAsync(ct);

        var candidatas = contas
            .Select(b => new ContaCandidata(
                b.Id.ToString(), b.Name, b.BillerName, b.Category?.Name ?? "(sem categoria)"))
            .ToList();

        var itens = new List<ItemImportacao>();
        int anexadas = 0, paraRevisao = 0, falhas = 0;

        foreach (var caminho in caminhosPdf ?? [])
        {
            var arquivo = Path.GetFileName(caminho);
            try
            {
                var info = leitorPdf.ExtrairDadosFatura(caminho);
                var texto = TentarLerTexto(caminho);

                // Match determinístico primeiro; a IA supervisiona esse palpite antes de anexar sozinho.
                var sugerida = contas.FirstOrDefault(b => b.MatchesFatura(null, texto));
                var decisao = await supervisor.RevisarAsync(
                    new ContextoClassificacao(
                        info.Valor, info.Data, info.Vencimento, texto, sugerida?.Id.ToString(), candidatas),
                    ct);

                var conta = decisao is { Aprovado: true, ContaId: { } id }
                    ? contas.FirstOrDefault(b => b.Id.ToString() == id)
                    : null;

                var dados = new FaturaExtraida(
                    BillerName: null,
                    Valor: info.Valor,
                    Emissao: info.Data,
                    Vencimento: info.Vencimento,
                    SourceEmailMessageId: null,
                    PdfPath: caminho,
                    TextoBruto: texto,
                    Competencia: info.Competencia);

                // Sempre salva: nada se perde. Anexa à conta confiável; senão fica avulsa para revisão.
                await ingestao.SalvarClassificadaAsync(userId, dados, conta, ct);

                var precisaRevisao = conta is null;
                if (precisaRevisao)
                {
                    paraRevisao++;
                }
                else
                {
                    anexadas++;
                }

                itens.Add(new ItemImportacao(
                    arquivo,
                    conta is not null
                        ? $"Anexada à conta \"{conta.Name}\"."
                        : $"Salva como avulsa para revisão ({decisao.Motivo ?? "sem conta confiável"}).",
                    conta?.Name,
                    info.Valor,
                    info.Vencimento,
                    precisaRevisao));
            }
            catch (Exception ex)
            {
                falhas++;
                logger.LogError(ex, "Falha ao importar a fatura do arquivo '{Arquivo}'.", caminho);
                itens.Add(new ItemImportacao(arquivo, "Falha ao processar o arquivo.", null, null, null, true));
            }
        }

        logger.LogInformation(
            "Importação de faturas: {Total} arquivo(s) — {Anexadas} anexada(s), {Revisao} para revisão, {Falhas} falha(s).",
            itens.Count, anexadas, paraRevisao, falhas);

        return new ResultadoImportacaoLote(itens.Count, anexadas, paraRevisao, falhas, itens);
    }

    private string? TentarLerTexto(string caminho)
    {
        try
        {
            return leitorPdf.ObterTextoBruto(caminho);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Falha ao ler o texto do PDF '{Arquivo}' para classificação.", caminho);
            return null;
        }
    }
}
