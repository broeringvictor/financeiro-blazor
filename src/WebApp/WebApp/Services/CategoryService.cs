using Microsoft.EntityFrameworkCore;
using WebApp.Data;
using WebApp.Extensions;
using WebApp.Models;
using WebApp.Models.Enums;

namespace WebApp.Services;

/// <summary>
/// Gestão das categorias (principal → subcategoria) de um usuário: leitura da árvore, CRUD, seed dos
/// padrões e backfill das linhas legadas (enum) para o novo vínculo por <see cref="Category"/>.
/// </summary>
public sealed class CategoryService(ApplicationDbContext db, ILogger<CategoryService> logger)
{
    /// <summary>Categorias principais (com as subcategorias carregadas), opcionalmente filtradas por tipo.</summary>
    public async Task<IReadOnlyList<Category>> GetTreeAsync(
        string userId, ETransactionTypes? type = null, CancellationToken ct = default)
    {
        await EnsureSeededAsync(userId, ct);

        var query = db.Categories
            .AsNoTracking()
            .Where(c => c.UserId == userId && c.ParentId == null && c.DeletedAt == null);

        if (type is { } t)
        {
            query = query.Where(c => c.Type == t);
        }

        return await query
            .Include(c => c.Children.Where(f => f.DeletedAt == null))
            .OrderBy(c => c.Type)
            .ThenBy(c => c.Name)
            .ToListAsync(ct);
    }

    /// <summary>Busca uma categoria do usuário pelo id (ou null).</summary>
    public Task<Category?> GetByIdAsync(string userId, Guid id, CancellationToken ct = default) =>
        db.Categories.FirstOrDefaultAsync(c => c.Id == id && c.UserId == userId && c.DeletedAt == null, ct);

    /// <summary>Cria uma categoria principal (parentId nulo) ou subcategoria.</summary>
    public async Task<Category> CriarAsync(
        string userId, string name, ETransactionTypes type, Guid? parentId = null, CancellationToken ct = default)
    {
        Category? parent = null;
        if (parentId is { } pid)
        {
            parent = await GetByIdAsync(userId, pid, ct)
                     ?? throw new InvalidOperationException("Categoria principal não encontrada.");
        }

        var category = new Category(userId, name, type, parent);
        db.Categories.Add(category);
        await db.SaveChangesAsync(ct);
        return category;
    }

