using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Services.WhatsApp;
using WebApp.Data;
using WebApp.Services;

namespace WebApp.Endpoints;

/// <summary>
/// Webhook de entrada da Evolution API. Monitora o grupo configurado e, ao chegar um PDF (boleto/fatura),
/// baixa o arquivo e aciona a importação (classificação + IA supervisora) em background. Responde 200 na hora
/// para a Evolution não reenviar por timeout; o processamento pesado roda num escopo próprio.
/// </summary>
public static class EvolutionWebhookEndpoints
{
    /// <summary>Referência mínima de uma mídia PDF recebida (id da mensagem + nome sugerido do arquivo).</summary>
    private sealed record PdfRecebido(string MessageId, string NomeArquivo);

    public static IEndpointRouteBuilder MapEvolutionWebhook(this IEndpointRouteBuilder app)
    {
        // {event?} porque a Evolution em modo "webhook-by-events" anexa o nome do evento ao path
        // (ex.: .../{token}/messages-upsert). O segmento é ignorado; o filtro de PDF trata cada evento.
        app.MapPost("/webhooks/evolution/{token}/{event?}", async (
            string token,
            HttpRequest request,
            EvolutionOptions options,
            IServiceScopeFactory scopeFactory,
            ILoggerFactory loggerFactory,
            CancellationToken ct) =>
        {
            var logger = loggerFactory.CreateLogger("EvolutionWebhook");

            // Token esperado: Evolution:WebhookToken se definido; senão a própria ApiKey global (que o Aspire
            // já injeta). Sem nenhum dos dois, o webhook fica desligado (não aceita nada).
            var esperado = string.IsNullOrWhiteSpace(options.WebhookToken) ? options.ApiKey : options.WebhookToken;
            if (string.IsNullOrWhiteSpace(esperado) || token != esperado)
            {
                return Results.Unauthorized();
            }

            JsonDocument payload;
            try
            {
                payload = await JsonDocument.ParseAsync(request.Body, cancellationToken: ct);
            }
            catch (JsonException ex)
            {
                logger.LogWarning(ex, "Webhook Evolution com corpo JSON inválido.");
                return Results.BadRequest();
            }

            var pdfs = ExtrairPdfsDoGrupo(payload.RootElement, options.RecipientNumber, logger).ToList();
            payload.Dispose();

            if (pdfs.Count == 0)
            {
                // Evento sem PDF do grupo (mensagem de texto, outro chat, etc.): nada a fazer.
                return Results.Ok(new { recebido = true, pdfs = 0 });
            }

            logger.LogInformation("Webhook Evolution: {Qtd} PDF(s) recebido(s) no grupo; processando em background.", pdfs.Count);

            // Fire-and-forget num escopo próprio (o escopo da requisição é descartado ao retornar).
            _ = Task.Run(() => ProcessarAsync(pdfs, scopeFactory, loggerFactory), CancellationToken.None);

            return Results.Ok(new { recebido = true, pdfs = pdfs.Count });
        });

        return app;
    }

