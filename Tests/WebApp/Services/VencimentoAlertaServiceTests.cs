using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Services.WhatsApp;
using WebApp.Data;
using WebApp.Models;
using WebApp.Models.Enums;
using WebApp.Models.ValueObjects;
using WebApp.Services;

namespace Tests.WebApp.Services;

public class VencimentoAlertaServiceTests
{
    private const string UserId = "user-123";

    [Fact]
    public void DatasAlvo_PadraoNoDiaEDoisDiasAntes()
    {
        var hoje = new DateOnly(2026, 7, 7);

        var datas = VencimentoAlertaService.DatasAlvo(hoje, new[] { 2, 0 });

        // No dia e dois dias antes (ordenadas). Não inclui o dia intermediário.
        Assert.Equal(new[] { hoje, hoje.AddDays(2) }, datas);
    }

    [Fact]
    public void DatasAlvo_IgnoraNegativosERemoveDuplicatas()
    {
        var hoje = new DateOnly(2026, 7, 7);

        var datas = VencimentoAlertaService.DatasAlvo(hoje, new[] { 0, 0, 2, -1 });

        Assert.Equal(new[] { hoje, hoje.AddDays(2) }, datas);
    }

    [Fact]
    public void MontarMensagem_VariasFaturas_EmUmaSoMensagem()
    {
        var hoje = new DateOnly(2026, 7, 7);
        var venceHoje = new Invoice(UserId, null, new DateOnly(2026, 7, 1), 100m, hoje);
        var venceEmDois = new Invoice(UserId, null, new DateOnly(2026, 7, 1), 200m, hoje.AddDays(2));

        var mensagem = VencimentoAlertaService.MontarMensagem(new[] { venceHoje, venceEmDois }, hoje);

        // Uma única string (uma mensagem) contendo ambas as faturas.
        Assert.Contains("vence *hoje* (07/07)", mensagem);
        Assert.Contains("vence 09/07", mensagem);
        Assert.Contains("R$", mensagem);
    }

    [Fact]
    public void MontarMensagem_FaturaVencida_MarcaComoVencida()
    {
        var hoje = new DateOnly(2026, 7, 7);
        var vencida = new Invoice(UserId, null, new DateOnly(2026, 7, 1), 150m, hoje.AddDays(-3));

        var mensagem = VencimentoAlertaService.MontarMensagem(new[] { vencida }, hoje);

        Assert.Contains("VENCIDA em 04/07", mensagem);
    }

    [Fact]
    public async Task EnviarAsync_IgnoraFaturasDeEntrada()
    {
        var (db, conn) = NovoContexto();
        await using var _ = db;
        using var __ = conn;

        var hoje = DateOnly.FromDateTime(DateTime.Today);

        // Uma saída e uma entrada, ambas com fatura pendente vencendo hoje.
        var saida = NovoLancamento(db, "Luz", ETransactionTypes.Expense);
        var entrada = NovoLancamento(db, "Salário", ETransactionTypes.Income);
        db.Bills.AddRange(saida, entrada);
        db.Invoices.AddRange(
            new Invoice(UserId, saida.Id, hoje, 100m, hoje),
            new Invoice(UserId, entrada.Id, hoje, 5000m, hoje));
        await db.SaveChangesAsync();

        var resultado = await NovoServico(db).EnviarAsync();

        // Só a saída entra no alerta; a entrada (recebível) é ignorada.
        Assert.Equal(1, resultado.Quantidade);
        Assert.Contains("Luz", resultado.Mensagem);
        Assert.DoesNotContain("Salário", resultado.Mensagem);
    }

    private static VencimentoAlertaService NovoServico(ApplicationDbContext db)
    {
        // Evolution não configurada: SendAlertAsync retorna false sem rede; a contagem do resultado
        // reflete as faturas selecionadas, que é o que este teste verifica.
        var whatsapp = new EvolutionWhatsAppClient(
            new HttpClient(), new EvolutionOptions(), NullLogger<EvolutionWhatsAppClient>.Instance);
        return new VencimentoAlertaService(
            db, whatsapp, new VencimentoAlertaOptions(), NullLogger<VencimentoAlertaService>.Instance);
    }

    private static Bill NovoLancamento(ApplicationDbContext db, string nome, ETransactionTypes tipo)
    {
        var categoria = new Category(UserId, nome, tipo);
        db.Categories.Add(categoria);

        var start = new DateOnly(2020, 1, 10);
        return new Bill(
            UserId, nome, "Fonte X", categoria,
            new RecurrenceRule(ERecurrenceFrequency.Monthly, 1, 10, start));
    }

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
}
