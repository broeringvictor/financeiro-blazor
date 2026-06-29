using System.Security.Claims;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using WebApp.Components.Shared;
using WebApp.Data;
using WebApp.Models;
using WebApp.Services;

namespace WebApp.Components.Pages;

public partial class Faturas : ComponentBase
{
    [Inject] private IDialogService DialogService { get; set; } = default!;

    [Inject] private ISnackbar Snackbar { get; set; } = default!;

    [Inject] private IServiceScopeFactory ScopeFactory { get; set; } = default!;

    [Inject] private ILogger<Faturas> Logger { get; set; } = default!;

    [CascadingParameter] private Task<AuthenticationState>? AuthState { get; set; }

    private InvoicesGrid? _grid;
    private string _userId = string.Empty;

    protected override async Task OnInitializedAsync()
    {
        if (AuthState is not null)
        {
            var state = await AuthState;
            _userId = state.User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? string.Empty;
        }
    }

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
            await service.PagarAsync(invoice.Id, _userId);

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
            if (alvo is null || alvo.UserId != _userId || alvo.DeletedAt is not null)
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

    private Task ReloadAsync() => _grid?.ReloadAsync() ?? Task.CompletedTask;
}
