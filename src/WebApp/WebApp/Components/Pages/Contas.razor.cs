using System.Security.Claims;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using WebApp.Components.Shared;
using WebApp.Data;
using WebApp.Models;

namespace WebApp.Components.Pages;

public partial class Contas : ComponentBase
{
    [Inject] private IDialogService DialogService { get; set; } = default!;

    [Inject] private ISnackbar Snackbar { get; set; } = default!;

    [Inject] private IServiceScopeFactory ScopeFactory { get; set; } = default!;

    [Inject] private ILogger<Contas> Logger { get; set; } = default!;

    [CascadingParameter] private Task<AuthenticationState>? AuthState { get; set; }

    private IReadOnlyList<Bill> _bills = [];
    private string _userId = string.Empty;

    protected override async Task OnInitializedAsync()
    {
        if (AuthState is not null)
        {
            var state = await AuthState;
            _userId = state.User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? string.Empty;
        }

        await CarregarAsync();
    }

    private async Task CarregarAsync()
    {
        await using var scope = ScopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        _bills = await db.Bills
            .AsNoTracking()
            .Where(b => b.UserId == _userId && b.DeletedAt == null)
            .OrderBy(b => b.Name)
            .ToListAsync();
    }

    private async Task NovaConta()
    {
        if (string.IsNullOrEmpty(_userId))
        {
            Snackbar.Add("Você precisa estar autenticado.", Severity.Warning);
            return;
        }

        var parameters = new DialogParameters<BillFormDialog> { { x => x.UserId, _userId } };
        var dialog = await DialogService.ShowAsync<BillFormDialog>("Nova conta", parameters, OpcoesDialogo());
        var result = await dialog.Result;

        if (result is { Canceled: false, Data: Bill nova })
        {
            await PersistirAsync(db => db.Bills.Add(nova), "Conta criada.");
        }
    }

    private async Task EditarConta(Bill bill)
    {
        var parameters = new DialogParameters<BillFormDialog>
        {
            { x => x.Bill, bill },
            { x => x.UserId, _userId },
        };

        var dialog = await DialogService.ShowAsync<BillFormDialog>("Editar conta", parameters, OpcoesDialogo());
        var result = await dialog.Result;

        if (result is { Canceled: false, Data: Bill editada })
        {
            await PersistirAsync(db => db.Bills.Update(editada), "Conta atualizada.");
        }
    }

    private async Task ExcluirConta(Bill bill)
    {
        var confirmado = await DialogService.ShowMessageBoxAsync(
            "Excluir conta",
            $"Deseja excluir \"{bill.Name}\"?",
            yesText: "Excluir",
            cancelText: "Cancelar");

        if (confirmado != true)
        {
            return;
        }

        try
        {
            await using var scope = ScopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            var alvo = await db.Bills.FindAsync(bill.Id);
            if (alvo is null || alvo.UserId != _userId)
            {
                return;
            }

            alvo.Delete();
            await db.SaveChangesAsync();

            Snackbar.Add("Conta excluída.", Severity.Success);
            await CarregarAsync();
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Falha ao excluir a conta {BillId}.", bill.Id);
            Snackbar.Add("Não foi possível excluir a conta.", Severity.Error);
        }
    }

    private async Task PersistirAsync(Action<ApplicationDbContext> aplicar, string sucesso)
    {
        try
        {
            await using var scope = ScopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            aplicar(db);
            await db.SaveChangesAsync();

            Snackbar.Add(sucesso, Severity.Success);
            await CarregarAsync();
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Falha ao salvar a conta do usuário '{UserId}'.", _userId);
            Snackbar.Add("Não foi possível salvar a conta.", Severity.Error);
        }
    }

    private static DialogOptions OpcoesDialogo() => new()
    {
        CloseOnEscapeKey = true,
        MaxWidth = MaxWidth.Small,
        FullWidth = true,
    };
}