    /// <summary>Percorre o payload da Evolution (messages.upsert) e coleta os PDFs vindos do grupo alvo.</summary>
    private static IEnumerable<PdfRecebido> ExtrairPdfsDoGrupo(JsonElement root, string grupoJid, ILogger logger)
    {
        if (!root.TryGetProperty("data", out var data))
        {
            yield break;
        }

        foreach (var msg in Mensagens(data))
        {
            if (!msg.TryGetProperty("key", out var key))
            {
                continue;
            }

            var remoteJid = Texto(key, "remoteJid");

            // Só o grupo configurado. Não filtramos por fromMe: o caso de uso é o próprio dono jogar os
            // boletos no grupo (fromMe=true). Sem risco de loop — o resumo que enviamos de volta é texto,
            // descartado pelo filtro de PDF adiante.
            if (!string.Equals(remoteJid, grupoJid, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var messageId = Texto(key, "id");
            if (string.IsNullOrWhiteSpace(messageId) || !msg.TryGetProperty("message", out var message))
            {
                continue;
            }

            // documentMessage direto ou aninhado em documentWithCaptionMessage.
            var doc = AcharDocumento(message);
            if (doc is not { } documento)
            {
                continue;
            }

            var mimetype = Texto(documento, "mimetype");
            var fileName = Texto(documento, "fileName") ?? Texto(documento, "title") ?? $"{messageId}.pdf";

            var ehPdf = (mimetype?.Contains("pdf", StringComparison.OrdinalIgnoreCase) ?? false)
                        || fileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase);

            if (ehPdf)
            {
                logger.LogInformation("Webhook Evolution: PDF '{Arquivo}' (msg {Id}) no grupo.", fileName, messageId);
                yield return new PdfRecebido(messageId, fileName);
            }
        }
    }

    private static IEnumerable<JsonElement> Mensagens(JsonElement data)
    {
        // data pode vir como objeto único, array de mensagens, ou objeto com "messages".
        if (data.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in data.EnumerateArray())
            {
                yield return item;
            }
        }
        else if (data.ValueKind == JsonValueKind.Object)
        {
            if (data.TryGetProperty("messages", out var messages) && messages.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in messages.EnumerateArray())
                {
                    yield return item;
                }
            }
            else
            {
                yield return data;
            }
        }
    }

    private static JsonElement? AcharDocumento(JsonElement message)
    {
        if (message.TryGetProperty("documentMessage", out var doc))
        {
            return doc;
        }

        if (message.TryGetProperty("documentWithCaptionMessage", out var wrapper)
            && wrapper.TryGetProperty("message", out var inner)
            && inner.TryGetProperty("documentMessage", out var innerDoc))
        {
            return innerDoc;
        }

        return null;
    }

    private static string? Texto(JsonElement el, string prop) =>
        el.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static async Task ProcessarAsync(
        IReadOnlyList<PdfRecebido> pdfs, IServiceScopeFactory scopeFactory, ILoggerFactory loggerFactory)
    {
        var logger = loggerFactory.CreateLogger("EvolutionWebhook");
        await using var scope = scopeFactory.CreateAsyncScope();
        var sp = scope.ServiceProvider;

        var options = sp.GetRequiredService<EvolutionOptions>();
        var wa = sp.GetRequiredService<EvolutionWhatsAppClient>();
        var orchestrator = sp.GetRequiredService<ImportacaoFaturaOrchestrator>();
        var db = sp.GetRequiredService<ApplicationDbContext>();

        var userId = await ResolverDonoAsync(options, db, logger);
        if (userId is null)
        {
            return;
        }

        Directory.CreateDirectory(options.DownloadDirectory);

        var caminhos = new List<string>();
        foreach (var pdf in pdfs)
        {
            var bytes = await wa.BaixarMidiaBase64Async(pdf.MessageId);
            if (bytes is null || bytes.Length == 0)
            {
                logger.LogWarning("Não foi possível baixar a mídia da mensagem {Id}.", pdf.MessageId);
                continue;
            }

            var caminho = CaminhoUnico(Path.Combine(options.DownloadDirectory, SanitizarNome(pdf.NomeArquivo)));
            await File.WriteAllBytesAsync(caminho, bytes);
            caminhos.Add(caminho);
        }

        if (caminhos.Count == 0)
        {
            return;
        }

        var resultado = await orchestrator.ImportarAsync(userId, caminhos);

        var mensagem = MontarResumo(resultado);
        if (mensagem is not null)
        {
            await wa.SendAlertAsync(mensagem);
        }
    }

    private static async Task<string?> ResolverDonoAsync(EvolutionOptions options, ApplicationDbContext db, ILogger logger)
    {
        if (!string.IsNullOrWhiteSpace(options.OwnerUserId))
        {
            return options.OwnerUserId;
        }

        var ids = await db.Users.Select(u => u.Id).Take(2).ToListAsync();
        if (ids.Count == 1)
        {
            return ids[0];
        }

        logger.LogError(
            "Webhook Evolution sem Evolution:OwnerUserId e {Qtd} usuário(s) cadastrado(s) — impossível decidir o dono das faturas.",
            ids.Count);
        return null;
    }

    /// <summary>Mensagem única para o grupo com os boletos que precisam de revisão; null quando não há pendências.</summary>
    private static string? MontarResumo(ResultadoImportacaoLote resultado)
    {
        var pendentes = resultado.Pendentes;
        if (pendentes.Count == 0)
        {
            return resultado.Anexadas > 0
                ? $"✅ {resultado.Anexadas} boleto(s) importado(s) e classificado(s) automaticamente."
                : null;
        }

        var sb = new StringBuilder();
        if (resultado.Anexadas > 0)
        {
            sb.AppendLine($"✅ {resultado.Anexadas} boleto(s) classificado(s) automaticamente.");
        }

        sb.AppendLine($"📎 {pendentes.Count} boleto(s) precisam de confirmação (salvos como avulsos):").AppendLine();
        foreach (var item in pendentes)
        {
            var valor = item.Valor is { } v ? v.ToString("C", System.Globalization.CultureInfo.GetCultureInfo("pt-BR")) : "valor a confirmar";
            var venc = item.Vencimento is { } d ? $" • venc {d:dd/MM}" : "";
            sb.AppendLine($"• {item.Arquivo}: {valor}{venc}");
        }

        sb.AppendLine().Append("Revise em *Faturas*.");
        return sb.ToString().TrimEnd();
    }

    private static string SanitizarNome(string nome)
    {
        var limpo = string.Concat(nome.Split(Path.GetInvalidFileNameChars()));
        if (string.IsNullOrWhiteSpace(limpo))
        {
            limpo = "boleto.pdf";
        }

        return limpo.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase) ? limpo : limpo + ".pdf";
    }

    private static string CaminhoUnico(string caminho)
    {
        if (!File.Exists(caminho))
        {
            return caminho;
        }

        var dir = Path.GetDirectoryName(caminho)!;
        var nome = Path.GetFileNameWithoutExtension(caminho);
        var ext = Path.GetExtension(caminho);
        return Path.Combine(dir, $"{nome}-{Guid.NewGuid():N}{ext}");
    }
}
