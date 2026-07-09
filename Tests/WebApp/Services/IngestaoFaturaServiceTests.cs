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
        new(db,
            new CategoryService(db, NullLogger<CategoryService>.Instance),
            NullLogger<IngestaoFaturaService>.Instance);

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

    // Adiciona uma categoria de despesa ao contexto (ainda não salva) e devolve-a.
    private static Category NovaCategoria(ApplicationDbContext db, string nome = "Contas")
    {
        var categoria = new Category(UserId, nome, ETransactionTypes.Expense);
        db.Categories.Add(categoria);
        return categoria;
    }

    private static Bill CriarContaCelesc(ApplicationDbContext db) =>
        new(UserId, "Luz - Celesc", "Celesc", NovaCategoria(db),
            new RecurrenceRule(ERecurrenceFrequency.Monthly, 1, 10, new DateOnly(2026, 1, 10)));

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
        var bill = CriarContaCelesc(db);
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
        db.Bills.Add(CriarContaCelesc(db));
        await db.SaveChangesAsync();
        var sut = NovoServico(db);

        await sut.UpsertAsync(UserId, DadosCelesc(messageId: null));
        await sut.UpsertAsync(UserId, DadosCelesc(messageId: null) with { Valor = 250m });

        Assert.Equal(1, await db.Invoices.CountAsync());
        Assert.Equal(250m, (await db.Invoices.SingleAsync()).Amount);
    }

    [Fact]
    public async Task UpsertAsync_ContaValorFixo_ValorExtraidoDivergente_UsaValorExtraido()
    {
        var (db, conn) = NovoContexto();
        await using var _ = db;
        using var __ = conn;
        db.Bills.Add(new Bill(
            UserId, "Aluguel", "Imobiliária X", NovaCategoria(db, "Aluguel"),
            new RecurrenceRule(ERecurrenceFrequency.Monthly, 1, 5, new DateOnly(2026, 1, 5)),
            fixedAmount: 1500m));
        await db.SaveChangesAsync();
        var sut = NovoServico(db);

        var invoice = await sut.UpsertAsync(UserId, DadosCelesc() with
        {
            BillerName = "Imobiliária X",
            Valor = 187.42m,
        });

        // O valor extraído da fatura sempre prevalece sobre o FixedAmount, mesmo divergindo dele.
        Assert.Equal(187.42m, invoice.Amount);
    }

    [Fact]
    public async Task UpsertAsync_ContaValorFixo_ValorExtraidoBate_Confirma()
    {
        var (db, conn) = NovoContexto();
        await using var _ = db;
        using var __ = conn;
        db.Bills.Add(new Bill(
            UserId, "Aluguel", "Imobiliária X", NovaCategoria(db, "Aluguel"),
            new RecurrenceRule(ERecurrenceFrequency.Monthly, 1, 5, new DateOnly(2026, 1, 5)),
            fixedAmount: 1500m));
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
        db.Bills.Add(CriarContaCelesc(db));
        await db.SaveChangesAsync();
        var sut = NovoServico(db);
        var invoice = await sut.UpsertAsync(UserId, DadosCelesc());

        var tx = await sut.PagarAsync(invoice.Id, UserId);

        Assert.Equal(ETransactionTypes.Expense, tx.Type);
        // A transação herda a categoria da conta associada à fatura.
        var bill = await db.Bills.FirstAsync();
        Assert.Equal(bill.CategoryId, tx.CategoryId);
        Assert.Equal(187.42m, tx.Amount);

        var atualizada = await db.Invoices.SingleAsync();
        Assert.Equal(EInvoiceStatus.Paid, atualizada.Status);
        Assert.Equal(tx.Id, atualizada.PaymentTransactionId);
    }

    [Fact]
    public async Task PagarAsync_FaturaDeEntrada_CriaTransacaoDeReceita()
    {
        var (db, conn) = NovoContexto();
        await using var _ = db;
        using var __ = conn;

        // Conta de entrada (categoria de receita) com uma fatura pendente.
        var categoria = new Category(UserId, "Salário", ETransactionTypes.Income);
        db.Categories.Add(categoria);
        var entrada = new Bill(UserId, "Salário", "Empresa X", categoria,
            new RecurrenceRule(ERecurrenceFrequency.Monthly, 1, 5, new DateOnly(2026, 1, 5)));
        db.Bills.Add(entrada);
        var fatura = new Invoice(UserId, entrada.Id, new DateOnly(2026, 7, 1), 5000m, new DateOnly(2026, 7, 5));
        db.Invoices.Add(fatura);
        await db.SaveChangesAsync();
        var sut = NovoServico(db);

        var tx = await sut.PagarAsync(fatura.Id, UserId);

        Assert.Equal(ETransactionTypes.Income, tx.Type);
        Assert.Equal(categoria.Id, tx.CategoryId);
        Assert.Equal(5000m, tx.Amount);
    }

    [Fact]
    public async Task ImportarDeArquivo_FaturaAnteriorAoInicio_CriaERecuaInicioDaConta()
    {
        var (db, conn) = NovoContexto();
        await using var _ = db;
        using var __ = conn;
        db.Bills.Add(CriarContaCelesc(db)); // início em 2026-01-10
        await db.SaveChangesAsync();
        var sut = NovoServico(db);

        // Fatura de dezembro/2025, anterior ao início da conta.
        var dados = new FaturaExtraida("Celesc", 187.42m,
            new DateOnly(2025, 12, 5), new DateOnly(2025, 12, 15), null, null, "Fatura Celesc energia");

        var resultado = await sut.ImportarDeArquivoAsync(UserId, dados);

        Assert.Equal(ImportacaoStatus.Criada, resultado.Status);
        Assert.True(resultado.AjustouInicioConta);
        Assert.Equal("Luz - Celesc", resultado.Conta);

        var bill = await db.Bills.FirstAsync();
        Assert.Equal(new DateOnly(2025, 12, 1), bill.Recurrence.StartDate); // recuado para o mês da fatura
        Assert.Equal(1, await db.Invoices.CountAsync());
    }

    [Fact]
    public async Task ImportarDeArquivo_FaturaDentroDoPeriodo_NaoRecuaInicio()
    {
        var (db, conn) = NovoContexto();
        await using var _ = db;
        using var __ = conn;
        db.Bills.Add(CriarContaCelesc(db));
        await db.SaveChangesAsync();
        var sut = NovoServico(db);

        var dados = new FaturaExtraida("Celesc", 200m,
            new DateOnly(2026, 3, 5), new DateOnly(2026, 3, 15), null, null, "Fatura Celesc");

        var resultado = await sut.ImportarDeArquivoAsync(UserId, dados);

        Assert.Equal(ImportacaoStatus.Criada, resultado.Status);
        Assert.False(resultado.AjustouInicioConta);

        var bill = await db.Bills.FirstAsync();
        Assert.Equal(new DateOnly(2026, 1, 10), bill.Recurrence.StartDate); // inalterado
    }

    [Fact]
    public async Task ImportarDeArquivo_ComCompetenciaDaReferencia_DefineCompetenciaDaFatura()
    {
        var (db, conn) = NovoContexto();
        await using var _ = db;
        using var __ = conn;
        db.Bills.Add(CriarContaCelesc(db));
        await db.SaveChangesAsync();
        var sut = NovoServico(db);

        // Emissão/vencimento em julho, mas a referência do boleto é junho → a fatura deve ficar em junho.
        var dados = new FaturaExtraida("Celesc", 361.81m,
            new DateOnly(2026, 7, 15), new DateOnly(2026, 7, 15), null, null, "Fatura Celesc",
            Competencia: new DateOnly(2026, 6, 1));

        var resultado = await sut.ImportarDeArquivoAsync(UserId, dados);

        Assert.Equal(ImportacaoStatus.Criada, resultado.Status);
        var invoice = await db.Invoices.SingleAsync();
        Assert.Equal(new DateOnly(2026, 6, 1), invoice.ReferenceMonth);
    }

    [Fact]
    public async Task ImportarDeArquivo_SemContaCorrespondente_NaoCriaFatura()
    {
        var (db, conn) = NovoContexto();
        await using var _ = db;
        using var __ = conn;
        db.Bills.Add(CriarContaCelesc(db));
        await db.SaveChangesAsync();
        var sut = NovoServico(db);

        var dados = new FaturaExtraida("Vivo", 90m,
            new DateOnly(2026, 2, 5), new DateOnly(2026, 2, 15), null, null, "Fatura Vivo telefonia");

        var resultado = await sut.ImportarDeArquivoAsync(UserId, dados);

        Assert.Equal(ImportacaoStatus.SemConta, resultado.Status);
        Assert.Null(resultado.Invoice);
        Assert.Equal(0, await db.Invoices.CountAsync());
    }

    [Fact]
    public async Task ImportarDeArquivo_ClassificaPeloTextoDoPdf_QuandoSemFornecedor()
    {
        var (db, conn) = NovoContexto();
        await using var _ = db;
        using var __ = conn;
        db.Bills.Add(CriarContaCelesc(db));
        await db.SaveChangesAsync();
        var sut = NovoServico(db);

        // Sem BillerName; o nome da conta ("Celesc") aparece no texto do PDF.
        var dados = new FaturaExtraida(null, 150m,
            new DateOnly(2026, 4, 5), new DateOnly(2026, 4, 15), null, null,
            "COMPANHIA DE ENERGIA - CELESC DISTRIBUICAO S.A. - fatura mensal");

        var resultado = await sut.ImportarDeArquivoAsync(UserId, dados);

        Assert.Equal(ImportacaoStatus.Criada, resultado.Status);
        Assert.Equal("Luz - Celesc", resultado.Conta);
    }

    [Fact]
    public async Task ImportarDeArquivo_MesmaCompetencia_AtualizaSemDuplicar()
    {
        var (db, conn) = NovoContexto();
        await using var _ = db;
        using var __ = conn;
        db.Bills.Add(CriarContaCelesc(db));
        await db.SaveChangesAsync();
        var sut = NovoServico(db);

        var dados = new FaturaExtraida("Celesc", 100m,
            new DateOnly(2026, 5, 5), new DateOnly(2026, 5, 15), null, null, "Fatura Celesc");

        var primeira = await sut.ImportarDeArquivoAsync(UserId, dados);
        var segunda = await sut.ImportarDeArquivoAsync(UserId, dados with { Valor = 123m });

        Assert.Equal(ImportacaoStatus.Criada, primeira.Status);
        Assert.Equal(ImportacaoStatus.Atualizada, segunda.Status);
        Assert.Equal(1, await db.Invoices.CountAsync());
        Assert.Equal(123m, (await db.Invoices.SingleAsync()).Amount);
    }
}
