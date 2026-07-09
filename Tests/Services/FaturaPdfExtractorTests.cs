using Services.Pdf;

namespace Tests.Services;

public class FaturaPdfExtractorTests
{
    private static readonly FaturaPdfExtractor Sut = new(new PdfOptions());

    [Fact]
    public void ExtrairDeTexto_ComValorRotuladoEDatas()
    {
        var texto = "Conta de energia\nValor a pagar R$ 187,42\nData de emissão 28/06/2026\nVencimento 10/07/2026";

        var info = Sut.ExtrairDeTexto(texto);

        Assert.Equal(187.42m, info.Valor);
        Assert.Equal(new DateOnly(2026, 6, 28), info.Data);
        Assert.Equal(new DateOnly(2026, 7, 10), info.Vencimento);
    }

    [Fact]
    public void ExtrairDeTexto_SemCifrao_UsaRotuloOuMaiorDecimal()
    {
        var texto = "Total a pagar 250,00 Vencimento: 05/08/2026";

        var info = Sut.ExtrairDeTexto(texto);

        Assert.Equal(250.00m, info.Valor);
        Assert.Equal(new DateOnly(2026, 8, 5), info.Vencimento);
    }

    [Fact]
    public void ExtrairDeTexto_ComMilhar_RetornaMaiorValor()
    {
        var texto = "Itens 12,50 e 30,00. Total R$ 1.234,56";

        var info = Sut.ExtrairDeTexto(texto);

        Assert.Equal(1234.56m, info.Valor);
    }

    [Fact]
    public void ExtrairDeTexto_BoletoCelesc_IgnoraSubtotalEDebitosEUsaTotal()
    {
        // Trechos reais de uma fatura Celesc: dois SUBTOTAL parciais e a soma de débitos em atraso
        // ("Totalizando") não podem ser confundidos com o TOTAL a pagar (R$ 361,81).
        var texto =
            "SUBTOTAL 299,50\n"
            + "SUBTOTAL 62,31\n"
            + "Atenção! Contas em atraso: 03/2025 R$655,40 = Totalizando R$2.209,54.\n"
            + "TOTAL 361,81\n"
            + "Referência 06/2026 Vencimento 15/07/2026\n"
            + "Total a Pagar (R$) 361,81";

        var info = Sut.ExtrairDeTexto(texto);

        Assert.Equal(361.81m, info.Valor);
        Assert.Equal(new DateOnly(2026, 7, 15), info.Vencimento);
    }

    [Fact]
    public void ExtrairDeTexto_ComLinhaDigitavelBancaria_DecodificaValorEVencimento()
    {
        // Linha digitável real do boleto Bradesco (Celesc): valor R$ 361,81, vencimento 15/07/2026.
        // Aqui o SUBTOTAL/Totalizando existem no texto, mas a linha digitável tem prioridade.
        var texto =
            "SUBTOTAL 299,50\n"
            + "= Totalizando R$2.209,54.\n"
            + "TOTAL 361,81\n"
            + "237-2 23790.3480090130.06399436013.613603315080000036181\n";

        var info = Sut.ExtrairDeTexto(texto);

        Assert.Equal(361.81m, info.Valor);
        Assert.Equal(new DateOnly(2026, 7, 15), info.Vencimento);
    }

    [Fact]
    public void DecodificarLinhaDigitavelBancaria_SemLinhaValida_RetornaNull()
    {
        Assert.Null(Sut.DecodificarLinhaDigitavelBancaria("apenas texto e 12345 sem boleto"));
    }

    [Fact]
    public void ExtrairDeTexto_ComChaveNfeEProtocolo_NaoConfundeComLinhaDigitavel()
    {
        // Regressão: a Chave de Acesso da NF-e (44 dígitos) + protocolo formavam uma janela de 47 dígitos que
        // passava só nos mód-10 e devolvia valor absurdo (R$ 42.260.002,23). O DV geral mód-11 deve rejeitá-la
        // e o decoder deve achar a linha digitável real (R$ 361,81).
        var texto =
            "Chave de Acesso:\n"
            + "4226.0608.3367.8300.0190.6600.1095.1902.9010.2343.7926\n"
            + "Protocolo de Autorização: 3.422.600.022.372.999 - 15/06/2026 às 16:07\n"
            + "237-2 23790.3480090130.06399436013.613603315080000036181\n";

        var info = Sut.ExtrairDeTexto(texto);

        Assert.Equal(361.81m, info.Valor);
        Assert.Equal(new DateOnly(2026, 7, 15), info.Vencimento);
    }

    [Fact]
    public void ExtrairDeTexto_Competencia_UsaReferenciaProximaDoVencimentoEIgnoraAtrasos()
    {
        // Vencimento 15/07/2026; a competência é 06/2026. As referências de débitos em atraso (03/2025,
        // 07/2025, 02/2026) devem ser descartadas por estarem longe do vencimento.
        var texto =
            "Referência 06/2026\n"
            + "Contas em atraso nas referência(s): 03/2025 07/2025 02/2026\n"
            + "Vencimento 15/07/2026";

        var info = Sut.ExtrairDeTexto(texto);

        Assert.Equal(new DateOnly(2026, 7, 15), info.Vencimento);
        Assert.Equal(new DateOnly(2026, 6, 1), info.Competencia);
    }

    [Fact]
    public void ExtrairDeTexto_TextoVazio_RetornaErro()
    {
        var info = Sut.ExtrairDeTexto("   ");

        Assert.Null(info.Valor);
        Assert.NotNull(info.Erro);
    }
}
