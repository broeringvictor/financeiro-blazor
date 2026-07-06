using Microsoft.EntityFrameworkCore;
using WebApp.Data;
using WebApp.Models;

namespace WebApp.Services;

/// <summary>Configuração da geração automática de faturas em aberto (seção "GeracaoFaturas").</summary>
public sealed class GeracaoFaturaOptions
{
    /// <summary>Liga/desliga o worker diário de geração.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Horário do dia (local) em que a geração é disparada.</summary>
    public TimeOnly HoraExecucao { get; set; } = new(2, 0);

    /// <summary>Quantas faturas em aberto manter à frente quando a recorrência não tem prazo (EndDate nulo).</summary>
    public int BufferMinimo { get; set; } = 3;

    /// <summary>Teto de segurança de ocorrências avaliadas por conta numa execução (evita geração descontrolada).</summary>
    public int MaxOcorrencias { get; set; } = 600;
}

/// <summary>
/// Gera faturas (<see cref="Invoice"/>) Pending diretamente da recorrência da conta, sem depender de e-mail/boleto.
/// Cobre as contas pagas manualmente (ex.: Pix) e cria "placeholders" que a ingestão de e-mail depois preenche
/// com o valor real — a dedupe por competência de <see cref="IngestaoFaturaService"/> evita duplicar. O valor
/// usado é <see cref="Bill.FixedAmount"/> quando existe, senão 0 ("valor a conhecer").
/// </summary>
public sealed class GeracaoFaturaService(
    ApplicationDbContext db,
    GeracaoFaturaOptions options,
    ILogger<GeracaoFaturaService> logger)
{
    /// <summary>
    /// Gera as faturas em aberto faltantes de uma conta e retorna quantas foram criadas.
    /// Sem prazo (<see cref="RecurrenceRule.EndDate"/> nulo): garante <see cref="GeracaoFaturaOptions.BufferMinimo"/>
    /// faturas na janela (mês corrente + futuros). Com prazo: cria todas as ocorrências até o EndDate.
    /// Idempotente: nunca duplica competência já existente nem sobrescreve fatura existente.
    /// </summary>
    public async Task<int> GerarPendentesAsync(string userId, Bill bill, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);

        if (!bill.Active)
        {
            return 0;
        }

        var regra = bill.Recurrence;
        var primeiroDiaMesAtual = new DateOnly(DateTime.Today.Year, DateTime.Today.Month, 1);

        // Competências já existentes (qualquer status, não deletadas) na janela mês corrente + futuros.
        var existentes = await db.Invoices
            .Where(i => i.UserId == userId && i.BillId == bill.Id
                        && i.DeletedAt == null && i.ReferenceMonth >= primeiroDiaMesAtual)
            .Select(i => i.ReferenceMonth)
            .ToListAsync(ct);

        var competencias = existentes.ToHashSet();

        var criadas = 0;
        // NextDueDateAfter é estritamente posterior: começar no último dia do mês anterior inclui o mês corrente.
        var cursor = primeiroDiaMesAtual.AddDays(-1);

        for (var i = 0; i < options.MaxOcorrencias; i++)
        {
            // Sem prazo: parar assim que a janela já tiver o mínimo desejado de faturas.
            if (regra.EndDate is null && competencias.Count >= options.BufferMinimo)
            {
                break;
            }

            var vencimento = regra.NextDueDateAfter(cursor);

            // Recorrência encerrada: NextDueDateAfter devolve uma ocorrência <= cursor.
            if (vencimento <= cursor)
            {
                break;
            }

            if (regra.EndDate is { } fim && vencimento > fim)
            {
                break;
            }

            cursor = vencimento;

            var referencia = new DateOnly(vencimento.Year, vencimento.Month, 1);
            if (!competencias.Add(referencia))
            {
                continue; // já existe fatura nessa competência
            }

            db.Invoices.Add(new Invoice(userId, bill.Id, referencia, bill.FixedAmount ?? 0m, vencimento));
            criadas++;
        }

        if (criadas > 0)
        {
            await db.SaveChangesAsync(ct);
            logger.LogInformation(
                "Geradas {Qtd} fatura(s) em aberto para a conta {BillId} ({Nome}).", criadas, bill.Id, bill.Name);
        }

        return criadas;
    }

    /// <summary>Gera as faturas em aberto faltantes de todas as contas ativas do usuário. Retorna o total criado.</summary>
    public async Task<int> GerarParaUsuarioAsync(string userId, CancellationToken ct = default)
    {
        var contas = await db.Bills
            .Where(b => b.UserId == userId && b.Active && b.DeletedAt == null)
            .ToListAsync(ct);

        var total = 0;
        foreach (var bill in contas)
        {
            total += await GerarPendentesAsync(userId, bill, ct);
        }

        return total;
    }
}
