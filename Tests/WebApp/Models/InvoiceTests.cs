using WebApp.Models;
using WebApp.Models.Enums;

namespace Tests.WebApp.Models;

public class InvoiceTests
{
    private const string UserId = "user-123";

    private static Invoice CriarFatura(decimal amount = 187.42m, DateOnly? due = null) =>
        new(UserId, billId: Guid.CreateVersion7(),
            referenceMonth: new DateOnly(2026, 6, 15),
            amount: amount,
            dueDate: due ?? new DateOnly(2026, 7, 10),
            issueDate: new DateOnly(2026, 6, 28),
            sourceEmailMessageId: "msg-1",
            pdfPath: @"C:\tmp\fatura.pdf");

    [Fact]
    public void Create_NormalizaCompetenciaParaPrimeiroDia()
    {
        var invoice = CriarFatura();

        Assert.Equal(new DateOnly(2026, 6, 1), invoice.ReferenceMonth);
        Assert.Equal(EInvoiceStatus.Pending, invoice.Status);
        Assert.Equal("msg-1", invoice.SourceEmailMessageId);
    }

    [Fact]
    public void Create_ComValorNegativo_DeveLancar()
    {
        Assert.Throws<ArgumentException>(() => CriarFatura(amount: -5m));
    }

    [Fact]
    public void RegisterPayment_TornaPagaEGuardaTransacao()
    {
        var invoice = CriarFatura();
        var txId = Guid.CreateVersion7();

        invoice.RegisterPayment(txId);

        Assert.Equal(EInvoiceStatus.Paid, invoice.Status);
        Assert.Equal(txId, invoice.PaymentTransactionId);
        Assert.NotNull(invoice.PaidAt);
    }

    [Fact]
    public void RegisterPayment_EmFaturaJaPaga_DeveLancar()
    {
        var invoice = CriarFatura();
        invoice.RegisterPayment(Guid.CreateVersion7());

        Assert.Throws<InvalidOperationException>(() => invoice.RegisterPayment(Guid.CreateVersion7()));
    }

    [Fact]
    public void Cancel_EmFaturaPaga_DeveLancar()
    {
        var invoice = CriarFatura();
        invoice.RegisterPayment(Guid.CreateVersion7());

        Assert.Throws<InvalidOperationException>(invoice.Cancel);
    }

    [Fact]
    public void IsOverdue_PendenteEVencida_DeveSerVerdadeiro()
    {
        var invoice = CriarFatura(due: DateOnly.FromDateTime(DateTime.Today).AddDays(-1));

        Assert.True(invoice.IsOverdue);
    }

    [Fact]
    public void IsOverdue_Paga_DeveSerFalso()
    {
        var invoice = CriarFatura(due: DateOnly.FromDateTime(DateTime.Today).AddDays(-1));
        invoice.RegisterPayment(Guid.CreateVersion7());

        Assert.False(invoice.IsOverdue);
    }

    [Fact]
    public void UpdateFromExtraction_AtualizaValorEVencimento()
    {
        var invoice = CriarFatura(amount: 0m);

        invoice.UpdateFromExtraction(amount: 200m, dueDate: new DateOnly(2026, 7, 15));

        Assert.Equal(200m, invoice.Amount);
        Assert.Equal(new DateOnly(2026, 7, 15), invoice.DueDate);
    }
}
