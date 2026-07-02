using System.Globalization;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using WebApp.Services;

namespace WebApp.Components.Shared;

/// <summary>Resumo financeiro do usuário: saldo, gastos por mês e previsão de gastos das contas.</summary>
public partial class FinanceSummaryPanel : ComponentBase
{
    [Inject] private IServiceScopeFactory ScopeFactory { get; set; } = default!;

    [Inject] private ILogger<FinanceSummaryPanel> Logger { get; set; } = default!;

    /// <summary>Dono dos dados exibidos.</summary>
    [Parameter, EditorRequired] public string UserId { get; set; } = string.Empty;

    private static readonly CultureInfo _culture = CultureInfo.GetCultureInfo("pt-BR");

    private bool _carregando = true;
    private decimal _saldo;
    private IReadOnlyList<GastoMensal> _gastosPorMes = [];
    private PrevisaoGastos _previsao = new([], 0m);

    private List<ChartSeries<double>> _series = [];
    private string[] _labels = [];

    // Cor primária do tema MudBlazor padrão — validada (contraste/luminância) para uso em gráfico de série única.
    private readonly ChartOptions _chartOptions = new() { ChartPalette = ["#594AE2"] };

    private string? _lastLoadedUserId;

    protected override async Task OnParametersSetAsync()
    {
        // No prerender estático (IsInteractive=false) a instância é descartada e recriada quando o
        // circuito interativo conecta — carregar aqui só duplicaria as consultas para nada.
        if (!RendererInfo.IsInteractive || string.IsNullOrEmpty(UserId) || UserId == _lastLoadedUserId)
        {
            return;
        }

        await CarregarAsync();
    }

    /// <summary>Recarrega os indicadores (chamado após criar/editar transações ou pagar faturas).</summary>
    public Task ReloadAsync() => CarregarAsync();

    private async Task CarregarAsync()
    {
        _carregando = true;
        StateHasChanged();

        try
        {
            await using var scope = ScopeFactory.CreateAsyncScope();
            var service = scope.ServiceProvider.GetRequiredService<FinanceSummaryService>();

            _saldo = await service.ObterSaldoAsync(UserId);
            _gastosPorMes = await service.ObterGastosPorMesAsync(UserId);
            _previsao = await service.ObterPrevisaoAsync(UserId);

            _labels = _gastosPorMes.Select(g => g.Mes.ToString("MMM/yy", _culture)).ToArray();
            _series =
            [
                new ChartSeries<double> { Name = "Gastos", Data = _gastosPorMes.Select(g => (double)g.Total).ToArray() },
            ];

            _lastLoadedUserId = UserId;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Falha ao carregar o resumo financeiro do usuário '{UserId}'.", UserId);
        }
        finally
        {
            _carregando = false;
        }
    }
}
