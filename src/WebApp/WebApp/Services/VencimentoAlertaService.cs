using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Services.WhatsApp;
using WebApp.Data;
using WebApp.Models;
using WebApp.Models.Enums;

namespace WebApp.Services;

/// <summary>Resultado de uma tentativa de alerta (pra logs e pro endpoint de teste manual).</summary>
public sealed record AlertaVencimentoResultado(bool Enviado, int Quantidade, string Mensagem);

/// <summary>
/// Monta e envia o digest de vencimentos por WhatsApp. Isolado do <see cref="VencimentoAlertaWorker"/>
/// pra poder ser acionado sob demanda (endpoint de teste em Development), não só no horário diário.
/// </summary>
public sealed class VencimentoAlertaService(
    ApplicationDbContext db,
    EvolutionWhatsAppClient whatsapp,
    VencimentoAlertaOptions options,
    ILogger<VencimentoAlertaService> logger)
{
    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    /// <summary>Busca as faturas pendentes vencidas/a vencer e envia o resumo. Não envia se não houver nenhuma.</summary>
    public async Task<AlertaVencimentoResultado> EnviarAsync(CancellationToken ct = default)
    {
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
            return new AlertaVencimentoResultado(false, 0, "");
        }

        var mensagem = MontarMensagem(faturas, hoje);
        var enviado = await whatsapp.SendAlertAsync(mensagem, ct);

        if (enviado)
        {
            logger.LogInformation("Alerta de vencimentos enviado: {Quantidade} fatura(s).", faturas.Count);
        }

        return new AlertaVencimentoResultado(enviado, faturas.Count, mensagem);
    }

    /// <summary>Monta a mensagem do WhatsApp (vencidas primeiro, depois a vencer). Público para teste.</summary>
    public static string MontarMensagem(IReadOnlyList<Invoice> faturas, DateOnly hoje)
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
