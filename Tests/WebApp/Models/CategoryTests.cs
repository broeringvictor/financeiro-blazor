using WebApp.Models;
using WebApp.Models.Enums;

namespace Tests.WebApp.Models;

public class CategoryTests
{
    private const string UserId = "user-123";

    [Fact]
    public void Create_Principal_DefineValoresEIsRoot()
    {
        var categoria = new Category(UserId, "Saúde", ETransactionTypes.Expense);

        Assert.Equal(UserId, categoria.UserId);
        Assert.Equal("Saúde", categoria.Name);
        Assert.Equal(ETransactionTypes.Expense, categoria.Type);
        Assert.Null(categoria.ParentId);
        Assert.True(categoria.IsRoot);
        Assert.NotEqual(Guid.Empty, categoria.Id);
    }

    [Fact]
    public void Create_Subcategoria_HerdaTipoDoPaiEDefineParentId()
    {
        var pai = new Category(UserId, "Saúde", ETransactionTypes.Expense);

        // O tipo informado (Income) é ignorado: a subcategoria herda o do pai (Expense).
        var sub = new Category(UserId, "Farmácia", ETransactionTypes.Income, pai);

        Assert.Equal(ETransactionTypes.Expense, sub.Type);
        Assert.Equal(pai.Id, sub.ParentId);
        Assert.False(sub.IsRoot);
    }

    [Fact]
    public void Create_SubDeSub_DeveLancar()
    {
        var pai = new Category(UserId, "Saúde", ETransactionTypes.Expense);
        var sub = new Category(UserId, "Farmácia", ETransactionTypes.Expense, pai);

        Assert.Throws<ArgumentException>(() => new Category(UserId, "Genéricos", ETransactionTypes.Expense, sub));
    }

    [Fact]
    public void Create_NomeComEspacos_RemoveEspacos()
    {
        var categoria = new Category(UserId, "  Lazer  ", ETransactionTypes.Expense);

        Assert.Equal("Lazer", categoria.Name);
    }

    [Fact]
    public void Edit_RenomeiaEMarcaAtualizado()
    {
        var categoria = new Category(UserId, "Saude", ETransactionTypes.Expense);
        var antes = categoria.UpdatedAt;

        categoria.Edit("Saúde");

        Assert.Equal("Saúde", categoria.Name);
        Assert.True(categoria.UpdatedAt >= antes);
    }
}
