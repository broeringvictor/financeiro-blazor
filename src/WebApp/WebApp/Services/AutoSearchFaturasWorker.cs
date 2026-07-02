using Microsoft.EntityFrameworkCore;
using WebApp.Data;
using WebApp.Models;

namespace WebApp.Services;

/// <summary>Configuração da busca automática diária de faturas (seção "AutoSearchFaturas").</summary>
public sealed class AutoSearchFaturasOptions
{
    /// <summary>Liga/desliga a varredura diária em background.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Horário do dia (local) em que a varredura é disparada.</summary>
    public TimeOnly HoraExecucao { get; set; } = new(3, 0);
}

/// <summary>
/// Varredura diária das contas com <see cref="Bill.AutoSearch"/> marcado: para cada uma, reusa o
/// mesmo fluxo do botão "Buscar fatura agora" (<see cref="BuscaFaturaOrchestrator"/>), sem intervenção
/// manual. Uma falha numa conta não interrompe as demais.
/// </summary>
public sealed class AutoSearchFaturasWorker(
    ILogger<AutoSearchFaturasWorker> logger,
    IServiceScopeFactory scopeFactory,
    AutoSearchFaturasOptions options) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Enabled)
        {
            logger.LogInformation("Busca automática de faturas DESABILITADA (AutoSearchFaturas:Enabled=false).");
            return;
        }

        logger.LogInformation("Busca automática de faturas HABILITADA. Horário diário: {Horario}", options.HoraExecucao);

        while (!stoppingToken.IsCancellationRequested)
        {
            var espera = CalcularEsperaAteProximaExecucao(options.HoraExecucao, DateTime.Now);
            logger.LogInformation("Próxima varredura em {Espera}.", espera);

            try
            {
                await Task.Delay(espera, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            if (stoppingToken.IsCancellationRequested)
            {
                break;
            }

            await using var scope = scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var orquestrador = scope.ServiceProvider.GetRequiredService<BuscaFaturaOrchestrator>();

            var contas = await db.Bills
                .Where(b => b.Active && b.AutoSearch && b.DeletedAt == null)
                .ToListAsync(stoppingToken);

            var referencia = new DateOnly(DateTime.Today.Year, DateTime.Today.Month, 1);
            var idsResolvidosNoPeriodo = await db.Invoices
                .Where(i => i.ReferenceMonth == referencia && i.Amount > 0 && i.DeletedAt == null && i.BillId != null)
                .Select(i => i.BillId!.Value)
                .ToListAsync(stoppingToken);

            var pendentes = contas.Where(b => !idsResolvidosNoPeriodo.Contains(b.Id)).ToList();
            if (pendentes.Count < contas.Count)
            {
                logger.LogInformation(
                    "{Puladas} conta(s) já com fatura reconhecida na competência atual — busca pulada.",
                    contas.Count - pendentes.Count);
            }

            await ExecutarVarreduraAsync(
                pendentes,
                (bill, ct) => orquestrador.BuscarPorContaAsync(bill.UserId, bill, ct),
                stoppingToken);
        }
    }

    /// <summary>Calcula a espera até o próximo disparo diário no <paramref name="horario"/> informado.</summary>
    public static TimeSpan CalcularEsperaAteProximaExecucao(TimeOnly horario, DateTime agora)
    {
        var proxima = agora.Date + horario.ToTimeSpan();
        if (proxima <= agora)
        {
            proxima = proxima.AddDays(1);
        }

        return proxima - agora;
    }

    /// <summary>
    /// Processa cada conta isoladamente (uma falha não impede as demais) e loga o resumo.
    /// </summary>
    public async Task<(int Sucesso, int Falhas, int Total)> ExecutarVarreduraAsync(
        IReadOnlyList<Bill> contas,
        Func<Bill, CancellationToken, Task<Invoice?>> buscarFaturaAsync,
        CancellationToken ct)
    {
        logger.LogInformation("Varredura diária de faturas (AutoSearch) iniciada. {Quantidade} conta(s) marcada(s).", contas.Count);

        var sucesso = 0;
        var falhas = 0;

        foreach (var bill in contas)
        {
            try
            {
                var invoice = await buscarFaturaAsync(bill, ct);
                if (invoice is not null)
                {
                    sucesso++;
                }
            }
            catch (Exception ex)
            {
                falhas++;
                logger.LogError(ex, "Falha ao buscar fatura automática da conta {BillId} ({Nome}).", bill.Id, bill.Name);
            }
        }

        logger.LogInformation(
            "Varredura diária concluída: {Sucesso} fatura(s) registrada(s), {Falhas} falha(s), de {Total} conta(s).",
            sucesso, falhas, contas.Count);

        return (sucesso, falhas, contas.Count);
    }
}
