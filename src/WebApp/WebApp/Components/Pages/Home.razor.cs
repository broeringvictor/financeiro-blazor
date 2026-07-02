using System.Security.Claims;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using WebApp.Components.Shared;
using WebApp.Data;
using WebApp.Models;
using WebApp.Services;

namespace WebApp.Components.Pages;

public partial class Home : ComponentBase
{
    [Inject] private IDialogService DialogService { get; set; } = default!;

    [Inject] private ISnackbar Snackbar { get; set; } = default!;

    [Inject] private IServiceScopeFactory ScopeFactory { get; set; } = default!;

    [Inject] private ILogger<Home> Logger { get; set; } = default!;

    [CascadingParameter] private Task<AuthenticationState>? AuthState { get; set; }

    private TransactionsGrid? _grid;
    private FinanceSummaryPanel? _resumo;
    private string _userId = string.Empty;

    private bool _buscando;
    private string? _resultadoBusca;

    protected override async Task OnInitializedAsync()
    {
        _userId = await GetUserIdAsync();
    }
    

    private Task AbrirNovaTransacao() => AbrirDialogoAsync(null);

    private Task EditarTransacao(Transaction transaction) => AbrirDialogoAsync(transaction);

    private async Task AbrirDialogoAsync(Transaction? existente)
    {
        if (string.IsNullOrEmpty(_userId))
        {
            Snackbar.Add("Você precisa estar autenticado para gerenciar transações.", Severity.Warning);
            return;
        }

        var parameters = new DialogParameters<TransactionFormDialog>
        {
            { x => x.UserId, _userId },
            { x => x.Transaction, existente },
        };

        var options = new DialogOptions
        {
            CloseOnEscapeKey = true,
            MaxWidth = MaxWidth.Small,
            FullWidth = true,
        };

        var titulo = existente is null ? "Nova transação" : "Editar transação";
        var dialog = await DialogService.ShowAsync<TransactionFormDialog>(titulo, parameters, options);
        var result = await dialog.Result;

        if (result is { Canceled: false, Data: Transaction transaction })
        {
            await SalvarAsync(transaction, isNew: existente is null);
        }
    }

    private async Task ExcluirTransacao(Transaction transaction)
    {
        var confirmado = await DialogService.ShowMessageBoxAsync(
            "Excluir transação",
            $"Deseja excluir \"{transaction.Title}\"?",
            yesText: "Excluir",
            cancelText: "Cancelar");

        if (confirmado != true)
        {
            return;
        }

        transaction.Delete();
        await PersistirAsync(transaction, db => db.Transactions.Update(transaction), "Transação excluída.");
    }

    private Task SalvarAsync(Transaction transaction, bool isNew)
    {
        var mensagem = isNew ? "Transação criada." : "Transação atualizada.";
        return PersistirAsync(
            transaction,
            db =>
            {
                if (isNew)
                {
                    db.Transactions.Add(transaction);
                }
                else
                {
                    db.Transactions.Update(transaction);
                }
            },
            mensagem);
    }

    private async Task PersistirAsync(Transaction transaction, Action<ApplicationDbContext> aplicar, string sucesso)
    {
        try
        {
            // Escopo curto: o DbContext não deve viver junto com o circuito interativo.
            await using var scope = ScopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            aplicar(db);
            await db.SaveChangesAsync();

            Snackbar.Add(sucesso, Severity.Success);

            if (_grid is not null)
            {
                await _grid.ReloadAsync();
            }

            if (_resumo is not null)
            {
                await _resumo.ReloadAsync();
            }
        }
        catch (DbUpdateException ex)
        {
            Logger.LogError(ex, "Falha ao persistir transação do usuário '{UserId}'.", transaction.UserId);
            Snackbar.Add("Não foi possível concluir a operação.", Severity.Error);
        }
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
