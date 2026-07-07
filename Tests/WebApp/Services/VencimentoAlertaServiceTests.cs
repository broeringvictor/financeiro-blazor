using WebApp.Models;
using WebApp.Services;

namespace Tests.WebApp.Services;

public class VencimentoAlertaServiceTests
{
    private const string UserId = "user-123";

    [Fact]
    public void DatasAlvo_PadraoNoDiaEDoisDiasAntes()
    {
        var hoje = new DateOnly(2026, 7, 7);

        var datas = VencimentoAlertaService.DatasAlvo(hoje, new[] { 2, 0 });

        // No dia e dois dias antes (ordenadas). Não inclui o dia intermediário.
        Assert.Equal(new[] { hoje, hoje.AddDays(2) }, datas);
    }

    [Fact]
    public void DatasAlvo_IgnoraNegativosERemoveDuplicatas()
    {
        var hoje = new DateOnly(2026, 7, 7);

        var datas = VencimentoAlertaService.DatasAlvo(hoje, new[] { 0, 0, 2, -1 });

        Assert.Equal(new[] { hoje, hoje.AddDays(2) }, datas);
    }

    [Fact]
    public void MontarMensagem_VariasFaturas_EmUmaSoMensagem()
    {
        var hoje = new DateOnly(2026, 7, 7);
        var venceHoje = new Invoice(UserId, null, new DateOnly(2026, 7, 1), 100m, hoje);
        var venceEmDois = new Invoice(UserId, null, new DateOnly(2026, 7, 1), 200m, hoje.AddDays(2));

        var mensagem = VencimentoAlertaService.MontarMensagem(new[] { venceHoje, venceEmDois }, hoje);

        // Uma única string (uma mensagem) contendo ambas as faturas.
        Assert.Contains("vence *hoje* (07/07)", mensagem);
        Assert.Contains("vence 09/07", mensagem);
        Assert.Contains("R$", mensagem);
    }

    [Fact]
    public void MontarMensagem_FaturaVencida_MarcaComoVencida()
    {
        var hoje = new DateOnly(2026, 7, 7);
        var vencida = new Invoice(UserId, null, new DateOnly(2026, 7, 1), 150m, hoje.AddDays(-3));

        var mensagem = VencimentoAlertaService.MontarMensagem(new[] { vencida }, hoje);

        Assert.Contains("VENCIDA em 04/07", mensagem);
    }
}
