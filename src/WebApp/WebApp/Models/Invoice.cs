using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using WebApp.Models.Enums;
using WebApp.Models.Shared;

namespace WebApp.Models;

/// <summary>
/// Fatura: cobrança concreta de um período (competência), normalmente originada de um e-mail.
/// O pagamento (1:1) é uma <see cref="Transaction"/> referenciada por <see cref="PaymentTransactionId"/>.
/// </summary>
public class Invoice : BaseModel
{
    /// <summary>Id do usuário dono da fatura (FK para AspNetUsers).</summary>
    [Required]
    public string UserId { get; private set; } = string.Empty;

    /// <summary>Conta recorrente associada (null para fatura avulsa).</summary>
    public Guid? BillId { get; private set; }

    /// <summary>Navegação para a conta recorrente (N:1, lado inverso de <see cref="Bill.Invoices"/>).</summary>
    public Bill? Bill { get; private set; }

    /// <summary>Competência (1º dia do mês de referência).</summary>
    [DisplayName("Competência")]
    public DateOnly ReferenceMonth { get; private set; }

    /// <summary>Valor da fatura (0 = ainda desconhecido até a extração).</summary>
    [DisplayName("Valor")]
    public decimal Amount { get; private set; }

    [DisplayName("Emissão")]
    public DateOnly? IssueDate { get; private set; }

    [DisplayName("Vencimento")]
    public DateOnly DueDate { get; private set; }

    [DisplayName("Situação")]
    public EInvoiceStatus Status { get; private set; } = EInvoiceStatus.Pending;

    /// <summary>Id da mensagem do Gmail de origem (dedupe; índice único filtrado).</summary>
    public string? SourceEmailMessageId { get; private set; }

    /// <summary>Caminho do PDF baixado.</summary>
    public string? PdfPath { get; private set; }

    /// <summary>Conteúdo bruto extraído do PDF (texto/JSON), para auditoria/reprocessamento.</summary>
    public string? ExtractionRaw { get; private set; }

    /// <summary>Transação de pagamento (1:1).</summary>
    public Guid? PaymentTransactionId { get; private set; }

    public DateTime? PaidAt { get; private set; }

    /// <summary>Vencida = pendente e já passou do vencimento (derivado, não persistido).</summary>
    [NotMapped]
    public bool IsOverdue =>
        Status == EInvoiceStatus.Pending && DueDate < DateOnly.FromDateTime(DateTime.Today);

    /// <summary>Construtor usado pelo Entity Framework.</summary>
    private Invoice() { }

    public Invoice(
        string userId,
        Guid? billId,
        DateOnly referenceMonth,
        decimal amount,
        DateOnly dueDate,
        DateOnly? issueDate = null,
        string? sourceEmailMessageId = null,
        string? pdfPath = null,
        string? extractionRaw = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        EnsureAmountValid(amount);

        UserId = userId;
        BillId = billId;
        ReferenceMonth = NormalizeReference(referenceMonth);
        Amount = amount;
        DueDate = dueDate;
        IssueDate = issueDate;
        SourceEmailMessageId = string.IsNullOrWhiteSpace(sourceEmailMessageId) ? null : sourceEmailMessageId.Trim();
        PdfPath = string.IsNullOrWhiteSpace(pdfPath) ? null : pdfPath.Trim();
        ExtractionRaw = extractionRaw;
    }

    /// <summary>Reaplica os dados extraídos (re-ingestão idempotente do mesmo e-mail).</summary>
    public void UpdateFromExtraction(
        decimal? amount = null,
        DateOnly? dueDate = null,
        DateOnly? issueDate = null,
        string? pdfPath = null,
        string? extractionRaw = null)
    {
        var hasChanges = false;

        if (amount is { } newAmount && newAmount != Amount)
        {
            EnsureAmountValid(newAmount);
            Amount = newAmount;
            hasChanges = true;
        }

        if (dueDate is { } newDue && newDue != DueDate)
        {
            DueDate = newDue;
            hasChanges = true;
        }

        if (issueDate is { } newIssue && newIssue != IssueDate)
        {
            IssueDate = newIssue;
            hasChanges = true;
        }

        if (!string.IsNullOrWhiteSpace(pdfPath) && PdfPath != pdfPath.Trim())
        {
            PdfPath = pdfPath.Trim();
            hasChanges = true;
        }

        if (extractionRaw is not null && ExtractionRaw != extractionRaw)
        {
            ExtractionRaw = extractionRaw;
            hasChanges = true;
        }

        if (hasChanges)
        {
            MarkAsUpdated();
        }
    }

    /// <summary>Registra o pagamento ligando a fatura a uma <see cref="Transaction"/> (1:1).</summary>
    public void RegisterPayment(Guid transactionId)
    {
        if (Status != EInvoiceStatus.Pending)
        {
            throw new InvalidOperationException($"Só é possível pagar faturas pendentes (situação atual: {Status}).");
        }

        Status = EInvoiceStatus.Paid;
        PaymentTransactionId = transactionId;
        PaidAt = DateTime.UtcNow;
        MarkAsUpdated();
    }

    public void Cancel()
    {
        if (Status == EInvoiceStatus.Paid)
        {
            throw new InvalidOperationException("Não é possível cancelar uma fatura já paga.");
        }

        if (Status != EInvoiceStatus.Canceled)
        {
            Status = EInvoiceStatus.Canceled;
            MarkAsUpdated();
        }
    }

    /// <summary>Exclusão lógica. Bloqueada para faturas já pagas (ficaria uma Transaction órfã, sem fatura/recibo).</summary>
    public void Delete()
    {
        if (Status == EInvoiceStatus.Paid)
        {
            throw new InvalidOperationException(
                "Não é possível excluir uma fatura já paga (a transação de pagamento ficaria sem referência).");
        }

        MarkAsDeleted();
    }

    private static DateOnly NormalizeReference(DateOnly reference) =>
        new(reference.Year, reference.Month, 1);

    private static void EnsureAmountValid(decimal amount)
    {
        if (amount < 0)
        {
            throw new ArgumentException("O valor da fatura não pode ser negativo.", nameof(amount));
        }
    }
}
