using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Services.WhatsApp;
using WebApp.Data;
using WebApp.Models.Enums;

namespace WebApp.Services;

/// <summary>Configuração do alerta diário de vencimentos por WhatsApp (seção "VencimentoAlerta").</summary>
public sealed class VencimentoAlertaOptions
{
    /// <summary>Liga/desliga o alerta diário.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Horário do dia (local) em que o alerta é disparado.</summary>
    public TimeOnly HoraExecucao { get; set; } = new(8, 0);

    /// <summary>Faturas que vencem dentro de N dias entram no alerta (além das já vencidas).</summary>
    public int DiasAntecedencia { get; set; } = 3;
}

/// <summary>
/// Uma vez por dia, envia por WhatsApp um resumo das faturas pendentes já vencidas ou a vencer nos
/// próximos <see cref="VencimentoAlertaOptions.DiasAntecedencia"/> dias. É um digest diário (não marca
/// nada como "notificado"): lembra o usuário todo dia até a fatura ser paga. Só envia se houver algo.
/// </summary>
public sealed class VencimentoAlertaWorker(
    ILogger<VencimentoAlertaWorker> logger,
    IServiceScopeFactory scopeFactory,
    EvolutionOptions evolutionOptions,
    VencimentoAlertaOptions options) : BackgroundService
{
    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

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
            options.HoraExecucao, options.DiasAntecedencia);

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
                await EnviarAlertaAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Falha ao montar/enviar o alerta de vencimentos.");
            }
        }
    }

    private async Task EnviarAlertaAsync(CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var whatsapp = scope.ServiceProvider.GetRequiredService<EvolutionWhatsAppClient>();

        var hoje = DateOnly.FromDateTime(DateTime.Today);
        var limite = hoje.AddDays(Math.Max(0, options.DiasAntecedencia));

        var faturas = await db.Invoices
            .Include(i => i.Bill)
            .Where(i => i.Status == EInvoiceStatus.Pending && i.DeletedAt == null && i.DueDate <= limite)
            .OrderBy(i => i.DueDate)
            .ToListAsync(ct);

        if (faturas.Count == 0)
        {
            logger.LogInformation("Nenhuma fatura pendente vencida ou a vencer até {Limite} — alerta não enviado.", limite);
            return;
        }

        var mensagem = MontarMensagem(faturas, hoje);
        var enviado = await whatsapp.SendAlertAsync(mensagem, ct);

        if (enviado)
        {
            logger.LogInformation("Alerta de vencimentos enviado: {Quantidade} fatura(s).", faturas.Count);
        }
    }

    /// <summary>Monta a mensagem do WhatsApp (vencidas primeiro, depois a vencer). Público para teste.</summary>
    public static string MontarMensagem(IReadOnlyList<Models.Invoice> faturas, DateOnly hoje)
    {
        var sb = new StringBuilder();
        sb.AppendLine("🔔 *Contas a vencer*").AppendLine();

        foreach (var f in faturas)
        {
            var nome = string.IsNullOrWhiteSpace(f.Bill?.Name) ? "Fatura avulsa" : f.Bill!.Name;
            var valor = f.Amount > 0 ? f.Amount.ToString("C", PtBr) : "valor a confirmar";

            if (f.DueDate < hoje)
            {
                sb.AppendLine($"⚠️ *{nome}*: {valor} — VENCIDA em {f.DueDate:dd/MM}");
            }
            else if (f.DueDate == hoje)
            {
                sb.AppendLine($"❗ *{nome}*: {valor} — vence *hoje* ({f.DueDate:dd/MM})");
            }
            else
            {
                sb.AppendLine($"• *{nome}*: {valor} — vence {f.DueDate:dd/MM}");
            }

            if (!string.IsNullOrWhiteSpace(f.Bill?.PixKey))
            {
                sb.AppendLine($"   Pix: {f.Bill!.PixKey}");
            }
        }

        return sb.ToString().TrimEnd();
    }
}
