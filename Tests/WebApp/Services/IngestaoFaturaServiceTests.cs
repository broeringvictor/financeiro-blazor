using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Services.Pdf;
using WebApp.Data;
using WebApp.Models;
using WebApp.Models.Enums;
using WebApp.Models.ValueObjects;
using WebApp.Services;

namespace Tests.WebApp.Services;

public class IngestaoFaturaServiceTests
{
    private const string UserId = "user-123";

    private static IngestaoFaturaService NovoServico(ApplicationDbContext db) =>
        new(db, NullLogger<IngestaoFaturaService>.Instance);

    private static (ApplicationDbContext db, SqliteConnection conn) NovoContexto()
    {
        var conn = new SqliteConnection("DataSource=:memory:");
        conn.Open();

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(conn)
            .Options;

        var db = new ApplicationDbContext(options);
        db.Database.EnsureCreated();

        db.Users.Add(new ApplicationUser { Id = UserId, UserName = "u", Email = "u@x.com" });
        db.SaveChanges();

        return (db, conn);
    }

    private static Bill CriarContaCelesc() =>
        new(UserId, "Luz - Celesc", "Celesc", ETransactionCategory.Utilities,
            new RecurrenceRule(ERecurrenceFrequency.Monthly, 1, 10, new DateOnly(2026, 1, 10)),
            senderContains: "celesc.com.br");

    private static FaturaExtraida DadosCelesc(string? messageId = "msg-1") =>
        new("Celesc", 187.42m, new DateOnly(2026, 6, 28), new DateOnly(2026, 7, 10), messageId, @"C:\tmp\celesc.pdf", "texto");

    [Fact]
    public async Task UpsertAsync_CriaNovaFatura()
    {
        var (db, conn) = NovoContexto();
        await using var _ = db;
        using var __ = conn;
        var sut = NovoServico(db);

        var invoice = await sut.UpsertAsync(UserId, DadosCelesc());

        Assert.Equal(1, await db.Invoices.CountAsync());
        Assert.Equal(187.42m, invoice.Amount);
        Assert.Equal(new DateOnly(2026, 6, 1), invoice.ReferenceMonth);
        Assert.Equal(EInvoiceStatus.Pending, invoice.Status);
    }

    [Fact]
    public async Task UpsertAsync_MesmoEmail_NaoDuplica()
    {
        var (db, conn) = NovoContexto();
        await using var _ = db;
        using var __ = conn;
        var sut = NovoServico(db);

        await sut.UpsertAsync(UserId, DadosCelesc());
        await sut.UpsertAsync(UserId, DadosCelesc() with { Valor = 200m });

        Assert.Equal(1, await db.Invoices.CountAsync());
        var invoice = await db.Invoices.SingleAsync();
        Assert.Equal(200m, invoice.Amount); // atualizou
    }

    [Fact]
    public async Task UpsertAsync_AssociaBillPeloFornecedor()
    {
        var (db, conn) = NovoContexto();
        await using var _ = db;
        using var __ = conn;
        var bill = CriarContaCelesc();
        db.Bills.Add(bill);
        await db.SaveChangesAsync();
        var sut = NovoServico(db);

        var invoice = await sut.UpsertAsync(UserId, DadosCelesc());

        Assert.Equal(bill.Id, invoice.BillId);
    }

    [Fact]
    public async Task UpsertAsync_MesmaContaECompetencia_SemEmail_NaoDuplica()
    {
        var (db, conn) = NovoContexto();
        await using var _ = db;
        using var __ = conn;
        db.Bills.Add(CriarContaCelesc());
        await db.SaveChangesAsync();
        var sut = NovoServico(db);

        await sut.UpsertAsync(UserId, DadosCelesc(messageId: null));
        await sut.UpsertAsync(UserId, DadosCelesc(messageId: null) with { Valor = 250m });

        Assert.Equal(1, await db.Invoices.CountAsync());
        Assert.Equal(250m, (await db.Invoices.SingleAsync()).Amount);
    }

    [Fact]
    public async Task UpsertAsync_ContaValorFixo_ValorExtraidoDivergente_FicaNaoReconhecido()
    {
        var (db, conn) = NovoContexto();
        await using var _ = db;
        using var __ = conn;
        db.Bills.Add(new Bill(
            UserId, "Aluguel", "Imobiliária X", ETransactionCategory.Rent,
            new RecurrenceRule(ERecurrenceFrequency.Monthly, 1, 5, new DateOnly(2026, 1, 5)),
            fixedAmount: 1500m,
            senderContains: "imobiliariax.com.br"));
        await db.SaveChangesAsync();
        var sut = NovoServico(db);

        var invoice = await sut.UpsertAsync(UserId, DadosCelesc() with
        {
            BillerName = "Imobiliária X",
            Valor = 187.42m,
        });

        Assert.Equal(0m, invoice.Amount);
    }

    [Fact]
    public async Task UpsertAsync_ContaValorFixo_ValorExtraidoBate_Confirma()
    {
        var (db, conn) = NovoContexto();
        await using var _ = db;
        using var __ = conn;
        db.Bills.Add(new Bill(
            UserId, "Aluguel", "Imobiliária X", ETransactionCategory.Rent,
            new RecurrenceRule(ERecurrenceFrequency.Monthly, 1, 5, new DateOnly(2026, 1, 5)),
            fixedAmount: 1500m,
            senderContains: "imobiliariax.com.br"));
        await db.SaveChangesAsync();
        var sut = NovoServico(db);

        var invoice = await sut.UpsertAsync(UserId, DadosCelesc() with
        {
            BillerName = "Imobiliária X",
            Valor = 1500m,
        });

        Assert.Equal(1500m, invoice.Amount);
    }

    [Fact]
    public async Task PagarAsync_CriaTransactionEMarcaPaga()
    {
        var (db, conn) = NovoContexto();
        await using var _ = db;
        using var __ = conn;
        db.Bills.Add(CriarContaCelesc());
        await db.SaveChangesAsync();
        var sut = NovoServico(db);
        var invoice = await sut.UpsertAsync(UserId, DadosCelesc());

        var tx = await sut.PagarAsync(invoice.Id, UserId);

        Assert.Equal(ETransactionTypes.Expense, tx.Type);
        Assert.Equal(ETransactionCategory.Utilities, tx.Category);
        Assert.Equal(187.42m, tx.Amount);

        var atualizada = await db.Invoices.SingleAsync();
        Assert.Equal(EInvoiceStatus.Paid, atualizada.Status);
        Assert.Equal(tx.Id, atualizada.PaymentTransactionId);
    }
}
