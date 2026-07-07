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

    /// <summary>
    /// Busca as faturas pendentes que vencem exatamente numa das datas-alvo (hoje e/ou N dias antes,
    /// conforme <see cref="VencimentoAlertaOptions.DiasAntecedencia"/>) e, se
    /// <see cref="VencimentoAlertaOptions.IncluirVencidas"/>, também as já vencidas ainda pendentes.
    /// Envia um único resumo com todas. Não envia se não houver nenhuma.
    /// </summary>
    public async Task<AlertaVencimentoResultado> EnviarAsync(CancellationToken ct = default)
    {
        var hoje = DateOnly.FromDateTime(DateTime.Today);
        var alvos = DatasAlvo(hoje, options.DiasAntecedencia);

        if (alvos.Count == 0 && !options.IncluirVencidas)
        {
            logger.LogInformation("Nenhuma data-alvo configurada (VencimentoAlerta:DiasAntecedencia) — alerta não enviado.");
            return new AlertaVencimentoResultado(false, 0, "");
        }

        var incluirVencidas = options.IncluirVencidas;
        var query = db.Invoices
            .Include(i => i.Bill)
            .Where(i => i.Status == EInvoiceStatus.Pending && i.DeletedAt == null);

        query = incluirVencidas
            ? query.Where(i => alvos.Contains(i.DueDate) || i.DueDate < hoje)
            : query.Where(i => alvos.Contains(i.DueDate));

        var faturas = await query.OrderBy(i => i.DueDate).ToListAsync(ct);

        if (faturas.Count == 0)
        {
            logger.LogInformation(
                "Nenhuma fatura pendente nas datas-alvo ({Datas}){Vencidas} — alerta não enviado.",
                string.Join(", ", alvos.Select(d => d.ToString("dd/MM"))),
                incluirVencidas ? " nem vencidas" : "");
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

    /// <summary>
    /// Datas em que uma fatura deve ser alertada, a partir de <paramref name="hoje"/> e dos dias de
    /// antecedência (0 = no dia). Ignora offsets negativos e remove duplicatas. Público para teste.
    /// </summary>
    public static List<DateOnly> DatasAlvo(DateOnly hoje, IEnumerable<int> diasAntecedencia) =>
        diasAntecedencia
            .Where(d => d >= 0)
            .Distinct()
            .Select(d => hoje.AddDays(d))
            .OrderBy(d => d)
            .ToList();

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
