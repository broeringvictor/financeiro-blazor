using System.Globalization;
using Microsoft.AspNetCore.Components;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using WebApp.Data;
using WebApp.Models;
using WebApp.Models.Enums;

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
    private ETransactionCategory? _categoryFilter;

    /// <summary>Recarrega a página atual a partir do banco (ex.: após criar/editar).</summary>
    public Task ReloadAsync() => _grid.ReloadServerData();

    // Categorias do tipo selecionado; sem tipo, todas.
    private IEnumerable<ETransactionCategory> CategoriasDisponiveis() =>
        _typeFilter is { } type ? TransactionCategories.For(type) : Enum.GetValues<ETransactionCategory>();

    private async Task OnSearchAsync(string search)
    {
        _search = search;
        await _grid.ReloadServerData();
    }

    private async Task OnTypeFilterChanged(ETransactionTypes? type)
    {
        _typeFilter = type;

        // Se a categoria filtrada não pertence ao novo tipo, limpa.
        if (type is { } t && _categoryFilter is { } c && !TransactionCategories.Belongs(t, c))
        {
            _categoryFilter = null;
        }

        await _grid.ReloadServerData();
    }

    private async Task OnCategoryFilterChanged(ETransactionCategory? category)
    {
        _categoryFilter = category;
        await _grid.ReloadServerData();
    }

    // Paginação/ordenação/busca feitas no banco — só a página atual é materializada.
    private async Task<GridData<Transaction>> LoadServerData(GridState<Transaction> state, CancellationToken token)
    {
        await using var scope = ScopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var query = db.Transactions
            .AsNoTracking()
            .Where(t => t.UserId == UserId && t.DeletedAt == null);

        if (_typeFilter is { } type)
        {
            query = query.Where(t => t.Type == type);
        }

        if (_categoryFilter is { } category)
        {
            query = query.Where(t => t.Category == category);
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
            nameof(Transaction.Category) => desc ? query.OrderByDescending(t => t.Category) : query.OrderBy(t => t.Category),
            _ => desc ? query.OrderByDescending(t => t.CreatedAt) : query.OrderBy(t => t.CreatedAt),
        };
    }
}
