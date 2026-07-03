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

    /// <summary>Quando informado, restringe às faturas de uma única conta (Bill).</summary>
    [Parameter] public Guid? BillId { get; set; }

    /// <summary>Disparado ao clicar em pagar uma fatura.</summary>
    [Parameter] public EventCallback<Invoice> OnPay { get; set; }

    /// <summary>Disparado ao clicar em cancelar uma fatura.</summary>
    [Parameter] public EventCallback<Invoice> OnCancel { get; set; }

    /// <summary>Disparado ao clicar em editar uma fatura (não paga).</summary>
    [Parameter] public EventCallback<Invoice> OnEdit { get; set; }

    /// <summary>Disparado ao clicar em excluir uma fatura.</summary>
    [Parameter] public EventCallback<Invoice> OnDelete { get; set; }

    /// <summary>Linha do grid: a fatura + o nome da conta associada.</summary>
    public sealed record InvoiceRow(Invoice Invoice, string? BillName);

    private static readonly CultureInfo _culture = CultureInfo.GetCultureInfo("pt-BR");

    private MudDataGrid<InvoiceRow> _grid = default!;
    private EInvoiceStatus? _statusFilter;

    protected override void OnInitialized()
    {
        // Na visão de uma conta específica mostramos todo o histórico; na visão geral, só as pendentes.
        _statusFilter = BillId is null ? EInvoiceStatus.Pending : null;
    }

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

        if (BillId is { } billId)
        {
            query = query.Where(i => i.BillId == billId);
        }

        if (_statusFilter is { } status)
        {
            query = query.Where(i => i.Status == status);
        }

        var totalItems = await query.CountAsync(token);

        query = BillId is null
            ? query.OrderBy(i => i.DueDate)
            : query.OrderByDescending(i => i.ReferenceMonth);

        var items = await query
            .Skip(state.Page * state.PageSize)
            .Take(state.PageSize)
            .ToListAsync(token);

        var rows = items
            .Select(i => new InvoiceRow(i, i.Bill?.Name))
            .ToList();

        return new GridData<InvoiceRow> { Items = rows, TotalItems = totalItems };
    }
}
