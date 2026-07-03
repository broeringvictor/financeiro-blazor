using Microsoft.EntityFrameworkCore;
using Services.Pdf;
using WebApp.Data;
using WebApp.Models;
using WebApp.Models.Enums;

namespace WebApp.Services;

/// <summary>
/// Persiste faturas a partir dos dados extraídos pelo agente e registra o pagamento (cria a Transaction).
/// Roda no WebApp, onde existe o usuário autenticado.
/// </summary>
public sealed class IngestaoFaturaService(ApplicationDbContext db, ILogger<IngestaoFaturaService> logger)
{
    /// <summary>Tolerância para considerar o valor extraído igual ao valor fixo cadastrado.</summary>
    private const decimal ToleranciaValorFixo = 0.01m;

    /// <summary>
    /// Cria ou atualiza a fatura (idempotente). Dedupe por e-mail de origem e por conta/competência.
    /// </summary>
    public async Task<Invoice> UpsertAsync(string userId, FaturaExtraida dados, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);

        var bill = await MatchBillAsync(userId, dados, ct);
        var valor = ValidarValor(bill, dados.Valor);

        // 1) Dedupe pelo e-mail de origem.
        if (!string.IsNullOrWhiteSpace(dados.SourceEmailMessageId))
        {
            var existentePorEmail = await db.Invoices.FirstOrDefaultAsync(
                i => i.UserId == userId
                     && i.SourceEmailMessageId == dados.SourceEmailMessageId
                     && i.DeletedAt == null,
                ct);

            if (existentePorEmail is not null)
            {
                existentePorEmail.UpdateFromExtraction(valor, dados.Vencimento, dados.Emissao, dados.PdfPath, dados.TextoBruto);
                await db.SaveChangesAsync(ct);
                return existentePorEmail;
            }
        }

        // Competência ~ mês de consumo: usa a emissão quando disponível, senão o vencimento.
        var referencia = PrimeiroDiaDoMes(dados.Emissao ?? dados.Vencimento ?? DateOnly.FromDateTime(DateTime.Today));

        // 2) Dedupe por conta/competência.
        if (bill is not null)
        {
            var existentePorPeriodo = await db.Invoices.FirstOrDefaultAsync(
                i => i.UserId == userId && i.BillId == bill.Id && i.ReferenceMonth == referencia && i.DeletedAt == null,
                ct);

            if (existentePorPeriodo is not null)
            {
                existentePorPeriodo.UpdateFromExtraction(valor, dados.Vencimento, dados.Emissao, dados.PdfPath, dados.TextoBruto);
                await db.SaveChangesAsync(ct);
                return existentePorPeriodo;
            }
        }

        var nova = new Invoice(
            userId,
            bill?.Id,
            referencia,
            valor ?? 0m,
            dados.Vencimento ?? UltimoDiaDoMes(referencia),
            dados.Emissao,
            dados.SourceEmailMessageId,
            dados.PdfPath,
            dados.TextoBruto);

        db.Invoices.Add(nova);
        await db.SaveChangesAsync(ct);
        return nova;
    }

    /// <summary>
    /// <see cref="Bill.FixedAmount"/> é só uma referência para previsão de gastos — o valor extraído da
    /// fatura sempre prevalece e nunca é descartado por divergir dele. Só loga quando diverge, pra
    /// facilitar notar contas com FixedAmount desatualizado.
    /// </summary>
    private decimal? ValidarValor(Bill? bill, decimal? valorExtraido)
    {
        if (bill?.FixedAmount is { } fixo && valorExtraido is { } extraido
            && Math.Abs(extraido - fixo) > ToleranciaValorFixo)
        {
            logger.LogInformation(
                "Valor extraído (R$ {Extraido}) diverge do valor fixo cadastrado para '{Conta}' (R$ {Fixo}); usando o valor extraído mesmo assim.",
                extraido, bill.Name, fixo);
        }

        return valorExtraido;
    }

    /// <summary>Quita a fatura: cria uma Transaction (Expense) e liga-a à fatura (1:1).</summary>
    public async Task<Transaction> PagarAsync(Guid invoiceId, string userId, CancellationToken ct = default)
    {
        var invoice = await db.Invoices.Include(i => i.Bill).FirstOrDefaultAsync(
                          i => i.Id == invoiceId && i.UserId == userId && i.DeletedAt == null, ct)
                      ?? throw new InvalidOperationException("Fatura não encontrada.");

        if (invoice.Amount <= 0)
        {
            throw new InvalidOperationException("Valor da fatura ainda não reconhecido; ajuste o valor antes de pagar.");
        }

        var categoria = invoice.Bill?.Category ?? ETransactionCategory.Utilities;
        var titulo = $"Fatura {invoice.Bill?.Name ?? "avulsa"}";

        var transaction = new Transaction(userId, ETransactionTypes.Expense, categoria, titulo, null, invoice.Amount);

        db.Transactions.Add(transaction);
        invoice.RegisterPayment(transaction.Id);
        await db.SaveChangesAsync(ct);

        return transaction;
    }

    /// <summary>Exclui (logicamente) a fatura e remove o PDF salvo em disco, se houver.</summary>
    public async Task ExcluirAsync(Guid invoiceId, string userId, CancellationToken ct = default)
    {
        var invoice = await db.Invoices.FirstOrDefaultAsync(
                          i => i.Id == invoiceId && i.UserId == userId && i.DeletedAt == null, ct)
                      ?? throw new InvalidOperationException("Fatura não encontrada.");

        var pdfPath = invoice.PdfPath;

        invoice.Delete();
        await db.SaveChangesAsync(ct);

        if (!string.IsNullOrWhiteSpace(pdfPath) && File.Exists(pdfPath))
        {
            try
            {
                File.Delete(pdfPath);
            }
            catch (Exception ex)
            {
                // A exclusão da fatura já foi persistida; falha ao apagar o arquivo não deve
                // reverter isso — só sobra um PDF órfão em disco.
                logger.LogWarning(ex, "Falha ao apagar o PDF da fatura {InvoiceId} ({Caminho}).", invoiceId, pdfPath);
            }
        }
    }

    private async Task<Bill?> MatchBillAsync(string userId, FaturaExtraida dados, CancellationToken ct)
    {
        var ativas = await db.Bills
            .Where(b => b.UserId == userId && b.Active && b.DeletedAt == null)
            .ToListAsync(ct);

        return ativas.FirstOrDefault(b =>
            (!string.IsNullOrWhiteSpace(dados.BillerName)
             && (b.BillerName.Contains(dados.BillerName, StringComparison.OrdinalIgnoreCase)
                 || dados.BillerName.Contains(b.BillerName, StringComparison.OrdinalIgnoreCase)))
            || b.MatchesEmail(dados.BillerName, dados.TextoBruto));
    }

    private static DateOnly PrimeiroDiaDoMes(DateOnly data) => new(data.Year, data.Month, 1);

    private static DateOnly UltimoDiaDoMes(DateOnly data) =>
        new(data.Year, data.Month, DateTime.DaysInMonth(data.Year, data.Month));
}
