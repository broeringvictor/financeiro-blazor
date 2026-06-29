namespace Services;

/// <summary>Configuração da varredura automática de faturas (seção "Worker").</summary>
public sealed class BackgroundScanOptions
{
    /// <summary>Liga/desliga a varredura periódica em background.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Intervalo entre varreduras, em horas.</summary>
    public int IntervalHours { get; set; } = 6;

    /// <summary>Espera inicial antes da primeira varredura (evita disparar no boot).</summary>
    public int InitialDelaySeconds { get; set; } = 30;

    /// <summary>Consulta enviada ao agente em cada varredura.</summary>
    public string Consulta { get; set; } =
        "Procure pelas faturas mais recentes da Celesc (conta de luz) e da Águas de Palhoça " +
        "(conta de água) nos meus e-mails e liste o valor e a data de vencimento de cada uma.";
}

/// <summary>
/// Varredura periódica de faturas em background. Compartilha o mesmo <see cref="AgentAccessor"/>
/// usado pelo endpoint HTTP, então o agente é criado/autenticado uma única vez.
/// </summary>
public sealed class Worker(
    ILogger<Worker> logger,
    AgentAccessor accessor,
    BackgroundScanOptions options) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Enabled)
        {
            logger.LogInformation("Varredura automática de faturas DESABILITADA (Worker:Enabled=false).");
            return;
        }

        logger.LogInformation(
            "Varredura automática HABILITADA. Intervalo: {Intervalo}h | Espera inicial: {Espera}s",
            Math.Max(1, options.IntervalHours), options.InitialDelaySeconds);

        try
        {
            await Task.Delay(TimeSpan.FromSeconds(options.InitialDelaySeconds), stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        using var timer = new PeriodicTimer(TimeSpan.FromHours(Math.Max(1, options.IntervalHours)));

        do
        {
            try
            {
                logger.LogInformation("Varredura iniciada. Consulta: {Consulta}", options.Consulta);
                var inicio = TimeProvider.System.GetTimestamp();

                var agente = await accessor.GetAsync(stoppingToken);
                var resposta = await agente.RunAsync(options.Consulta, cancellationToken: stoppingToken);

                var duracao = TimeProvider.System.GetElapsedTime(inicio);
                logger.LogInformation("Varredura concluída em {Duracao}. Resposta do agente:\n{Resposta}",
                    duracao, resposta.Text);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Falha na varredura automática de faturas.");
            }

            logger.LogInformation("Próxima varredura em {Intervalo}h.", Math.Max(1, options.IntervalHours));
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
