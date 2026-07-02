using Microsoft.AspNetCore.Components;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using WebApp.Data;
using WebApp.Models;

namespace WebApp.Components.Shared;

/// <summary>Grade de contas (Bill) com as ações de CRUD, busca de fatura e o histórico de faturas de cada conta.</summary>
public partial class ContasGrid : ComponentBase
{
    [Inject] private IDialogService DialogService { get; set; } = default!;

    [Inject] private ISnackbar Snackbar { get; set; } = default!;

    [Inject] private IServiceScopeFactory ScopeFactory { get; set; } = default!;

    [Inject] private ILogger<ContasGrid> Logger { get; set; } = default!;

    /// <summary>Dono das contas exibidas.</summary>
    [Parameter, EditorRequired] public string UserId { get; set; } = string.Empty;

    private IReadOnlyList<Bill> _bills = [];

    protected override async Task OnInitializedAsync() => await CarregarAsync();

    private async Task CarregarAsync()
    {
        await using var scope = ScopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        _bills = await db.Bills
            .AsNoTracking()
            .Where(b => b.UserId == UserId && b.DeletedAt == null)
            .OrderBy(b => b.Name)
            .ToListAsync();
    }

    private async Task NovaConta()
    {
        if (string.IsNullOrEmpty(UserId))
        {
            Snackbar.Add("Você precisa estar autenticado.", Severity.Warning);
            return;
        }

        var parameters = new DialogParameters<BillFormDialog> { { x => x.UserId, UserId } };
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
            { x => x.UserId, UserId },
        };

        var dialog = await DialogService.ShowAsync<BillFormDialog>("Editar conta", parameters, OpcoesDialogo());
        var result = await dialog.Result;

        if (result is { Canceled: false, Data: Bill editada })
        {
            await PersistirAsync(db => db.Bills.Update(editada), "Conta atualizada.");
        }
    }

    private async Task VerFaturas(Bill bill)
    {
        var parameters = new DialogParameters<BillInvoicesDialog>
        {
            { x => x.UserId, UserId },
            { x => x.BillId, bill.Id },
        };
        var options = new DialogOptions
        {
            CloseOnEscapeKey = true,
            MaxWidth = MaxWidth.Medium,
            FullWidth = true,
        };

        await DialogService.ShowAsync<BillInvoicesDialog>($"Faturas de \"{bill.Name}\"", parameters, options);
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
            if (alvo is null || alvo.UserId != UserId)
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
            Logger.LogError(ex, "Falha ao salvar a conta do usuário '{UserId}'.", UserId);
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
