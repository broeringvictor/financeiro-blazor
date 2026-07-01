using System.Globalization;
using Microsoft.AspNetCore.Components;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using WebApp.Data;
using WebApp.Models;
using WebApp.Models.Enums;

namespace WebApp.Components.Shared;

public partial class InvoicesGrid : ComponentBase
{
    [Inject] private IServiceScopeFactory ScopeFactory { get; set; } = default!;

    /// <summary>Dono das faturas exibidas.</summary>
    [Parameter, EditorRequired] public string UserId { get; set; } = string.Empty;

    /// <summary>Disparado ao clicar em pagar uma fatura.</summary>
    [Parameter] public EventCallback<Invoice> OnPay { get; set; }

    /// <summary>Disparado ao clicar em cancelar uma fatura.</summary>
    [Parameter] public EventCallback<Invoice> OnCancel { get; set; }

    /// <summary>Linha do grid: a fatura + o nome da conta associada.</summary>
    public sealed record InvoiceRow(Invoice Invoice, string? BillName);

    private static readonly CultureInfo _culture = CultureInfo.GetCultureInfo("pt-BR");

    private MudDataGrid<InvoiceRow> _grid = default!;
    private EInvoiceStatus? _statusFilter = EInvoiceStatus.Pending;

    /// <summary>Recarrega a página atual a partir do banco.</summary>
    public Task ReloadAsync() => _grid.ReloadServerData();

    private async Task OnStatusFilterChanged(EInvoiceStatus? status)
    {
        _statusFilter = status;
        await _grid.ReloadServerData();
    }

    private async Task<GridData<InvoiceRow>> LoadServerData(GridState<InvoiceRow> state, CancellationToken token)
    {
        await using var scope = ScopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var query = db.Invoices
            .AsNoTracking()
            .Include(i => i.Bill)
            .Where(i => i.UserId == UserId && i.DeletedAt == null);

        if (_statusFilter is { } status)
        {
            query = query.Where(i => i.Status == status);
        }

        var totalItems = await query.CountAsync(token);

        var items = await query
            .OrderBy(i => i.DueDate)
            .Skip(state.Page * state.PageSize)
            .Take(state.PageSize)
            .ToListAsync(token);

        var rows = items
            .Select(i => new InvoiceRow(i, i.Bill?.Name))
            .ToList();

        return new GridData<InvoiceRow> { Items = rows, TotalItems = totalItems };
    }
}
