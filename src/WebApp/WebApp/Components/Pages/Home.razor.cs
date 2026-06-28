using System.Security.Claims;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using WebApp.Components.Shared;
using WebApp.Data;
using WebApp.Models;

namespace WebApp.Components.Pages;

public partial class Home : ComponentBase
{
    [Inject] private IDialogService DialogService { get; set; } = default!;

    [Inject] private ISnackbar Snackbar { get; set; } = default!;

    [Inject] private IServiceScopeFactory ScopeFactory { get; set; } = default!;

    [CascadingParameter] private Task<AuthenticationState>? AuthState { get; set; }

    private async Task AbrirNovaTransacao()
    {
        var userId = await GetUserIdAsync();

        var parameters = new DialogParameters<TransactionFormDialog>
        {
            { x => x.UserId, userId },
        };

        var options = new DialogOptions
        {
            CloseOnEscapeKey = true,
            MaxWidth = MaxWidth.Small,
            FullWidth = true,
        };

        var dialog = await DialogService.ShowAsync<TransactionFormDialog>("Nova transação", parameters, options);
        var result = await dialog.Result;

        if (result is { Canceled: false, Data: Transaction transaction })
        {
            await SalvarAsync(transaction);
            Snackbar.Add($"Transação \"{transaction.Title}\" criada.", Severity.Success);
        }
    }

    private async Task SalvarAsync(Transaction transaction)
    {
        // Escopo curto: o DbContext não deve viver junto com o circuito interativo.
        await using var scope = ScopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        db.Transactions.Add(transaction);
        await db.SaveChangesAsync();
    }

    private async Task<string> GetUserIdAsync()
    {
        if (AuthState is null)
        {
            return string.Empty;
        }

        var state = await AuthState;
        return state.User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? string.Empty;
    }
}
