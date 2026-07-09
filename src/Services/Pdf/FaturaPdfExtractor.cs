using System.ComponentModel;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Writer;

namespace Services.Pdf;

/// <summary>Configuração de leitura de PDFs de faturas (seção "Pdf").</summary>
public sealed class PdfOptions
{
    /// <summary>Senha usada para abrir faturas protegidas, quando necessário.</summary>
    public string Password { get; set; } = string.Empty;
}

/// <summary>Dados extraídos de uma fatura em PDF.</summary>
public sealed record FaturaInfo(
    decimal? Valor,
    DateOnly? Data,
    DateOnly? Vencimento,
    string? Erro = null,
    DateOnly? Competencia = null);

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
    string? TextoBruto,
    DateOnly? Competencia = null);

/// <summary>Leitura de faturas em PDF. Abstrai <see cref="FaturaPdfExtractor"/> para permitir fakes em teste.</summary>
public interface IFaturaLeitorPdf
{
    /// <summary>Extrai valor, emissão e vencimento do PDF.</summary>
    FaturaInfo ExtrairDadosFatura(string caminhoPdf);

    /// <summary>Texto bruto do PDF (para classificação/fallback).</summary>
    string ObterTextoBruto(string caminhoPdf);
}

/// <summary>
/// Abre um PDF de fatura (usando senha, se preciso) e extrai valor, data e data de vencimento.
/// </summary>
public sealed partial class FaturaPdfExtractor(PdfOptions options, ILogger<FaturaPdfExtractor>? logger = null)
    : IFaturaLeitorPdf
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

        // Achou e conseguiu abrir a fatura: regrava sem a senha, pra poder ser aberta depois sem
        // precisar dela (ex.: visualização no navegador). Best-effort — não falha a extração.
        SalvarSemSenha(caminhoPdf);

        var info = ExtrairDeTexto(texto);

        _logger.LogInformation(
            "[Tool] ExtrairDadosFatura: textoLen={Len} valor={Valor} data={Data} vencimento={Venc} | amostra: {Amostra}",
            texto.Length, info.Valor, info.Data, info.Vencimento, Resumo(texto));

        return info;
    }

    /// <summary>Texto bruto do PDF, sem tentar extrair campos — usado pelo fallback via agente de IA.</summary>
    public string ObterTextoBruto(string caminhoPdf) => ExtrairTexto(caminhoPdf);

    /// <summary>Extrai valor/datas de um texto qualquer (PDF ou corpo de e-mail).</summary>
    public FaturaInfo ExtrairDeTexto(string? texto)
    {
        if (string.IsNullOrWhiteSpace(texto))
        {
            return new FaturaInfo(null, null, null, "Texto vazio.");
        }

        // Fonte primária (layout-agnóstica): a linha digitável do boleto codifica valor e vencimento de
        // forma padronizada. Vale pra qualquer boleto bancário, sem depender do layout da concessionária.
        var boleto = DecodificarLinhaDigitavelBancaria(texto);

        // Fallbacks (regex sobre rótulos do texto) só entram quando a linha digitável não resolve o campo.
        var valor = boleto?.Valor ?? ExtrairValor(texto);
        var vencimento = boleto?.Vencimento
                         ?? ExtrairData(texto, prioridade: ["vencimento", "vencto", "venc.", "venc", "pagar até", "pagamento até"]);
        var data = ExtrairData(texto, prioridade: ["emiss", "emissão", "data de emissão", "data"], excluir: vencimento);
        var competencia = ExtrairCompetencia(texto, vencimento);

        return new FaturaInfo(valor, data, vencimento, Competencia: competencia);
    }

    private static string Resumo(string texto)
    {
        var limpo = texto.Replace('\n', ' ').Replace('\r', ' ').Trim();
        return limpo.Length <= 300 ? limpo : limpo[..300];
    }

    /// <summary>
    /// Reabre o PDF já com a senha e regrava o arquivo, no mesmo caminho, sem a proteção.
    /// Não faz nada se não houver senha configurada (arquivo já está aberto).
    /// </summary>
    private void SalvarSemSenha(string caminhoPdf)
    {
        if (string.IsNullOrEmpty(options.Password))
        {
            return;
        }

        try
        {
            byte[] semSenha;
            using (var doc = PdfDocument.Open(caminhoPdf, new ParsingOptions { Passwords = [options.Password] }))
            using (var builder = new PdfDocumentBuilder())
            {
                for (var pagina = 1; pagina <= doc.NumberOfPages; pagina++)
                {
                    builder.AddPage(doc, pagina);
                }

                semSenha = builder.Build();
            }

            File.WriteAllBytes(caminhoPdf, semSenha);
            _logger.LogInformation("[Tool] ExtrairDadosFatura: PDF regravado sem senha ({Caminho}).", caminhoPdf);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[Tool] ExtrairDadosFatura: falha ao regravar o PDF sem senha ({Caminho}).", caminhoPdf);
        }
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

    /// <summary>Data-base do fator de vencimento (FEBRABAN): 07/10/1997.</summary>
    private static readonly DateOnly FatorVencimentoBase = new(1997, 10, 7);

    /// <summary>
    /// Decodifica valor e vencimento a partir da linha digitável de um boleto BANCÁRIO (47 dígitos, layout
    /// FEBRABAN) encontrada no texto — independente do visual da fatura. Localiza a linha por uma janela de 47
    /// dígitos que passe nos dígitos verificadores (mód-10) dos três primeiros campos, o que descarta outros
    /// números do documento. Retorna null quando não há linha digitável bancária reconhecível (aí o chamador
    /// cai nos fallbacks de regex/IA). Boletos de arrecadação (48 dígitos, começam com "8") ainda não entram aqui.
    /// </summary>
    public (decimal? Valor, DateOnly? Vencimento)? DecodificarLinhaDigitavelBancaria(string? texto)
    {
        if (string.IsNullOrEmpty(texto))
        {
            return null;
        }

        foreach (Match trecho in TrechoNumericoRegex().Matches(texto))
        {
            var digitos = NaoDigitoRegex().Replace(trecho.Value, string.Empty);
            for (var i = 0; i + 47 <= digitos.Length; i++)
            {
                var linha = digitos.AsSpan(i, 47);
                if (!LinhaDigitavelBancariaValida(linha))
                {
                    continue;
                }

                // Campo 5 (14 dígitos): fator de vencimento (4) + valor em centavos (10).
                var fator = int.Parse(linha.Slice(33, 4));
                var centavos = long.Parse(linha.Slice(37, 10));

                var valor = centavos == 0 ? (decimal?)null : centavos / 100m;
                var vencimento = fator == 0 ? (DateOnly?)null : VencimentoDoFator(fator);
                return (valor, vencimento);
            }
        }

        return null;
    }

    /// <summary>
    /// Valida a linha digitável bancária (47 dígitos): os DVs mód-10 dos campos 1–3 E o DV geral mód-11 do
    /// código de barras. O mód-11 é o discriminador forte — sem ele, sequências longas do PDF (ex.: a Chave de
    /// Acesso da NF-e) podem formar uma janela que passa só nos mód-10 e produzir valor/vencimento absurdos.
    /// </summary>
    private static bool LinhaDigitavelBancariaValida(ReadOnlySpan<char> linha) =>
        CampoMod10Valido(linha.Slice(0, 10))     // campo 1: 9 dados + DV
        && CampoMod10Valido(linha.Slice(10, 11)) // campo 2: 10 dados + DV
        && CampoMod10Valido(linha.Slice(21, 11)) // campo 3: 10 dados + DV
        && DvGeralValido(linha);

    /// <summary>Reconstrói o código de barras (44) a partir da linha digitável (47) e confere o DV geral (mód-11).</summary>
    private static bool DvGeralValido(ReadOnlySpan<char> linha)
    {
        Span<char> barra = stackalloc char[44];
        linha.Slice(0, 4).CopyTo(barra);              // pos 1-4: banco + moeda
        barra[4] = linha[32];                         // pos 5: DV geral
        linha.Slice(33, 14).CopyTo(barra.Slice(5));   // pos 6-19: fator + valor
        linha.Slice(4, 5).CopyTo(barra.Slice(19));    // pos 20-24: campo 1 (resto)
        linha.Slice(10, 10).CopyTo(barra.Slice(24));  // pos 25-34: campo 2
        linha.Slice(21, 10).CopyTo(barra.Slice(34));  // pos 35-44: campo 3

        var soma = 0;
        var peso = 2;
        for (var i = 43; i >= 0; i--)
        {
            if (i == 4)
            {
                continue; // pula a própria posição do DV geral
            }

            soma += (barra[i] - '0') * peso;
            peso = peso == 9 ? 2 : peso + 1;
        }

        var dv = 11 - soma % 11;
        if (dv is 0 or 10 or 11)
        {
            dv = 1;
        }

        return dv == barra[4] - '0';
    }

    /// <summary>Confere o dígito verificador mód-10 de um campo (último char é o DV).</summary>
    private static bool CampoMod10Valido(ReadOnlySpan<char> campo)
    {
        var soma = 0;
        var peso = 2;
        for (var i = campo.Length - 2; i >= 0; i--)
        {
            if (!char.IsDigit(campo[i]))
            {
                return false;
            }

            var produto = (campo[i] - '0') * peso;
            soma += produto > 9 ? produto - 9 : produto; // soma dos algarismos do produto
            peso = peso == 2 ? 1 : 2;
        }

        var dv = (10 - soma % 10) % 10;
        return char.IsDigit(campo[^1]) && dv == campo[^1] - '0';
    }

    /// <summary>
    /// Converte o fator de vencimento em data. Base 07/10/1997; trata o reset FEBRABAN de 22/02/2025 (o fator
    /// voltou a 1000 após atingir 9999) escolhendo, entre o ciclo antigo e o novo (+9000 dias), a data mais
    /// próxima de hoje — que é sempre a correta para boletos em circulação.
    /// </summary>
    private static DateOnly VencimentoDoFator(int fator)
    {
        var cicloAntigo = FatorVencimentoBase.AddDays(fator);
        var cicloNovo = FatorVencimentoBase.AddDays(fator + 9000);
        var hoje = DateOnly.FromDateTime(DateTime.Today).DayNumber;

        return Math.Abs(cicloAntigo.DayNumber - hoje) <= Math.Abs(cicloNovo.DayNumber - hoje)
            ? cicloAntigo
            : cicloNovo;
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

    /// <summary>
    /// Competência (mês de referência) a partir de datas no formato MM/AAAA no texto. O documento traz várias
    /// (débitos em atraso, avisos), então desambigua pela proximidade ao <paramref name="vencimento"/>: a
    /// competência de uma conta mensal fica entre ~4 meses antes e o próprio mês do vencimento. Sem vencimento,
    /// não arrisca (devolve null e o chamador cai no fallback emissão/vencimento).
    /// </summary>
    private static DateOnly? ExtrairCompetencia(string texto, DateOnly? vencimento)
    {
        if (vencimento is not { } venc)
        {
            return null;
        }

        var alvo = new DateOnly(venc.Year, venc.Month, 1);

        DateOnly? melhor = null;
        var menorDist = int.MaxValue;

        foreach (Match m in CompetenciaRegex().Matches(texto))
        {
            if (!int.TryParse(m.Groups["mm"].Value, out var mes) || mes is < 1 or > 12
                || !int.TryParse(m.Groups["yyyy"].Value, out var ano))
            {
                continue;
            }

            var candidato = new DateOnly(ano, mes, 1);
            var dist = alvo.DayNumber - candidato.DayNumber; // > 0 quando o candidato é anterior ao vencimento

            // Janela plausível: do mês do vencimento (dist ~-31) até ~4 meses antes (dist ~130).
            if (dist is < -31 or > 130)
            {
                continue;
            }

            var abs = Math.Abs(dist);
            if (abs < menorDist)
            {
                menorDist = abs;
                melhor = candidato;
            }
        }

        return melhor;
    }

    private static bool TryParseValor(string bruto, out decimal valor)
    {
        var normalizado = bruto.Trim().Replace(".", string.Empty).Replace(",", ".");
        return decimal.TryParse(normalizado, NumberStyles.Number, CultureInfo.InvariantCulture, out valor);
    }

    private static bool TryParseData(string bruto, out DateOnly data) =>
        DateOnly.TryParseExact(bruto, ["dd/MM/yyyy", "dd/MM/yy"], CultureInfo.InvariantCulture, DateTimeStyles.None, out data);

    // \b em volta do grupo: "total" precisa ser palavra inteira, senão casaria "SUBTOTAL" (subtotal parcial)
    // ou "Totalizando" (soma de débitos em atraso) — ambos valores errados. Só "TOTAL"/"Total a Pagar" valem.
    [GeneratedRegex(@"\b(?:valor\s*a\s*pagar|total\s*a\s*pagar|valor\s*do\s*documento|valor\s*cobrado|total)\b\D{0,15}R?\$?\s*(?<v>\d{1,3}(?:\.\d{3})*,\d{2})", RegexOptions.IgnoreCase)]
    private static partial Regex ValorRotuladoRegex();

    [GeneratedRegex(@"R\$\s*(?<v>\d{1,3}(?:\.\d{3})*,\d{2})", RegexOptions.IgnoreCase)]
    private static partial Regex ValorComRsRegex();

    [GeneratedRegex(@"(?<v>\d{1,3}(?:\.\d{3})*,\d{2})")]
    private static partial Regex ValorDecimalRegex();

    [GeneratedRegex(@"\d{2}/\d{2}/\d{2,4}")]
    private static partial Regex DataRegex();

    // Competência MM/AAAA. O lookbehind (?<![\d/]) evita casar o "MM/AAAA" de dentro de uma data dd/MM/AAAA.
    [GeneratedRegex(@"(?<![\d/])(?<mm>\d{2})/(?<yyyy>\d{4})")]
    private static partial Regex CompetenciaRegex();

    // Trechos que podem conter uma linha digitável: dígitos com pontos/espaços entre eles, com folga (>=45
    // caracteres) pra abrigar os 47 dígitos mesmo com prefixos/separadores. O NaoDigitoRegex limpa depois.
    [GeneratedRegex(@"[\d][\d.\s]{44,}[\d]")]
    private static partial Regex TrechoNumericoRegex();

    [GeneratedRegex(@"\D")]
    private static partial Regex NaoDigitoRegex();
}
