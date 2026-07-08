using Microsoft.EntityFrameworkCore;
using Services.Pdf;
using WebApp.Data;
using WebApp.Models;
using WebApp.Models.Enums;
using WebApp.Models.ValueObjects;

namespace WebApp.Services;

/// <summary>Situação de uma fatura na importação por arquivo.</summary>
public enum ImportacaoStatus
{
    /// <summary>Fatura nova criada e associada à conta casada.</summary>
    Criada,

    /// <summary>Já havia fatura na mesma conta/competência; foi atualizada.</summary>
    Atualizada,

    /// <summary>Nenhuma conta existente casou com a fatura; nada foi criado.</summary>
    SemConta,

    /// <summary>Faltaram dados mínimos (valor e datas) para registrar a fatura.</summary>
    SemDados,
}

/// <summary>Resultado da importação de uma única fatura de arquivo.</summary>
public sealed record ResultadoImportacao(
    Invoice? Invoice,
    ImportacaoStatus Status,
    bool AjustouInicioConta,
    string? Conta);

/// <summary>
/// Persiste faturas a partir dos dados extraídos pelo agente e registra o pagamento (cria a Transaction).
/// Roda no WebApp, onde existe o usuário autenticado.
/// </summary>
public sealed class IngestaoFaturaService(
    ApplicationDbContext db,
    CategoryService categoryService,
    ILogger<IngestaoFaturaService> logger)
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
        var (invoice, _) = await PersistirFaturaAsync(userId, dados, bill, ct);
        return invoice;
    }

    /// <summary>
    /// Importa uma fatura vinda de arquivo (fluxo em lote): casa com uma conta existente e, se casar, registra
    /// a fatura — recuando a data de início da conta quando a fatura for anterior a ela. Sem conta correspondente
    /// não cria nada (<see cref="ImportacaoStatus.SemConta"/>); sem valor nem datas, <see cref="ImportacaoStatus.SemDados"/>.
    /// </summary>
    public async Task<ResultadoImportacao> ImportarDeArquivoAsync(
        string userId, FaturaExtraida dados, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);

        if (dados.Valor is null && dados.Vencimento is null && dados.Emissao is null)
        {
            return new ResultadoImportacao(null, ImportacaoStatus.SemDados, false, null);
        }

        var bill = await MatchBillAsync(userId, dados, ct);
        if (bill is null)
        {
            return new ResultadoImportacao(null, ImportacaoStatus.SemConta, false, null);
        }

        // Recua o início pela mesma data que define a competência (emissão, senão vencimento).
        var dataCompetencia = dados.Emissao ?? dados.Vencimento;
        var ajustou = dataCompetencia is { } data && await RecuarInicioContaSeAnteriorAsync(bill, data, ct);

        var (invoice, criada) = await PersistirFaturaAsync(userId, dados, bill, ct);
        return new ResultadoImportacao(
            invoice,
            criada ? ImportacaoStatus.Criada : ImportacaoStatus.Atualizada,
            ajustou,
            bill.Name);
    }

    /// <summary>Núcleo de persistência (dedupe por e-mail e por conta/competência); devolve se a fatura é nova.</summary>
    private async Task<(Invoice Invoice, bool Criada)> PersistirFaturaAsync(
        string userId, FaturaExtraida dados, Bill? bill, CancellationToken ct)
    {
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
                return (existentePorEmail, false);
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
                return (existentePorPeriodo, false);
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
        return (nova, true);
    }

    /// <summary>
    /// Recua a data de início da recorrência da conta para o 1º dia do mês da fatura quando esta é anterior ao
    /// início atual, preservando frequência, intervalo, dia de vencimento e fim. A geração automática só olha do
    /// mês corrente pra frente, então recuar o início não cria faturas retroativas — apenas torna a recorrência
    /// coerente com o histórico importado. Devolve se houve ajuste.
    /// </summary>
    private async Task<bool> RecuarInicioContaSeAnteriorAsync(Bill bill, DateOnly dataFatura, CancellationToken ct)
    {
        var regra = bill.Recurrence;
        if (dataFatura >= regra.StartDate)
        {
            return false;
        }

        var novoInicio = PrimeiroDiaDoMes(dataFatura);
        var nova = new RecurrenceRule(regra.Frequency, regra.Interval, regra.DueDay, novoInicio, regra.EndDate);
        bill.Edit(recurrence: nova);
        await db.SaveChangesAsync(ct);

        logger.LogInformation(
            "Data de início da conta '{Conta}' recuada de {De:yyyy-MM-dd} para {Para:yyyy-MM-dd} por fatura importada anterior.",
            bill.Name, regra.StartDate, novoInicio);

        return true;
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

    /// <summary>
    /// Quita a fatura: cria uma Transaction do mesmo tipo da conta (saída → despesa, entrada → receita)
    /// e liga-a à fatura (1:1). Faturas avulsas (sem conta) são tratadas como despesa.
    /// </summary>
    public async Task<Transaction> PagarAsync(Guid invoiceId, string userId, CancellationToken ct = default)
    {
        var invoice = await db.Invoices
                          .Include(i => i.Bill).ThenInclude(b => b!.Category)
                          .FirstOrDefaultAsync(
                              i => i.Id == invoiceId && i.UserId == userId && i.DeletedAt == null, ct)
                      ?? throw new InvalidOperationException("Fatura não encontrada.");

        if (invoice.Amount <= 0)
        {
            throw new InvalidOperationException("Valor da fatura ainda não reconhecido; ajuste o valor antes de pagar.");
        }

        // O tipo da transação segue a categoria da conta (entrada vira receita); avulsa cai em despesa.
        var tipo = invoice.Bill?.Category?.Type ?? ETransactionTypes.Expense;
        var categoria = invoice.Bill?.Category
                        ?? await categoryService.ResolveDefaultAsync(userId, tipo, ct);
        var titulo = $"Fatura {invoice.Bill?.Name ?? "avulsa"}";

        var transaction = new Transaction(userId, tipo, categoria, titulo, null, invoice.Amount);

        db.Transactions.Add(transaction);
        invoice.RegisterPayment(transaction.Id);
        await db.SaveChangesAsync(ct);

        return transaction;
    }

    /// <summary>Corrige manualmente valor/vencimento/emissão/situação de uma fatura ainda não paga.</summary>
    public async Task EditarAsync(
        Guid invoiceId,
        string userId,
        decimal amount,
        DateOnly dueDate,
        DateOnly? issueDate,
        EInvoiceStatus status,
        CancellationToken ct = default)
    {
        var invoice = await db.Invoices.FirstOrDefaultAsync(
                          i => i.Id == invoiceId && i.UserId == userId && i.DeletedAt == null, ct)
                      ?? throw new InvalidOperationException("Fatura não encontrada.");

        invoice.EditarManualmente(amount, dueDate, issueDate, status);
        await db.SaveChangesAsync(ct);
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
