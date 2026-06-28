using WebApp.Models.Enums;

namespace Tests.WebApp.Models.Enums;

public class TransactionCategoriesTests
{
    [Theory]
    [InlineData(ETransactionTypes.Income, ETransactionCategory.Salary)]
    [InlineData(ETransactionTypes.Expense, ETransactionCategory.Rent)]
    [InlineData(ETransactionTypes.Transfer, ETransactionCategory.BetweenAccounts)]
    public void Belongs_QuandoCategoriaPertenceAoTipo_DeveSerTrue(
        ETransactionTypes type, ETransactionCategory category)
    {
        Assert.True(TransactionCategories.Belongs(type, category));
    }

    [Theory]
    [InlineData(ETransactionTypes.Income, ETransactionCategory.Rent)]
    [InlineData(ETransactionTypes.Expense, ETransactionCategory.Salary)]
    [InlineData(ETransactionTypes.Transfer, ETransactionCategory.Groceries)]
    public void Belongs_QuandoCategoriaNaoPertenceAoTipo_DeveSerFalse(
        ETransactionTypes type, ETransactionCategory category)
    {
        Assert.False(TransactionCategories.Belongs(type, category));
    }

    [Fact]
    public void For_DeveRetornarApenasCategoriasDoTipo()
    {
        var doIncome = TransactionCategories.For(ETransactionTypes.Income);

        Assert.NotEmpty(doIncome);
        Assert.All(doIncome, c => Assert.True(TransactionCategories.Belongs(ETransactionTypes.Income, c)));
    }

    [Fact]
    public void For_CadaCategoria_DevePertencerAExatamenteUmTipo()
    {
        var tipos = Enum.GetValues<ETransactionTypes>();

        foreach (var category in Enum.GetValues<ETransactionCategory>())
        {
            var donos = tipos.Count(t => TransactionCategories.Belongs(t, category));
            Assert.Equal(1, donos);
        }
    }
}