    /// <summary>Renomeia uma categoria.</summary>
    public async Task EditarAsync(Guid id, string userId, string name, CancellationToken ct = default)
    {
        var category = await GetByIdAsync(userId, id, ct)
                       ?? throw new InvalidOperationException("Categoria não encontrada.");
        category.Edit(name);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Exclui (logicamente) uma categoria. Bloqueia se houver subcategorias ativas ou se estiver em uso por
    /// transações/contas não excluídas.
    /// </summary>
    public async Task ExcluirAsync(Guid id, string userId, CancellationToken ct = default)
    {
        var category = await GetByIdAsync(userId, id, ct)
                       ?? throw new InvalidOperationException("Categoria não encontrada.");

        var temFilhas = await db.Categories.AnyAsync(
            c => c.ParentId == id && c.DeletedAt == null, ct);
        if (temFilhas)
        {
            throw new InvalidOperationException("Exclua ou mova as subcategorias antes de excluir esta categoria.");
        }

        var emUso = await db.Transactions.AnyAsync(t => t.CategoryId == id && t.DeletedAt == null, ct)
                    || await db.Bills.AnyAsync(b => b.CategoryId == id && b.DeletedAt == null, ct);
        if (emUso)
        {
            throw new InvalidOperationException("Esta categoria está em uso por transações ou contas e não pode ser excluída.");
        }

        category.Delete();
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Uma categoria padrão para o tipo (preferindo a "Outras..."), criando o seed se necessário.</summary>
    public async Task<Category> ResolveDefaultAsync(
        string userId, ETransactionTypes type, CancellationToken ct = default)
    {
        await EnsureSeededAsync(userId, ct);

        var roots = await db.Categories
            .Where(c => c.UserId == userId && c.ParentId == null && c.Type == type && c.DeletedAt == null)
            .OrderBy(c => c.Name)
            .ToListAsync(ct);

        // Preferir a categoria "Outras..." (OtherIncome/OtherExpense) como padrão de fallback.
        var preferida = TransactionCategories.For(type)
            .FirstOrDefault(c => c is ETransactionCategory.OtherIncome or ETransactionCategory.OtherExpense)
            .GetDisplayName();

        return roots.FirstOrDefault(c => c.Name == preferida)
               ?? roots.FirstOrDefault()
               ?? throw new InvalidOperationException($"Nenhuma categoria disponível para o tipo {type}.");
    }

    /// <summary>
    /// Garante que o usuário tenha as categorias principais padrão (uma por valor do enum legado).
    /// Idempotente. Retorna o mapa enum → id da categoria principal (usado no backfill).
    /// </summary>
    public async Task<IReadOnlyDictionary<ETransactionCategory, Guid>> EnsureSeededAsync(
        string userId, CancellationToken ct = default)
    {
        var existentes = await db.Categories
            .Where(c => c.UserId == userId && c.ParentId == null)
            .ToListAsync(ct);

        var mapa = new Dictionary<ETransactionCategory, Guid>();
        var novas = new List<Category>();

        foreach (var (type, enumCategory) in TransactionCategories.All)
        {
            var nome = enumCategory.GetDisplayName();
            var existente = existentes.FirstOrDefault(c => c.Type == type && c.Name == nome);

            if (existente is not null)
            {
                mapa[enumCategory] = existente.Id;
                continue;
            }

            var nova = new Category(userId, nome, type);
            novas.Add(nova);
            mapa[enumCategory] = nova.Id;
        }

        if (novas.Count > 0)
        {
            db.Categories.AddRange(novas);
            await db.SaveChangesAsync(ct);
            logger.LogInformation("Seed de {Qtd} categoria(s) padrão para o usuário {UserId}.", novas.Count, userId);
        }

        return mapa;
    }

    /// <summary>
    /// Preenche <c>CategoryId</c> das transações/contas legadas (ainda nulas) a partir da categoria enum
    /// legada, usando o mapa do seed. Retorna quantas linhas foram atualizadas.
    /// </summary>
    public async Task<int> BackfillLegacyAsync(string userId, CancellationToken ct = default)
    {
        var mapa = await EnsureSeededAsync(userId, ct);
        var atualizadas = 0;

        var transacoes = await db.Transactions
            .Where(t => t.UserId == userId && t.CategoryId == null)
            .ToListAsync(ct);
        foreach (var t in transacoes)
        {
            if (mapa.TryGetValue(t.LegacyCategory, out var categoryId))
            {
                db.Entry(t).Property(x => x.CategoryId).CurrentValue = categoryId;
                atualizadas++;
            }
        }

        var contas = await db.Bills
            .Where(b => b.UserId == userId && b.CategoryId == null)
            .ToListAsync(ct);
        foreach (var b in contas)
        {
            if (mapa.TryGetValue(b.LegacyCategory, out var categoryId))
            {
                db.Entry(b).Property(x => x.CategoryId).CurrentValue = categoryId;
                atualizadas++;
            }
        }

        if (atualizadas > 0)
        {
            await db.SaveChangesAsync(ct);
            logger.LogInformation("Backfill de categoria em {Qtd} linha(s) do usuário {UserId}.", atualizadas, userId);
        }

        return atualizadas;
    }

    /// <summary>Roda o seed + backfill para todos os usuários que têm transações ou contas. Chamado no startup.</summary>
    public async Task SeedAllUsersAsync(CancellationToken ct = default)
    {
        var userIds = await db.Transactions.Select(t => t.UserId)
            .Union(db.Bills.Select(b => b.UserId))
            .Distinct()
            .ToListAsync(ct);

        foreach (var userId in userIds)
        {
            await BackfillLegacyAsync(userId, ct);
        }
    }
}
