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

    [Description("Extrai o valor, a data e a data de vencimento de uma fatura em PDF já baixada, " +
                 "a partir do caminho do arquivo. Lida com PDFs protegidos por senha automaticamente.")]
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

        var valor = ExtrairValor(texto);
        var vencimento = ExtrairData(texto, prioridade: ["vencimento", "venc."]);
        var data = ExtrairData(texto, prioridade: ["emiss", "emissão", "data"], excluir: vencimento);

        _logger.LogInformation("[Tool] ExtrairDadosFatura: valor={Valor} data={Data} vencimento={Venc}",
            valor, data, vencimento);

        return new FaturaInfo(valor, data, vencimento);
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
        // 1) Tenta valores rotulados ("valor a pagar", "total a pagar", "total", "valor do documento").
        var rotulado = ValorRotuladoRegex().Match(texto);
        if (rotulado.Success && TryParseValor(rotulado.Groups["v"].Value, out var v))
        {
            return v;
        }

        // 2) Fallback: maior valor monetário encontrado no documento.
        decimal? maior = null;
        foreach (Match m in ValorRegex().Matches(texto))
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

            var janela = texto.Substring(idx, Math.Min(60, texto.Length - idx));
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

    [GeneratedRegex(@"(?:valor\s+a\s+pagar|total\s+a\s+pagar|valor\s+do\s+documento|total)\D{0,20}R?\$?\s*(?<v>\d{1,3}(?:\.\d{3})*,\d{2})", RegexOptions.IgnoreCase)]
    private static partial Regex ValorRotuladoRegex();

    [GeneratedRegex(@"R\$\s*(?<v>\d{1,3}(?:\.\d{3})*,\d{2})", RegexOptions.IgnoreCase)]
    private static partial Regex ValorRegex();

    [GeneratedRegex(@"\d{2}/\d{2}/\d{2,4}")]
    private static partial Regex DataRegex();
}
