using System.ComponentModel;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using UglyToad.PdfPig;

namespace Services.Pdf;

/// <summary>Configuração de leitura de PDFs de faturas (seção "Pdf").</summary>
public sealed class PdfOptions
{
    /// <summary>Senha usada para abrir faturas protegidas, quando necessário.</summary>
    public string Password { get; set; } = string.Empty;
}

/// <summary>Dados extraídos de uma fatura em PDF.</summary>
public sealed record FaturaInfo(decimal? Valor, DateOnly? Data, DateOnly? Vencimento, string? Erro = null);

/// <summary>
/// Carga estruturada de uma fatura para ingestão no banco (consumida pelo WebApp).
/// Reúne os dados do e-mail (fornecedor, messageId) com os extraídos do PDF.
/// </summary>
public sealed record FaturaExtraida(
    string? BillerName,
    decimal? Valor,
    DateOnly? Emissao,
    DateOnly? Vencimento,
    string? SourceEmailMessageId,
    string? PdfPath,
    string? TextoBruto);

/// <summary>
/// Abre um PDF de fatura (usando senha, se preciso) e extrai valor, data e data de vencimento.
/// </summary>
public sealed partial class FaturaPdfExtractor(PdfOptions options, ILogger<FaturaPdfExtractor>? logger = null)
{
    private readonly ILogger _logger = logger ?? NullLogger<FaturaPdfExtractor>.Instance;

    [Description("PASSO FINAL: extrai valor, data de emissão e vencimento de um PDF de fatura já baixado " +
                 "(use o caminho retornado por BaixarAnexosPdf). Lida com PDF protegido por senha automaticamente. " +
                 "Prefira esta tool a ler o corpo do e-mail para obter os valores.")]
    public FaturaInfo ExtrairDadosFatura(
        [Description("Caminho do arquivo PDF da fatura (obtido em BaixarAnexosPdf).")]
        string caminhoPdf)
    {
        _logger.LogInformation("[Tool] ExtrairDadosFatura: {Caminho}", caminhoPdf);

        if (!File.Exists(caminhoPdf))
        {
            return new FaturaInfo(null, null, null, $"Arquivo não encontrado: {caminhoPdf}");
        }

        string texto;
        try
        {
            texto = ExtrairTexto(caminhoPdf);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Tool] ExtrairDadosFatura: falha ao abrir/ler o PDF.");
            return new FaturaInfo(null, null, null, $"Falha ao ler o PDF: {ex.Message}");
        }

        var info = ExtrairDeTexto(texto);

        _logger.LogInformation(
            "[Tool] ExtrairDadosFatura: textoLen={Len} valor={Valor} data={Data} vencimento={Venc} | amostra: {Amostra}",
            texto.Length, info.Valor, info.Data, info.Vencimento, Resumo(texto));

        return info;
    }

    /// <summary>Extrai valor/datas de um texto qualquer (PDF ou corpo de e-mail).</summary>
    public FaturaInfo ExtrairDeTexto(string? texto)
    {
        if (string.IsNullOrWhiteSpace(texto))
        {
            return new FaturaInfo(null, null, null, "Texto vazio.");
        }

        var valor = ExtrairValor(texto);
        var vencimento = ExtrairData(texto, prioridade: ["vencimento", "vencto", "venc.", "venc", "pagar até", "pagamento até"]);
        var data = ExtrairData(texto, prioridade: ["emiss", "emissão", "data de emissão", "data"], excluir: vencimento);

        return new FaturaInfo(valor, data, vencimento);
    }

    private static string Resumo(string texto)
    {
        var limpo = texto.Replace('\n', ' ').Replace('\r', ' ').Trim();
        return limpo.Length <= 300 ? limpo : limpo[..300];
    }

    private string ExtrairTexto(string caminhoPdf)
    {
        var parsingOptions = string.IsNullOrEmpty(options.Password)
            ? new ParsingOptions()
            : new ParsingOptions { Passwords = [options.Password] };

        using var doc = PdfDocument.Open(caminhoPdf, parsingOptions);

        var sb = new StringBuilder();
        foreach (var page in doc.GetPages())
        {
            sb.AppendLine(page.Text);
        }

        return sb.ToString();
    }

    private static decimal? ExtrairValor(string texto)
    {
        // 1) Valor rotulado ("valor a pagar", "total a pagar", "total", "valor do documento", "valor cobrado").
        var rotulado = ValorRotuladoRegex().Match(texto);
        if (rotulado.Success && TryParseValor(rotulado.Groups["v"].Value, out var v))
        {
            return v;
        }

        // 2) Maior valor precedido de "R$".
        if (MaiorValor(ValorComRsRegex().Matches(texto)) is { } comRs)
        {
            return comRs;
        }

        // 3) Último recurso: maior número no formato monetário (1.234,56 / 187,42) do documento.
        return MaiorValor(ValorDecimalRegex().Matches(texto));
    }

    private static decimal? MaiorValor(MatchCollection matches)
    {
        decimal? maior = null;
        foreach (Match m in matches)
        {
            if (TryParseValor(m.Groups["v"].Value, out var atual) && (maior is null || atual > maior))
            {
                maior = atual;
            }
        }

        return maior;
    }

    private static DateOnly? ExtrairData(string texto, string[] prioridade, DateOnly? excluir = null)
    {
        // Procura uma data próxima de uma das palavras-chave prioritárias.
        foreach (var chave in prioridade)
        {
            var idx = texto.IndexOf(chave, StringComparison.OrdinalIgnoreCase);
            if (idx < 0)
            {
                continue;
            }

            var janela = texto.Substring(idx, Math.Min(80, texto.Length - idx));
            var m = DataRegex().Match(janela);
            if (m.Success && TryParseData(m.Value, out var d) && d != excluir)
            {
                return d;
            }
        }

        // Fallback: primeira data válida diferente da que deve ser excluída.
        foreach (Match m in DataRegex().Matches(texto))
        {
            if (TryParseData(m.Value, out var d) && d != excluir)
            {
                return d;
            }
        }

        return null;
    }

    private static bool TryParseValor(string bruto, out decimal valor)
    {
        var normalizado = bruto.Trim().Replace(".", string.Empty).Replace(",", ".");
        return decimal.TryParse(normalizado, NumberStyles.Number, CultureInfo.InvariantCulture, out valor);
    }

    private static bool TryParseData(string bruto, out DateOnly data) =>
        DateOnly.TryParseExact(bruto, ["dd/MM/yyyy", "dd/MM/yy"], CultureInfo.InvariantCulture, DateTimeStyles.None, out data);

    [GeneratedRegex(@"(?:valor\s*a\s*pagar|total\s*a\s*pagar|valor\s*do\s*documento|valor\s*cobrado|total)\D{0,15}R?\$?\s*(?<v>\d{1,3}(?:\.\d{3})*,\d{2})", RegexOptions.IgnoreCase)]
    private static partial Regex ValorRotuladoRegex();

    [GeneratedRegex(@"R\$\s*(?<v>\d{1,3}(?:\.\d{3})*,\d{2})", RegexOptions.IgnoreCase)]
    private static partial Regex ValorComRsRegex();

    [GeneratedRegex(@"(?<v>\d{1,3}(?:\.\d{3})*,\d{2})")]
    private static partial Regex ValorDecimalRegex();

    [GeneratedRegex(@"\d{2}/\d{2}/\d{2,4}")]
    private static partial Regex DataRegex();
}
