using System.ComponentModel.DataAnnotations;
using WebApp.Extensions;
using WebApp.Models.Enums;

namespace Tests.WebApp.Extensions;

public class EnumExtensionsTests
{
    [Theory]
    [InlineData(ETransactionTypes.Income, "Receita")]
    [InlineData(ETransactionTypes.Expense, "Despesa")]
    [InlineData(ETransactionTypes.Transfer, "Transferência")]
    public void GetDisplayName_ComAtributoDisplay_DeveRetornarOName(ETransactionTypes value, string esperado)
    {
        Assert.Equal(esperado, value.GetDisplayName());
    }

    private enum SemDisplay
    {
        ApenasNome,

        [Display(Name = "Com rótulo")]
        ComDisplay,
    }

    [Fact]
    public void GetDisplayName_SemAtributo_DeveUsarONomeDoMembro()
    {
        Assert.Equal(nameof(SemDisplay.ApenasNome), SemDisplay.ApenasNome.GetDisplayName());
    }

    [Fact]
    public void GetDisplayName_ComAtributo_DeveUsarOName()
    {
        Assert.Equal("Com rótulo", SemDisplay.ComDisplay.GetDisplayName());
    }
}
