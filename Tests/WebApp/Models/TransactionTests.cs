using WebApp.Models;
using WebApp.Models.Enums;

namespace Tests.WebApp.Models;

public class TransactionTests
{
    private const string UserId = "user-123";

    // ---------- Create ----------

    [Fact]
    public void Create_DeveDefinirTodosOsValores()
    {
        var transaction = new Transaction(
            UserId, ETransactionTypes.Expense, ETransactionCategory.Rent, "Aluguel", "Pagamento mensal", 1500m);

        Assert.Equal(UserId, transaction.UserId);
        Assert.Equal(ETransactionTypes.Expense, transaction.Type);
        Assert.Equal(ETransactionCategory.Rent, transaction.Category);
        Assert.Equal("Aluguel", transaction.Title);
        Assert.Equal("Pagamento mensal", transaction.Description);
        Assert.Equal(1500m, transaction.Amount);
    }

    [Fact]
    public void Create_DeveGerarIdEDatas()
    {
        var antes = DateTime.UtcNow;

        var transaction = new Transaction(
            UserId, ETransactionTypes.Income, ETransactionCategory.Salary, "Salário", null, 5000m);

        Assert.NotEqual(Guid.Empty, transaction.Id);
        Assert.InRange(transaction.CreatedAt, antes, DateTime.UtcNow);
        Assert.True(transaction.UpdatedAt >= transaction.CreatedAt);
        Assert.Null(transaction.DeletedAt);
    }

    [Fact]
    public void Create_DeveRemoverEspacosDoTitulo()
    {
        var transaction = new Transaction(
            UserId, ETransactionTypes.Income, ETransactionCategory.Salary, "  Salário  ", null, 5000m);

        Assert.Equal("Salário", transaction.Title);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Create_DeveNormalizarDescricaoVaziaParaNull(string? descricao)
    {
        var transaction = new Transaction(
            UserId, ETransactionTypes.Income, ETransactionCategory.Salary, "Salário", descricao, 5000m);

        Assert.Null(transaction.Description);
    }

    [Fact]
    public void Create_ComCategoriaDeOutroTipo_DeveLancar()
    {
        // Salary é categoria de Income, não de Expense.
        var ex = Assert.Throws<ArgumentException>(() => new Transaction(
            UserId, ETransactionTypes.Expense, ETransactionCategory.Salary, "Aluguel", null, 100m));

        Assert.Equal("category", ex.ParamName);
    }

    // ---------- Edit ----------

    private static Transaction CriarParaEdicao() =>
        new(
            id: Guid.CreateVersion7(),
            userId: UserId,
            createdAt: DateTime.UtcNow.AddDays(-1),
            type: ETransactionTypes.Expense,
            category: ETransactionCategory.Groceries,
            title: "Original",
            description: "Descrição original",
            amount: 100m);

    [Fact]
    public void Edit_DeveAtualizarApenasOsCamposInformados()
    {
        var transaction = CriarParaEdicao();

        transaction.Edit(title: "Atualizado", amount: 250m);

        Assert.Equal("Atualizado", transaction.Title);
        Assert.Equal(250m, transaction.Amount);
        // Não informados permanecem inalterados.
        Assert.Equal(ETransactionTypes.Expense, transaction.Type);
        Assert.Equal(ETransactionCategory.Groceries, transaction.Category);
        Assert.Equal("Descrição original", transaction.Description);
    }

    [Fact]
    public void Edit_DeveTrocarTipoECategoriaJuntos()
    {
        var transaction = CriarParaEdicao();

        transaction.Edit(type: ETransactionTypes.Income, category: ETransactionCategory.Freelance);

        Assert.Equal(ETransactionTypes.Income, transaction.Type);
        Assert.Equal(ETransactionCategory.Freelance, transaction.Category);
    }

    [Fact]
    public void Edit_NovaCategoriaIncompativelComOTipoAtual_DeveLancar()
    {
        var transaction = CriarParaEdicao(); // Expense

        // Salary não pertence a Expense; não passar novo tipo.
        Assert.Throws<ArgumentException>(() => transaction.Edit(category: ETransactionCategory.Salary));
    }

    [Fact]
    public void Edit_NovoTipoSemNovaCategoria_DeveLancarSeCategoriaAtualNaoPertence()
    {
        var transaction = CriarParaEdicao(); // Expense + Groceries

        // Trocar só o tipo deixaria Groceries inválido para Income.
        Assert.Throws<ArgumentException>(() => transaction.Edit(type: ETransactionTypes.Income));
    }

    [Fact]
    public void Edit_ComMudancas_DeveAtualizarUpdatedAt()
    {
        var transaction = CriarParaEdicao();
        var updatedAtAnterior = transaction.UpdatedAt;

        transaction.Edit(amount: 999m);

        Assert.True(transaction.UpdatedAt >= updatedAtAnterior);
        Assert.Equal(999m, transaction.Amount);
    }

    [Fact]
    public void Edit_SemArgumentos_NaoDeveMarcarComoAtualizado()
    {
        var transaction = CriarParaEdicao();
        var updatedAtAnterior = transaction.UpdatedAt;

        transaction.Edit();

        Assert.Equal(updatedAtAnterior, transaction.UpdatedAt);
    }

    [Fact]
    public void Edit_ComValoresIguais_NaoDeveMarcarComoAtualizado()
    {
        var transaction = CriarParaEdicao();
        var updatedAtAnterior = transaction.UpdatedAt;

        transaction.Edit(
            type: ETransactionTypes.Expense,
            category: ETransactionCategory.Groceries,
            title: "Original",
            description: "Descrição original",
            amount: 100m);

        Assert.Equal(updatedAtAnterior, transaction.UpdatedAt);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Edit_TituloVazioOuEspacos_DeveSerIgnorado(string titulo)
    {
        var transaction = CriarParaEdicao();

        transaction.Edit(title: titulo);

        Assert.Equal("Original", transaction.Title);
    }

    [Fact]
    public void Edit_DeveRemoverEspacosDoNovoTitulo()
    {
        var transaction = CriarParaEdicao();

        transaction.Edit(title: "  Novo título  ");

        Assert.Equal("Novo título", transaction.Title);
    }

    [Fact]
    public void Construtor_DeEdicao_DevePreservarIdECreatedAt()
    {
        var id = Guid.CreateVersion7();
        var createdAt = DateTime.UtcNow.AddDays(-5);

        var transaction = new Transaction(id, UserId, createdAt, title: "Qualquer");

        Assert.Equal(id, transaction.Id);
        Assert.Equal(UserId, transaction.UserId);
        Assert.Equal(createdAt, transaction.CreatedAt);
    }
}
