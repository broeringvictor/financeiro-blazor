using Microsoft.EntityFrameworkCore;
using WebApp.Data;
using WebApp.Models;

namespace WebApp.Services;

/// <summary>
/// "Verificador" diário: para cada conta ativa, garante que as faturas em aberto da recorrência já existam,
/// gerando-as diretamente (sem depender de e-mail/boleto) via <see cref="GeracaoFaturaService"/>. Mantém o
/// buffer rolante das contas sem prazo e completa as ocorrências das contas com prazo. Uma falha numa conta
/// não interrompe as demais.
/// </summary>
public sealed class GeracaoFaturasWorker(
    ILogger<GeracaoFaturasWorker> logger,
    IServiceScopeFactory scopeFactory,
    GeracaoFaturaOptions options) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Enabled)
        {
            logger.LogInformation("Geração automática de faturas DESABILITADA (GeracaoFaturas:Enabled=false).");
            return;
        }

        logger.LogInformation("Geração automática de faturas HABILITADA. Horário diário: {Horario}", options.HoraExecucao);

        while (!stoppingToken.IsCancellationRequested)
        {
            var espera = CalcularEsperaAteProximaExecucao(options.HoraExecucao, DateTime.Now);
            logger.LogInformation("Próxima geração de faturas em {Espera}.", espera);

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
            var gerador = scope.ServiceProvider.GetRequiredService<GeracaoFaturaService>();

            var contas = await db.Bills
                .Where(b => b.Active && b.DeletedAt == null)
                .ToListAsync(stoppingToken);

            await GerarParaContasAsync(gerador, contas, stoppingToken);
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

    /// <summary>Gera as faturas de cada conta isoladamente (uma falha não impede as demais) e loga o resumo.</summary>
    public async Task<(int Criadas, int Falhas, int Total)> GerarParaContasAsync(
        GeracaoFaturaService gerador,
        IReadOnlyList<Bill> contas,
        CancellationToken ct)
    {
        logger.LogInformation("Geração diária de faturas iniciada. {Quantidade} conta(s) ativa(s).", contas.Count);

        var criadas = 0;
        var falhas = 0;

        foreach (var bill in contas)
        {
            try
            {
                criadas += await gerador.GerarPendentesAsync(bill.UserId, bill, ct);
            }
            catch (Exception ex)
            {
                falhas++;
                logger.LogError(ex, "Falha ao gerar faturas da conta {BillId} ({Nome}).", bill.Id, bill.Name);
            }
        }

        logger.LogInformation(
            "Geração diária concluída: {Criadas} fatura(s) criada(s), {Falhas} falha(s), de {Total} conta(s).",
            criadas, falhas, contas.Count);

        return (criadas, falhas, contas.Count);
    }
}
