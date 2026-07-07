using Services.WhatsApp;

namespace WebApp.Services;

/// <summary>Configuração do alerta diário de vencimentos por WhatsApp (seção "VencimentoAlerta").</summary>
public sealed class VencimentoAlertaOptions
{
    /// <summary>Liga/desliga o alerta diário.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Horário do dia (local) em que o alerta é disparado.</summary>
    public TimeOnly HoraExecucao { get; set; } = new(8, 0);

    /// <summary>
    /// Dias de antecedência em que uma fatura entra no alerta (0 = no próprio dia do vencimento).
    /// Padrão: alerta no dia e 2 dias antes.
    /// </summary>
    public int[] DiasAntecedencia { get; set; } = [2, 0];
}

/// <summary>
/// Uma vez por dia dispara o <see cref="VencimentoAlertaService"/>, que envia por WhatsApp um resumo
/// das faturas pendentes já vencidas ou a vencer nos próximos dias. É um digest diário (não marca nada
/// como "notificado"): lembra o usuário todo dia até a fatura ser paga.
/// </summary>
public sealed class VencimentoAlertaWorker(
    ILogger<VencimentoAlertaWorker> logger,
    IServiceScopeFactory scopeFactory,
    EvolutionOptions evolutionOptions,
    VencimentoAlertaOptions options) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Enabled)
        {
            logger.LogInformation("Alerta de vencimentos por WhatsApp DESABILITADO (VencimentoAlerta:Enabled=false).");
            return;
        }

        if (!evolutionOptions.IsConfigured || string.IsNullOrWhiteSpace(evolutionOptions.RecipientNumber))
        {
            logger.LogWarning(
                "Alerta de vencimentos HABILITADO, mas Evolution não está configurada (BaseUrl/ApiKey/RecipientNumber). " +
                "O worker roda mas não conseguirá enviar até configurar a seção \"Evolution\".");
        }

        logger.LogInformation(
            "Alerta de vencimentos HABILITADO. Horário diário: {Horario} | Antecedência: {Dias} dia(s).",
            options.HoraExecucao, string.Join(", ", options.DiasAntecedencia));

        while (!stoppingToken.IsCancellationRequested)
        {
            var espera = AutoSearchFaturasWorker.CalcularEsperaAteProximaExecucao(options.HoraExecucao, DateTime.Now);
            logger.LogInformation("Próximo alerta de vencimentos em {Espera}.", espera);

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

            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var service = scope.ServiceProvider.GetRequiredService<VencimentoAlertaService>();
                await service.EnviarAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Falha ao montar/enviar o alerta de vencimentos.");
            }
        }
    }
}
