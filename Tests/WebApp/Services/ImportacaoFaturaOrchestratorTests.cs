using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Services.Agents;
using Services.Pdf;
using WebApp.Data;
using WebApp.Models;
using WebApp.Models.Enums;
using WebApp.Models.ValueObjects;
using WebApp.Services;

namespace Tests.WebApp.Services;

public class ImportacaoFaturaOrchestratorTests
{
    private const string UserId = "user-123";

    [Fact]
    public async Task Importar_IaAprova_AnexaFaturaNaConta()
    {
        var (db, conn) = NovoContexto();
        await using var _ = db;
        using var __ = conn;
        var bill = CriarContaCelesc(db);
        db.Bills.Add(bill);
        await db.SaveChangesAsync();

        var leitor = new FakeLeitor(new FaturaInfo(187.42m, new DateOnly(2026, 3, 5), new DateOnly(2026, 3, 15)), "Fatura Celesc");
        var supervisor = new FakeSupervisor(ctx => new ClassificacaoSupervisionada(ctx.ContaSugeridaId, true, false, "confiante"));
        var orquestrador = NovoOrquestrador(db, leitor, supervisor);

        var resultado = await orquestrador.ImportarAsync(UserId, ["boleto.pdf"]);

        Assert.Equal(1, resultado.Anexadas);
        Assert.Equal(0, resultado.ParaRevisao);
        var invoice = await db.Invoices.SingleAsync();
        Assert.Equal(bill.Id, invoice.BillId);
        Assert.Equal(187.42m, invoice.Amount);
    }

    [Fact]
    public async Task Importar_IaPedeRevisao_SalvaAvulsaEMarcaPendente()
    {
        var (db, conn) = NovoContexto();
        await using var _ = db;
        using var __ = conn;
        db.Bills.Add(CriarContaCelesc(db));
        await db.SaveChangesAsync();

        var leitor = new FakeLeitor(new FaturaInfo(90m, new DateOnly(2026, 2, 5), new DateOnly(2026, 2, 15)), "documento ambíguo");
        var supervisor = new FakeSupervisor(_ => new ClassificacaoSupervisionada(null, false, true, "não identifiquei a conta"));
        var orquestrador = NovoOrquestrador(db, leitor, supervisor);

        var resultado = await orquestrador.ImportarAsync(UserId, ["misterio.pdf"]);

        Assert.Equal(0, resultado.Anexadas);
        Assert.Equal(1, resultado.ParaRevisao);
        Assert.Single(resultado.Pendentes);

        var invoice = await db.Invoices.SingleAsync();
        Assert.Null(invoice.BillId); // salva como avulsa, nada se perde
    }

    [Fact]
    public async Task Importar_FaturaAnteriorAoInicio_RecuaInicioDaConta()
    {
        var (db, conn) = NovoContexto();
        await using var _ = db;
        using var __ = conn;
        var bill = CriarContaCelesc(db); // início 2026-01-10
        db.Bills.Add(bill);
        await db.SaveChangesAsync();

        var leitor = new FakeLeitor(new FaturaInfo(200m, new DateOnly(2025, 11, 5), new DateOnly(2025, 11, 15)), "Fatura Celesc");
        var supervisor = new FakeSupervisor(ctx => new ClassificacaoSupervisionada(ctx.ContaSugeridaId, true, false, "ok"));
        var orquestrador = NovoOrquestrador(db, leitor, supervisor);

        await orquestrador.ImportarAsync(UserId, ["boleto-antigo.pdf"]);

        var atualizada = await db.Bills.FirstAsync();
        Assert.Equal(new DateOnly(2025, 11, 1), atualizada.Recurrence.StartDate);
    }

    private sealed class FakeLeitor(FaturaInfo info, string? texto) : IFaturaLeitorPdf
    {
        public FaturaInfo ExtrairDadosFatura(string caminhoPdf) => info;
        public string ObterTextoBruto(string caminhoPdf) => texto ?? string.Empty;
    }

    private sealed class FakeSupervisor(Func<ContextoClassificacao, ClassificacaoSupervisionada> decisao)
        : IFaturaClassificadorSupervisor
    {
        public Task<ClassificacaoSupervisionada> RevisarAsync(ContextoClassificacao contexto, CancellationToken ct = default) =>
            Task.FromResult(decisao(contexto));
    }

    private static ImportacaoFaturaOrchestrator NovoOrquestrador(
        ApplicationDbContext db, IFaturaLeitorPdf leitor, IFaturaClassificadorSupervisor supervisor)
    {
        var ingestao = new IngestaoFaturaService(
            db, new CategoryService(db, NullLogger<CategoryService>.Instance), NullLogger<IngestaoFaturaService>.Instance);
        return new ImportacaoFaturaOrchestrator(db, leitor, supervisor, ingestao, NullLogger<ImportacaoFaturaOrchestrator>.Instance);
    }

    private static Bill CriarContaCelesc(ApplicationDbContext db)
    {
        var categoria = new Category(UserId, "Energia", ETransactionTypes.Expense);
        db.Categories.Add(categoria);
        return new Bill(UserId, "Luz - Celesc", "Celesc", categoria,
            new RecurrenceRule(ERecurrenceFrequency.Monthly, 1, 10, new DateOnly(2026, 1, 10)));
    }

    private static (ApplicationDbContext db, SqliteConnection conn) NovoContexto()
    {
        var conn = new SqliteConnection("DataSource=:memory:");
        conn.Open();

        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(conn).Options;
        var db = new ApplicationDbContext(options);
        db.Database.EnsureCreated();

        db.Users.Add(new ApplicationUser { Id = UserId, UserName = "u", Email = "u@x.com" });
        db.SaveChanges();

        return (db, conn);
    }
}
