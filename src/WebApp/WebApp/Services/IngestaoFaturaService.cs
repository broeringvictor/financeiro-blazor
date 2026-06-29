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
public sealed class IngestaoFaturaService(ApplicationDbContext db)
{
    /// <summary>
    /// Cria ou atualiza a fatura (idempotente). Dedupe por e-mail de origem e por conta/competência.
    /// </summary>
    public async Task<Invoice> UpsertAsync(string userId, FaturaExtraida dados, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);

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
                existentePorEmail.UpdateFromExtraction(dados.Valor, dados.Vencimento, dados.Emissao, dados.PdfPath, dados.TextoBruto);
                await db.SaveChangesAsync(ct);
                return existentePorEmail;
            }
        }

        var bill = await MatchBillAsync(userId, dados, ct);

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
                existentePorPeriodo.UpdateFromExtraction(dados.Valor, dados.Vencimento, dados.Emissao, dados.PdfPath, dados.TextoBruto);
                await db.SaveChangesAsync(ct);
                return existentePorPeriodo;
            }
        }

        var nova = new Invoice(
            userId,
            bill?.Id,
            referencia,
            dados.Valor ?? 0m,
            dados.Vencimento ?? UltimoDiaDoMes(referencia),
            dados.Emissao,
            dados.SourceEmailMessageId,
            dados.PdfPath,
            dados.TextoBruto);

        db.Invoices.Add(nova);
        await db.SaveChangesAsync(ct);
        return nova;
    }

    /// <summary>Quita a fatura: cria uma Transaction (Expense) e liga-a à fatura (1:1).</summary>
    public async Task<Transaction> PagarAsync(Guid invoiceId, string userId, CancellationToken ct = default)
    {
        var invoice = await db.Invoices.FirstOrDefaultAsync(
                          i => i.Id == invoiceId && i.UserId == userId && i.DeletedAt == null, ct)
                      ?? throw new InvalidOperationException("Fatura não encontrada.");

        var bill = invoice.BillId is { } billId
            ? await db.Bills.FirstOrDefaultAsync(b => b.Id == billId, ct)
            : null;

        var categoria = bill?.Category ?? ETransactionCategory.Utilities;
        var titulo = $"Fatura {bill?.Name ?? "avulsa"}";

        var transaction = new Transaction(userId, ETransactionTypes.Expense, categoria, titulo, null, invoice.Amount);

        db.Transactions.Add(transaction);
        invoice.RegisterPayment(transaction.Id);
        await db.SaveChangesAsync(ct);

        return transaction;
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
