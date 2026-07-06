using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using WebApp.Data;
using WebApp.Models;
using WebApp.Models.Enums;
using WebApp.Models.ValueObjects;
using WebApp.Services;

namespace Tests.WebApp.Services;

public class CategoryServiceTests
{
    private const string UserId = "user-123";

    private static CategoryService NovoServico(ApplicationDbContext db) =>
        new(db, NullLogger<CategoryService>.Instance);

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

    [Fact]
    public async Task EnsureSeeded_Cria13Raizes_Idempotente()
    {
        var (db, conn) = NovoContexto();
        await using var _ = db;
        using var __ = conn;
        var sut = NovoServico(db);

        var mapa = await sut.EnsureSeededAsync(UserId);
        var segundaRodada = await sut.EnsureSeededAsync(UserId);

        Assert.Equal(13, mapa.Count);
        Assert.Equal(13, await db.Categories.CountAsync());
        Assert.All(await db.Categories.ToListAsync(), c => Assert.Null(c.ParentId));
        // Rodar de novo não duplica.
        Assert.Equal(13, segundaRodada.Count);
        Assert.Equal(13, await db.Categories.CountAsync());
    }

    [Fact]
    public async Task BackfillLegacy_MapeiaEnumParaCategoriaSeedada()
    {
        var (db, conn) = NovoContexto();
        await using var _ = db;
        using var __ = conn;

        // Insere uma transação "legada": Category (enum) preenchido, CategoryId nulo.
        var id = Guid.CreateVersion7();
        await db.Database.ExecuteSqlAsync($@"
INSERT INTO Transactions (Id, UserId, Type, Category, Title, Amount, CreatedAt, UpdatedAt)
VALUES ({id}, {UserId}, {(int)ETransactionTypes.Expense}, {(int)ETransactionCategory.Rent},
        {"Aluguel de maio"}, {1500m}, {DateTime.UtcNow}, {DateTime.UtcNow})");

        var sut = NovoServico(db);
        var atualizadas = await sut.BackfillLegacyAsync(UserId);

        Assert.Equal(1, atualizadas);
        var tx = await db.Transactions.Include(t => t.Category).SingleAsync();
        Assert.NotNull(tx.CategoryId);
        Assert.Equal("Aluguel", tx.Category!.Name); // DisplayName de ETransactionCategory.Rent
        Assert.Equal(ETransactionTypes.Expense, tx.Category.Type);
    }

    [Fact]
    public async Task Criar_Subcategoria_HerdaTipoDoPai()
    {
        var (db, conn) = NovoContexto();
        await using var _ = db;
        using var __ = conn;
        var sut = NovoServico(db);

        var pai = await sut.CriarAsync(UserId, "Saúde", ETransactionTypes.Expense);
        var sub = await sut.CriarAsync(UserId, "Farmácia", ETransactionTypes.Income, pai.Id);

        Assert.Equal(pai.Id, sub.ParentId);
        Assert.Equal(ETransactionTypes.Expense, sub.Type);
    }

    [Fact]
    public async Task Excluir_ComSubcategorias_Bloqueia()
    {
        var (db, conn) = NovoContexto();
        await using var _ = db;
        using var __ = conn;
        var sut = NovoServico(db);

        var pai = await sut.CriarAsync(UserId, "Saúde", ETransactionTypes.Expense);
        await sut.CriarAsync(UserId, "Farmácia", ETransactionTypes.Expense, pai.Id);

        await Assert.ThrowsAsync<InvalidOperationException>(() => sut.ExcluirAsync(pai.Id, UserId));
    }

    [Fact]
    public async Task Excluir_EmUsoPorConta_Bloqueia()
    {
        var (db, conn) = NovoContexto();
        await using var _ = db;
        using var __ = conn;
        var sut = NovoServico(db);

        var categoria = await sut.CriarAsync(UserId, "Saúde", ETransactionTypes.Expense);
        db.Bills.Add(new Bill(UserId, "Plano de saúde", "Unimed", categoria,
            new RecurrenceRule(ERecurrenceFrequency.Monthly, 1, 10, new DateOnly(2026, 1, 10))));
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() => sut.ExcluirAsync(categoria.Id, UserId));
    }

    [Fact]
    public async Task GetTree_FiltraPorTipoETrazSubcategorias()
    {
        var (db, conn) = NovoContexto();
        await using var _ = db;
        using var __ = conn;
        var sut = NovoServico(db);

        var saude = await sut.CriarAsync(UserId, "Saúde", ETransactionTypes.Expense);
        await sut.CriarAsync(UserId, "Farmácia", ETransactionTypes.Expense, saude.Id);

        var arvore = await sut.GetTreeAsync(UserId, ETransactionTypes.Expense);

        // Só categorias de Despesa (as 13 seedadas incluem 6 despesas + a "Saúde" criada = 7 raízes).
        Assert.All(arvore, c => Assert.Equal(ETransactionTypes.Expense, c.Type));
        var raizSaude = arvore.Single(c => c.Name == "Saúde");
        Assert.Contains(raizSaude.Children, f => f.Name == "Farmácia");
    }
}
