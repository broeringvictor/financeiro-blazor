using System.ComponentModel;
using System.Text;
using Google.Apis.Gmail.v1;
using Google.Apis.Gmail.v1.Data;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Services.Gmail;

/// <summary>Resumo enxuto de um e-mail, retornado nas buscas.</summary>
public sealed record ResumoEmail(string Id, string Assunto, string Remetente, string Data, string Trecho);

/// <summary>Conteúdo completo de um e-mail.</summary>
public sealed record DetalheEmail(string Id, string Assunto, string Remetente, string Data, string Corpo);

/// <summary>
/// Ferramentas (tools) que o agente de IA pode chamar para localizar contas/faturas no Gmail.
/// Cada método público é exposto como uma function tool via AIFunctionFactory.Create.
/// </summary>
public sealed class GmailTools(
    GmailService gmail,
    string user = "me",
    ILogger? logger = null,
    string? downloadDirectory = null)
{
    private readonly ILogger _logger = logger ?? NullLogger.Instance;

    private readonly string _downloadDirectory =
        downloadDirectory ?? Path.Combine(Path.GetTempPath(), "financeiro-faturas");

    [Description("Pesquisa e-mails no Gmail relacionados a contas, faturas ou boletos. " +
                 "Use a sintaxe de busca do Gmail no parâmetro 'consulta'. " +
                 "Retorna apenas resumos; use ObterDetalhesEmail para ler o conteúdo completo.")]
    public async Task<IReadOnlyList<ResumoEmail>> BuscarEmailsDeContas(
        [Description("Consulta no formato de busca do Gmail, ex.: 'fatura OR boleto OR \"conta a pagar\" newer_than:30d'.")]
        string consulta,
        [Description("Número máximo de e-mails a retornar (1 a 50).")]
        int maxResultados = 10,
        CancellationToken ct = default)
    {
        var max = Math.Clamp(maxResultados, 1, 50);
        _logger.LogInformation("[Tool] BuscarEmailsDeContas: consulta=\"{Consulta}\" max={Max}", consulta, max);

        var listRequest = gmail.Users.Messages.List(user);
        listRequest.Q = consulta;
        listRequest.MaxResults = max;

        var list = await listRequest.ExecuteAsync(ct);
        if (list.Messages is null || list.Messages.Count == 0)
        {
            _logger.LogInformation("[Tool] BuscarEmailsDeContas: nenhum e-mail encontrado.");
            return [];
        }

        var resumos = new List<ResumoEmail>(list.Messages.Count);
        foreach (var item in list.Messages)
        {
            var getRequest = gmail.Users.Messages.Get(user, item.Id);
            getRequest.Format = UsersResource.MessagesResource.GetRequest.FormatEnum.Metadata;
            getRequest.MetadataHeaders = new Google.Apis.Util.Repeatable<string>(["Subject", "From", "Date"]);

            var msg = await getRequest.ExecuteAsync(ct);
            resumos.Add(new ResumoEmail(
                msg.Id,
                Cabecalho(msg, "Subject"),
                Cabecalho(msg, "From"),
                Cabecalho(msg, "Date"),
                msg.Snippet ?? string.Empty));
        }

        _logger.LogInformation("[Tool] BuscarEmailsDeContas: {Qtd} e-mail(s) encontrado(s) -> {Assuntos}",
            resumos.Count, string.Join(" | ", resumos.Select(r => r.Assunto)));

        return resumos;
    }

    [Description("Obtém o conteúdo completo (assunto, remetente, data e corpo em texto) de um e-mail pelo seu ID.")]
    public async Task<DetalheEmail> ObterDetalhesEmail(
        [Description("ID da mensagem do Gmail, obtido em BuscarEmailsDeContas.")]
        string messageId,
        CancellationToken ct = default)
    {
        _logger.LogInformation("[Tool] ObterDetalhesEmail: messageId={MessageId}", messageId);

        var getRequest = gmail.Users.Messages.Get(user, messageId);
        getRequest.Format = UsersResource.MessagesResource.GetRequest.FormatEnum.Full;

        var msg = await getRequest.ExecuteAsync(ct);
        _logger.LogInformation("[Tool] ObterDetalhesEmail: assunto=\"{Assunto}\"", Cabecalho(msg, "Subject"));

        return new DetalheEmail(
            msg.Id,
            Cabecalho(msg, "Subject"),
            Cabecalho(msg, "From"),
            Cabecalho(msg, "Date"),
            ExtrairCorpoTexto(msg.Payload));
    }

    [Description("Baixa os anexos em PDF de um e-mail (por exemplo, a fatura) para uma pasta temporária " +
                 "e retorna os caminhos dos arquivos salvos. Use depois de identificar o e-mail correto.")]
    public async Task<IReadOnlyList<string>> BaixarAnexosPdf(
        [Description("ID da mensagem do Gmail cujos anexos PDF devem ser baixados.")]
        string messageId,
        CancellationToken ct = default)
    {
        _logger.LogInformation("[Tool] BaixarAnexosPdf: messageId={MessageId}", messageId);

        var getRequest = gmail.Users.Messages.Get(user, messageId);
        getRequest.Format = UsersResource.MessagesResource.GetRequest.FormatEnum.Full;
        var msg = await getRequest.ExecuteAsync(ct);

        var anexos = new List<MessagePart>();
        ColetarAnexosPdf(msg.Payload, anexos);

        if (anexos.Count == 0)
        {
            _logger.LogInformation("[Tool] BaixarAnexosPdf: nenhum PDF encontrado no e-mail.");
            return [];
        }

        Directory.CreateDirectory(_downloadDirectory);
        var caminhos = new List<string>(anexos.Count);

        foreach (var parte in anexos)
        {
            byte[] dados;
            if (!string.IsNullOrEmpty(parte.Body?.Data))
            {
                dados = DecodificarBase64UrlBytes(parte.Body.Data);
            }
            else if (!string.IsNullOrEmpty(parte.Body?.AttachmentId))
            {
                var anexo = await gmail.Users.Messages.Attachments
                    .Get(user, messageId, parte.Body.AttachmentId).ExecuteAsync(ct);
                dados = DecodificarBase64UrlBytes(anexo.Data);
            }
            else
            {
                continue;
            }

            var nome = SanitizarNomeArquivo(parte.Filename, messageId);
            var caminho = CaminhoUnico(Path.Combine(_downloadDirectory, nome));
            await File.WriteAllBytesAsync(caminho, dados, ct);

            caminhos.Add(caminho);
            _logger.LogInformation("[Tool] BaixarAnexosPdf: salvo {Caminho} ({Bytes} bytes)", caminho, dados.Length);
        }

        return caminhos;
    }

    private static void ColetarAnexosPdf(MessagePart? part, List<MessagePart> acc)
    {
        if (part is null)
            return;

        var ehPdf = string.Equals(part.MimeType, "application/pdf", StringComparison.OrdinalIgnoreCase)
                    || (part.Filename?.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase) ?? false);

        if (ehPdf && part.Body is not null && (part.Body.AttachmentId is not null || part.Body.Data is not null))
        {
            acc.Add(part);
        }

        if (part.Parts is not null)
        {
            foreach (var p in part.Parts)
            {
                ColetarAnexosPdf(p, acc);
            }
        }
    }

    private static byte[] DecodificarBase64UrlBytes(string base64Url)
    {
        var normalizado = base64Url.Replace('-', '+').Replace('_', '/');
        switch (normalizado.Length % 4)
        {
            case 2: normalizado += "=="; break;
            case 3: normalizado += "="; break;
        }

        return Convert.FromBase64String(normalizado);
    }

    private static string SanitizarNomeArquivo(string? filename, string fallback)
    {
        var nome = string.IsNullOrWhiteSpace(filename) ? $"{fallback}.pdf" : filename;

        foreach (var c in Path.GetInvalidFileNameChars())
        {
            nome = nome.Replace(c, '_');
        }

        if (!nome.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
        {
            nome += ".pdf";
        }

        return nome;
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

        for (var i = 1; ; i++)
        {
            var candidato = Path.Combine(dir, $"{nome} ({i}){ext}");
            if (!File.Exists(candidato))
            {
                return candidato;
            }
        }
    }

    private static string Cabecalho(Message msg, string nome) =>
        msg.Payload?.Headers?.FirstOrDefault(h =>
            string.Equals(h.Name, nome, StringComparison.OrdinalIgnoreCase))?.Value ?? string.Empty;

    /// <summary>Percorre a árvore de partes MIME procurando o corpo em text/plain.</summary>
    private static string ExtrairCorpoTexto(MessagePart? payload)
    {
        if (payload is null)
            return string.Empty;

        if (string.Equals(payload.MimeType, "text/plain", StringComparison.OrdinalIgnoreCase)
            && payload.Body?.Data is { Length: > 0 } data)
        {
            return DecodificarBase64Url(data);
        }

        if (payload.Parts is not null)
        {
            foreach (var parte in payload.Parts)
            {
                var corpo = ExtrairCorpoTexto(parte);
                if (!string.IsNullOrWhiteSpace(corpo))
                    return corpo;
            }
        }

        return string.Empty;
    }

    private static string DecodificarBase64Url(string base64Url)
    {
        var normalizado = base64Url.Replace('-', '+').Replace('_', '/');
        switch (normalizado.Length % 4)
        {
            case 2: normalizado += "=="; break;
            case 3: normalizado += "="; break;
        }

        return Encoding.UTF8.GetString(Convert.FromBase64String(normalizado));
    }
}
