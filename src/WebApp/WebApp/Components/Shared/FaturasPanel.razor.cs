using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using WebApp.Data;
using WebApp.Models;
using WebApp.Services;

namespace WebApp.Components.Shared;

/// <summary>Grade de faturas (Invoice) com pagar/cancelar. Reusada na página /faturas e no modal de contas+faturas.</summary>
public partial class FaturasPanel : ComponentBase
{
    [Inject] private IDialogService DialogService { get; set; } = default!;

    [Inject] private ISnackbar Snackbar { get; set; } = default!;

    [Inject] private IServiceScopeFactory ScopeFactory { get; set; } = default!;

    [Inject] private ILogger<FaturasPanel> Logger { get; set; } = default!;

    /// <summary>Dono das faturas exibidas.</summary>
    [Parameter, EditorRequired] public string UserId { get; set; } = string.Empty;

    /// <summary>Quando informado, restringe às faturas de uma única conta (Bill).</summary>
    [Parameter] public Guid? BillId { get; set; }

    private InvoicesGrid? _grid;

    private async Task PagarFatura(Invoice invoice)
    {
        var confirmado = await DialogService.ShowMessageBoxAsync(
            "Pagar fatura",
            $"Confirmar pagamento de {invoice.Amount:C}? Isso cria uma transação de despesa.",
            yesText: "Pagar",
            cancelText: "Cancelar");

        if (confirmado != true)
        {
            return;
        }

        try
        {
            await using var scope = ScopeFactory.CreateAsyncScope();
            var service = scope.ServiceProvider.GetRequiredService<IngestaoFaturaService>();
            await service.PagarAsync(invoice.Id, UserId);

            Snackbar.Add("Fatura paga e transação criada.", Severity.Success);
            await ReloadAsync();
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Falha ao pagar a fatura {InvoiceId}.", invoice.Id);
            Snackbar.Add("Não foi possível pagar a fatura.", Severity.Error);
        }
    }

    private async Task CancelarFatura(Invoice invoice)
    {
        var confirmado = await DialogService.ShowMessageBoxAsync(
            "Cancelar fatura",
            "Deseja cancelar esta fatura?",
            yesText: "Cancelar fatura",
            cancelText: "Voltar");

        if (confirmado != true)
        {
            return;
        }

        try
        {
            await using var scope = ScopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            var alvo = await db.Invoices.FindAsync(invoice.Id);
            if (alvo is null || alvo.UserId != UserId || alvo.DeletedAt is not null)
            {
                return;
            }

            alvo.Cancel();
            await db.SaveChangesAsync();

            Snackbar.Add("Fatura cancelada.", Severity.Success);
            await ReloadAsync();
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Falha ao cancelar a fatura {InvoiceId}.", invoice.Id);
            Snackbar.Add("Não foi possível cancelar a fatura.", Severity.Error);
        }
    }

    private async Task EditarFatura(Invoice invoice)
    {
        var parameters = new DialogParameters<InvoiceFormDialog> { { x => x.Invoice, invoice } };
        var options = new DialogOptions { CloseOnEscapeKey = true, MaxWidth = MaxWidth.ExtraSmall, FullWidth = true };

        var dialog = await DialogService.ShowAsync<InvoiceFormDialog>("Editar fatura", parameters, options);
        var result = await dialog.Result;

        if (result is not { Canceled: false, Data: InvoiceFormDialog.Resultado dados })
        {
            return;
        }

        try
        {
            await using var scope = ScopeFactory.CreateAsyncScope();
            var service = scope.ServiceProvider.GetRequiredService<IngestaoFaturaService>();
            await service.EditarAsync(invoice.Id, UserId, dados.Amount, dados.DueDate, dados.IssueDate);

            Snackbar.Add("Fatura atualizada.", Severity.Success);
            await ReloadAsync();
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Falha ao editar a fatura {InvoiceId}.", invoice.Id);
            Snackbar.Add("Não foi possível editar a fatura.", Severity.Error);
        }
    }

    private async Task ExcluirFatura(Invoice invoice)
    {
        var confirmado = await DialogService.ShowMessageBoxAsync(
            "Excluir fatura",
            "Deseja excluir esta fatura? O PDF salvo (se houver) também será apagado. Esta ação não pode ser desfeita.",
            yesText: "Excluir",
            cancelText: "Voltar");

        if (confirmado != true)
        {
            return;
        }

        try
        {
            await using var scope = ScopeFactory.CreateAsyncScope();
            var service = scope.ServiceProvider.GetRequiredService<IngestaoFaturaService>();
            await service.ExcluirAsync(invoice.Id, UserId);

            Snackbar.Add("Fatura excluída.", Severity.Success);
            await ReloadAsync();
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Falha ao excluir a fatura {InvoiceId}.", invoice.Id);
            Snackbar.Add("Não foi possível excluir a fatura.", Severity.Error);
        }
    }

    private Task ReloadAsync() => _grid?.ReloadAsync() ?? Task.CompletedTask;
}
