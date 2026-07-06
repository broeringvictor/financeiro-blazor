using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using WebApp.Data;
using WebApp.Models;
using WebApp.Models.Enums;
using WebApp.Models.ValueObjects;
using WebApp.Services;

namespace Tests.WebApp.Services;

public class GeracaoFaturaServiceTests
{
    private const string UserId = "user-123";

    private static GeracaoFaturaService NovoServico(ApplicationDbContext db, GeracaoFaturaOptions? options = null) =>
        new(db, options ?? new GeracaoFaturaOptions(), NullLogger<GeracaoFaturaService>.Instance);

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

    private static Bill ContaMensal(
        DateOnly? endDate = null,
        decimal? fixedAmount = null,
        int dueDay = 10)
    {
        // StartDate no passado garante ocorrências a partir do mês corrente independentemente da data do teste.
        var start = new DateOnly(2020, 1, dueDay);
        return new Bill(
            UserId, "Aluguel", "Imobiliária X", ETransactionCategory.Rent,
            new RecurrenceRule(ERecurrenceFrequency.Monthly, 1, dueDay, start, endDate),
            fixedAmount: fixedAmount);
    }

    [Fact]
    public async Task GerarPendentes_SemPrazo_CriaTresFaturasPending()
    {
        var (db, conn) = NovoContexto();
        await using var _ = db;
        using var __ = conn;
        var bill = ContaMensal();
        db.Bills.Add(bill);
        await db.SaveChangesAsync();
        var sut = NovoServico(db);

        var criadas = await sut.GerarPendentesAsync(UserId, bill);

        Assert.Equal(3, criadas);
        Assert.Equal(3, await db.Invoices.CountAsync());
        Assert.All(await db.Invoices.ToListAsync(), i => Assert.Equal(EInvoiceStatus.Pending, i.Status));
        // Competências distintas e consecutivas a partir do mês corrente.
        var refs = await db.Invoices.Select(i => i.ReferenceMonth).OrderBy(r => r).ToListAsync();
        Assert.Equal(3, refs.Distinct().Count());
    }

    [Fact]
    public async Task GerarPendentes_SemPrazo_Idempotente()
    {
        var (db, conn) = NovoContexto();
        await using var _ = db;
        using var __ = conn;
        var bill = ContaMensal();
        db.Bills.Add(bill);
        await db.SaveChangesAsync();
        var sut = NovoServico(db);

        await sut.GerarPendentesAsync(UserId, bill);
        var segundaRodada = await sut.GerarPendentesAsync(UserId, bill);

        Assert.Equal(0, segundaRodada);
        Assert.Equal(3, await db.Invoices.CountAsync());
    }

    [Fact]
    public async Task GerarPendentes_ComPrazo_CriaTodasAteEndDate()
    {
        var (db, conn) = NovoContexto();
        await using var _ = db;
        using var __ = conn;
        // Prazo de 2 meses a partir do mês corrente => 3 competências (mês atual + 2).
        var fim = new DateOnly(DateTime.Today.Year, DateTime.Today.Month, 10).AddMonths(2);
        var bill = ContaMensal(endDate: fim);
        db.Bills.Add(bill);
        await db.SaveChangesAsync();
        var sut = NovoServico(db);

        var criadas = await sut.GerarPendentesAsync(UserId, bill);

        Assert.Equal(3, criadas);
        var ultimaCompetencia = await db.Invoices.MaxAsync(i => i.ReferenceMonth);
        Assert.Equal(new DateOnly(fim.Year, fim.Month, 1), ultimaCompetencia);
    }

    [Fact]
    public async Task GerarPendentes_SemValorFixo_CriaComAmountZero()
    {
        var (db, conn) = NovoContexto();
        await using var _ = db;
        using var __ = conn;
        var bill = ContaMensal(fixedAmount: null);
        db.Bills.Add(bill);
        await db.SaveChangesAsync();
        var sut = NovoServico(db);

        await sut.GerarPendentesAsync(UserId, bill);

        Assert.All(await db.Invoices.ToListAsync(), i => Assert.Equal(0m, i.Amount));
    }

    [Fact]
    public async Task GerarPendentes_ComValorFixo_UsaFixedAmount()
    {
        var (db, conn) = NovoContexto();
        await using var _ = db;
        using var __ = conn;
        var bill = ContaMensal(fixedAmount: 1500m);
        db.Bills.Add(bill);
        await db.SaveChangesAsync();
        var sut = NovoServico(db);

        await sut.GerarPendentesAsync(UserId, bill);

        Assert.All(await db.Invoices.ToListAsync(), i => Assert.Equal(1500m, i.Amount));
    }

    [Fact]
    public async Task GerarPendentes_ComFaturaExistenteNaCompetencia_NaoDuplica()
    {
        var (db, conn) = NovoContexto();
        await using var _ = db;
        using var __ = conn;
        var bill = ContaMensal(fixedAmount: 1500m);
        db.Bills.Add(bill);

        // Fatura já existente no mês corrente (ex.: vinda de e-mail), com valor próprio.
        var mesAtual = new DateOnly(DateTime.Today.Year, DateTime.Today.Month, 1);
        var venc = new DateOnly(DateTime.Today.Year, DateTime.Today.Month, 10);
        db.Invoices.Add(new Invoice(UserId, bill.Id, mesAtual, 999m, venc));
        await db.SaveChangesAsync();
        var sut = NovoServico(db);

        var criadas = await sut.GerarPendentesAsync(UserId, bill);

        // Buffer de 3: já havia 1, então cria só mais 2; a existente permanece intacta.
        Assert.Equal(2, criadas);
        Assert.Equal(3, await db.Invoices.CountAsync());
        var doMesAtual = await db.Invoices.SingleAsync(i => i.ReferenceMonth == mesAtual);
        Assert.Equal(999m, doMesAtual.Amount);
    }

    [Fact]
    public async Task GerarPendentes_ContaInativa_NaoGera()
    {
        var (db, conn) = NovoContexto();
        await using var _ = db;
        using var __ = conn;
        var bill = ContaMensal();
        bill.Deactivate();
        db.Bills.Add(bill);
        await db.SaveChangesAsync();
        var sut = NovoServico(db);

        var criadas = await sut.GerarPendentesAsync(UserId, bill);

        Assert.Equal(0, criadas);
        Assert.Equal(0, await db.Invoices.CountAsync());
    }

    [Fact]
    public async Task GerarPendentes_RecorrenciaEncerrada_NaoGera()
    {
        var (db, conn) = NovoContexto();
        await using var _ = db;
        using var __ = conn;
        // EndDate no passado: recorrência já terminou.
        var bill = ContaMensal(endDate: new DateOnly(2021, 1, 10));
        db.Bills.Add(bill);
        await db.SaveChangesAsync();
        var sut = NovoServico(db);

        var criadas = await sut.GerarPendentesAsync(UserId, bill);

        Assert.Equal(0, criadas);
        Assert.Equal(0, await db.Invoices.CountAsync());
    }
}
