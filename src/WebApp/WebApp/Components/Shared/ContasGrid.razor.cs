using Microsoft.AspNetCore.Components;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;
using MudBlazor;
using WebApp.Data;
using WebApp.Models;
using WebApp.Models.Enums;
using WebApp.Services;

namespace WebApp.Components.Shared;

/// <summary>
/// Grade de lançamentos (Bill) de um tipo — entradas ou saídas — com as ações de CRUD, busca de fatura
/// e o histórico de faturas de cada lançamento.
/// </summary>
public partial class ContasGrid : ComponentBase
{
    [Inject] private IDialogService DialogService { get; set; } = default!;

    [Inject] private ISnackbar Snackbar { get; set; } = default!;

    [Inject] private IServiceScopeFactory ScopeFactory { get; set; } = default!;

    [Inject] private ILogger<ContasGrid> Logger { get; set; } = default!;

    [Inject] private IJSRuntime JS { get; set; } = default!;

    /// <summary>Dono das contas exibidas.</summary>
    [Parameter, EditorRequired] public string UserId { get; set; } = string.Empty;

    /// <summary>Tipo dos lançamentos exibidos nesta grade (entradas ou saídas).</summary>
    [Parameter] public ETransactionTypes Tipo { get; set; } = ETransactionTypes.Expense;

    private bool _isIncome => Tipo == ETransactionTypes.Income;

    private IReadOnlyList<Bill> _bills = [];

    protected override async Task OnInitializedAsync()
    {
        // No prerender estático a instância é descartada e recriada quando o circuito interativo
        // conecta — carregar aqui só duplicaria a consulta para nada.
        if (!RendererInfo.IsInteractive)
        {
            return;
        }

        await CarregarAsync();
    }

    private async Task CarregarAsync()
    {
        await using var scope = ScopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var query = db.Bills
            .AsNoTracking()
            .Include(b => b.Category).ThenInclude(c => c!.Parent)
            .Where(b => b.UserId == UserId && b.DeletedAt == null);

        // Entradas são só as com categoria de receita; saídas incluem os legados sem categoria (tratados como despesa).
        query = _isIncome
            ? query.Where(b => b.Category != null && b.Category.Type == ETransactionTypes.Income)
            : query.Where(b => b.Category == null || b.Category.Type == ETransactionTypes.Expense);

        _bills = await query
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

        var parameters = new DialogParameters<BillFormDialog>
        {
            { x => x.UserId, UserId },
            { x => x.Tipo, Tipo },
        };
        var titulo = _isIncome ? "Nova entrada" : "Nova saída";
        var dialog = await DialogService.ShowAsync<BillFormDialog>(titulo, parameters, OpcoesDialogo());
        var result = await dialog.Result;

        if (result is { Canceled: false, Data: Bill nova })
        {
            await PersistirAsync(db => db.Bills.Add(nova), $"{(_isIncome ? "Entrada" : "Saída")} criada.");
            await GerarFaturasEmAbertoAsync(nova);
        }
    }

    private async Task EditarConta(Bill bill)
    {
        var parameters = new DialogParameters<BillFormDialog>
        {
            { x => x.Bill, bill },
            { x => x.UserId, UserId },
            { x => x.Tipo, Tipo },
        };

        var titulo = _isIncome ? "Editar entrada" : "Editar saída";
        var dialog = await DialogService.ShowAsync<BillFormDialog>(titulo, parameters, OpcoesDialogo());
        var result = await dialog.Result;

        if (result is { Canceled: false, Data: Bill editada })
        {
            await PersistirAsync(db => db.Bills.Update(editada), $"{(_isIncome ? "Entrada" : "Saída")} atualizada.");
            await GerarFaturasEmAbertoAsync(editada);
        }
    }

    /// <summary>
    /// Gera as faturas em aberto da conta logo após salvá-la, para o usuário ver as pendências na hora —
    /// sem esperar o worker diário. Falhas aqui não devem quebrar o salvamento, então são só logadas.
    /// </summary>
    private async Task GerarFaturasEmAbertoAsync(Bill bill)
    {
        try
        {
            await using var scope = ScopeFactory.CreateAsyncScope();
            var gerador = scope.ServiceProvider.GetRequiredService<GeracaoFaturaService>();
            var qtd = await gerador.GerarPendentesAsync(UserId, bill);

            if (qtd > 0)
            {
                Snackbar.Add($"{qtd} fatura(s) em aberto criada(s).", Severity.Info);
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Falha ao gerar faturas em aberto da conta {BillId}.", bill.Id);
        }
    }

    /// <summary>Copia a chave Pix da conta para a área de transferência.</summary>
    private async Task CopiarPix(string? pix)
    {
        if (string.IsNullOrWhiteSpace(pix))
        {
            return;
        }

        try
        {
            await JS.InvokeVoidAsync("navigator.clipboard.writeText", pix);
            Snackbar.Add("Pix copiado.", Severity.Success);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Falha ao copiar a chave Pix.");
            Snackbar.Add("Não foi possível copiar o Pix.", Severity.Error);
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

    private async Task BuscarConta(Bill bill)
    {
        if (string.IsNullOrEmpty(UserId))
        {
            return;
        }

        try
        {
            Snackbar.Add($"Procurando faturas de \"{bill.BillerName}\"...", Severity.Info);

            await using var scope = ScopeFactory.CreateAsyncScope();
            var orquestrador = scope.ServiceProvider.GetRequiredService<BuscaFaturaOrchestrator>();
            var invoice = await orquestrador.BuscarPorContaAsync(UserId, bill);

            Snackbar.Add(
                invoice is null
                    ? "Nenhuma fatura encontrada no e-mail."
                    : $"Fatura registrada: {invoice.Amount:C}, vence em {invoice.DueDate:dd/MM/yyyy}.",
                invoice is null ? Severity.Info : Severity.Success);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Falha ao buscar fatura da conta {BillId}.", bill.Id);
            Snackbar.Add("Não foi possível buscar a fatura.", Severity.Error);
        }
    }

    private async Task ExcluirConta(Bill bill)
    {
        var rotulo = _isIncome ? "entrada" : "saída";
        var confirmado = await DialogService.ShowMessageBoxAsync(
            $"Excluir {rotulo}",
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

            Snackbar.Add($"{char.ToUpper(rotulo[0]) + rotulo[1..]} excluída.", Severity.Success);
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
