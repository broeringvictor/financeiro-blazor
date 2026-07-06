using System.Globalization;
using Microsoft.AspNetCore.Components;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using WebApp.Data;
using WebApp.Models;
using WebApp.Models.Enums;
using WebApp.Services;

namespace WebApp.Components.Shared;

public partial class TransactionsGrid : ComponentBase
{
    [Inject] private IServiceScopeFactory ScopeFactory { get; set; } = default!;

    /// <summary>Dono das transações exibidas.</summary>
    [Parameter, EditorRequired] public string UserId { get; set; } = string.Empty;

    /// <summary>Disparado ao clicar em editar uma linha.</summary>
    [Parameter] public EventCallback<Transaction> OnEdit { get; set; }

    /// <summary>Disparado ao clicar em excluir uma linha.</summary>
    [Parameter] public EventCallback<Transaction> OnDelete { get; set; }

    private static readonly CultureInfo _culture = CultureInfo.GetCultureInfo("pt-BR");

    private MudDataGrid<Transaction> _grid = default!;
    private string? _search;
    private ETransactionTypes? _typeFilter;
    private Guid? _categoryFilter;
    private IReadOnlyList<Category> _categories = [];

    protected override async Task OnInitializedAsync()
    {
        if (!RendererInfo.IsInteractive)
        {
            return;
        }

        await CarregarCategoriasAsync();
    }

    /// <summary>Recarrega a página atual a partir do banco (ex.: após criar/editar).</summary>
    public Task ReloadAsync() => _grid.ReloadServerData();

    private async Task CarregarCategoriasAsync()
    {
        await using var scope = ScopeFactory.CreateAsyncScope();
        var categorias = scope.ServiceProvider.GetRequiredService<CategoryService>();
        _categories = await categorias.GetTreeAsync(UserId, _typeFilter);
    }

    // Nome exibido da categoria da transação: "Principal › Sub" quando for subcategoria.
    private static string NomeCategoria(Transaction t) =>
        t.Category is null ? "—"
        : t.Category.Parent is null ? t.Category.Name
        : $"{t.Category.Parent.Name} › {t.Category.Name}";

    private async Task OnSearchAsync(string search)
    {
        _search = search;
        await _grid.ReloadServerData();
    }

    private async Task OnTypeFilterChanged(ETransactionTypes? type)
    {
        _typeFilter = type;
        await CarregarCategoriasAsync();

        // Se a categoria filtrada não pertence mais à lista do novo tipo, limpa.
        if (_categoryFilter is { } id && !_categories.Any(c => c.Id == id || c.Children.Any(f => f.Id == id)))
        {
            _categoryFilter = null;
        }

        await _grid.ReloadServerData();
    }

    private async Task OnCategoryFilterChanged(Guid? categoryId)
    {
        _categoryFilter = categoryId;
        await _grid.ReloadServerData();
    }

    // Paginação/ordenação/busca feitas no banco — só a página atual é materializada.
    private async Task<GridData<Transaction>> LoadServerData(GridState<Transaction> state, CancellationToken token)
    {
        await using var scope = ScopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var query = db.Transactions
            .AsNoTracking()
            .Include(t => t.Category).ThenInclude(c => c!.Parent)
            .Where(t => t.UserId == UserId && t.DeletedAt == null);

        if (_typeFilter is { } type)
        {
            query = query.Where(t => t.Type == type);
        }

        if (_categoryFilter is { } categoryId)
        {
            // Filtrar por uma categoria principal inclui as suas subcategorias; por uma sub, é exato.
            var éPrincipal = _categories.Any(c => c.Id == categoryId);
            query = éPrincipal
                ? query.Where(t => t.CategoryId == categoryId || (t.Category != null && t.Category.ParentId == categoryId))
                : query.Where(t => t.CategoryId == categoryId);
        }

        if (!string.IsNullOrWhiteSpace(_search))
        {
            var term = $"%{_search.Trim()}%";
            query = query.Where(t =>
                EF.Functions.Like(t.Title, term) ||
                (t.Description != null && EF.Functions.Like(t.Description, term)));
        }

        var totalItems = await query.CountAsync(token);

        query = ApplySort(query, state.SortDefinitions.FirstOrDefault());

        var items = await query
            .Skip(state.Page * state.PageSize)
            .Take(state.PageSize)
            .ToListAsync(token);

        return new GridData<Transaction> { Items = items, TotalItems = totalItems };
    }

    private static IQueryable<Transaction> ApplySort(IQueryable<Transaction> query, SortDefinition<Transaction>? sort)
    {
        if (sort is null)
        {
            return query.OrderByDescending(t => t.CreatedAt);
        }

        var desc = sort.Descending;

        return sort.SortBy switch
        {
            nameof(Transaction.Title) => desc ? query.OrderByDescending(t => t.Title) : query.OrderBy(t => t.Title),
            nameof(Transaction.Description) => desc ? query.OrderByDescending(t => t.Description) : query.OrderBy(t => t.Description),
            nameof(Transaction.Amount) => desc ? query.OrderByDescending(t => t.Amount) : query.OrderBy(t => t.Amount),
            nameof(Transaction.Type) => desc ? query.OrderByDescending(t => t.Type) : query.OrderBy(t => t.Type),
            nameof(Transaction.Category) => desc
                ? query.OrderByDescending(t => t.Category!.Name)
                : query.OrderBy(t => t.Category!.Name),
            _ => desc ? query.OrderByDescending(t => t.CreatedAt) : query.OrderBy(t => t.CreatedAt),
        };
    }
}
