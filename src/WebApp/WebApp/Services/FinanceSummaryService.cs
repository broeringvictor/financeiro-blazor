using Microsoft.EntityFrameworkCore;
using WebApp.Data;
using WebApp.Models.Enums;

namespace WebApp.Services;

/// <summary>Total de despesas (Transaction) lançadas num mês (competência = mês de <c>CreatedAt</c>).</summary>
public sealed record GastoMensal(DateOnly Mes, decimal Total);

/// <summary>Previsão de gasto de uma conta recorrente, com a origem do valor usado.</summary>
public sealed record PrevisaoItem(Guid BillId, string Nome, decimal? ValorPrevisto, string Origem);

/// <summary>Previsão de gastos do próximo período: itens por conta + total somado.</summary>
public sealed record PrevisaoGastos(IReadOnlyList<PrevisaoItem> Itens, decimal Total);

/// <summary>
/// Agregações financeiras derivadas de <see cref="WebApp.Models.Transaction"/> (saldo, gastos por mês)
/// e da combinação Bill/Invoice/Transaction (previsão de gastos). Somente leitura.
/// </summary>
public sealed class FinanceSummaryService(ApplicationDbContext db)
{
    /// <summary>Saldo = receitas − despesas. Transferências não afetam o saldo (movimentação neutra).</summary>
    public async Task<decimal> ObterSaldoAsync(string userId, CancellationToken ct = default)
    {
        var totaisPorTipo = await db.Transactions
            .Where(t => t.UserId == userId && t.DeletedAt == null)
            .GroupBy(t => t.Type)
            .Select(g => new { Tipo = g.Key, Total = g.Sum(t => t.Amount) })
            .ToListAsync(ct);

        var receitas = totaisPorTipo.FirstOrDefault(t => t.Tipo == ETransactionTypes.Income)?.Total ?? 0m;
        var despesas = totaisPorTipo.FirstOrDefault(t => t.Tipo == ETransactionTypes.Expense)?.Total ?? 0m;

        return receitas - despesas;
    }

    /// <summary>Total de despesas por mês, dos últimos <paramref name="meses"/> meses (inclui meses sem gasto, com total 0).</summary>
    public async Task<IReadOnlyList<GastoMensal>> ObterGastosPorMesAsync(string userId, int meses = 12, CancellationToken ct = default)
    {
        var primeiroMes = new DateOnly(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1).AddMonths(-(meses - 1));
        var desde = primeiroMes.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);

        var lancamentos = await db.Transactions
            .Where(t => t.UserId == userId && t.DeletedAt == null
                        && t.Type == ETransactionTypes.Expense && t.CreatedAt >= desde)
            .Select(t => new { t.CreatedAt, t.Amount })
            .ToListAsync(ct);

        var porMes = lancamentos
            .GroupBy(t => new DateOnly(t.CreatedAt.Year, t.CreatedAt.Month, 1))
            .ToDictionary(g => g.Key, g => g.Sum(t => t.Amount));

        var resultado = new List<GastoMensal>(meses);
        var cursor = primeiroMes;
        for (var i = 0; i < meses; i++)
        {
            resultado.Add(new GastoMensal(cursor, porMes.GetValueOrDefault(cursor)));
            cursor = cursor.AddMonths(1);
        }

        return resultado;
    }

    /// <summary>
    /// Previsão de gasto por conta ativa, nesta ordem de prioridade:
    /// 1) valor da fatura já reconhecida na competência atual (mais atual);
    /// 2) média dos valores das transações pagas ligadas à conta (histórico);
    /// 3) valor fixo cadastrado na conta;
    /// 4) sem dados suficientes → sem previsão para essa conta.
    /// </summary>
    public async Task<PrevisaoGastos> ObterPrevisaoAsync(string userId, CancellationToken ct = default)
    {
        var bills = await db.Bills
            .Where(b => b.UserId == userId && b.Active && b.DeletedAt == null)
            .ToListAsync(ct);

        if (bills.Count == 0)
        {
            return new PrevisaoGastos([], 0m);
        }

        var billIds = bills.Select(b => b.Id).ToList();
        var referenciaAtual = new DateOnly(DateTime.Today.Year, DateTime.Today.Month, 1);

        var faturaAtualPorBill = await db.Invoices
            .Where(i => i.UserId == userId && i.BillId.HasValue && billIds.Contains(i.BillId!.Value)
                        && i.ReferenceMonth == referenciaAtual && i.Amount > 0 && i.DeletedAt == null)
            .ToDictionaryAsync(i => i.BillId!.Value, i => i.Amount, ct);

        var pagosPorBill = await (
                from invoice in db.Invoices
                join transacao in db.Transactions on invoice.PaymentTransactionId equals transacao.Id
                where invoice.UserId == userId && invoice.BillId.HasValue && billIds.Contains(invoice.BillId!.Value)
                      && invoice.DeletedAt == null && transacao.DeletedAt == null
                select new { BillId = invoice.BillId!.Value, transacao.Amount })
            .ToListAsync(ct);

        var mediaPorBill = pagosPorBill
            .GroupBy(p => p.BillId)
            .ToDictionary(g => g.Key, g => g.Average(p => p.Amount));

        var itens = bills.Select(bill => CalcularPrevisao(bill, faturaAtualPorBill, mediaPorBill)).ToList();
        var total = itens.Sum(i => i.ValorPrevisto ?? 0m);

        return new PrevisaoGastos(itens, total);
    }

    private static PrevisaoItem CalcularPrevisao(
        Models.Bill bill,
        IReadOnlyDictionary<Guid, decimal> faturaAtualPorBill,
        IReadOnlyDictionary<Guid, decimal> mediaPorBill)
    {
        if (faturaAtualPorBill.TryGetValue(bill.Id, out var valorFatura))
        {
            return new PrevisaoItem(bill.Id, bill.Name, valorFatura, "Fatura atual");
        }

        if (mediaPorBill.TryGetValue(bill.Id, out var media))
        {
            return new PrevisaoItem(bill.Id, bill.Name, media, "Média histórica");
        }

        if (bill.FixedAmount is { } fixo)
        {
            return new PrevisaoItem(bill.Id, bill.Name, fixo, "Valor fixo");
        }

        return new PrevisaoItem(bill.Id, bill.Name, null, "Sem dados");
    }
}
