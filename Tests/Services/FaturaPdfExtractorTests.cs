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
    public void ExtrairDeTexto_TextoVazio_RetornaErro()
    {
        var info = Sut.ExtrairDeTexto("   ");

        Assert.Null(info.Valor);
        Assert.NotNull(info.Erro);
    }
}
