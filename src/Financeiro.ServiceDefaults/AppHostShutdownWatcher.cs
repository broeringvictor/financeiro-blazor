using System.Diagnostics;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Financeiro.ServiceDefaults;

/// <summary>
/// Monitora o processo do AppHost (PID em APPHOST_PID, injetado pelo Aspire) e encerra este
/// processo filho assim que o AppHost terminar — inclusive quando ele é fechado/finalizado
/// abruptamente — evitando processos órfãos.
/// </summary>
internal sealed class AppHostShutdownWatcher(
    IHostApplicationLifetime lifetime,
    ILogger<AppHostShutdownWatcher> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!int.TryParse(Environment.GetEnvironmentVariable("APPHOST_PID"), out var pid))
        {
            // Rodando fora do AppHost (ex.: standalone). Nada a monitorar.
            return;
        }

        Process appHost;
        try
        {
            appHost = Process.GetProcessById(pid);
        }
        catch (ArgumentException)
        {
            // AppHost já não existe — encerra imediatamente.
            lifetime.StopApplication();
            return;
        }

        try
        {
            await appHost.WaitForExitAsync(stoppingToken);
        }
        catch (OperationCanceledException)
        {
            // Shutdown normal deste processo; não faz nada.
            return;
        }

        logger.LogWarning("AppHost (PID {Pid}) encerrou. Finalizando este processo.", pid);
        lifetime.StopApplication();
    }
}
