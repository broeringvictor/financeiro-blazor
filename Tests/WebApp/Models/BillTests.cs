using WebApp.Models;
using WebApp.Models.Enums;
using WebApp.Models.ValueObjects;

namespace Tests.WebApp.Models;

public class BillTests
{
    private const string UserId = "user-123";

    private static RecurrenceRule Mensal() =>
        new(ERecurrenceFrequency.Monthly, 1, 10, new DateOnly(2026, 1, 10));

    private static Bill CriarConta() =>
        new(UserId, "Luz - Celesc", "Celesc", ETransactionCategory.Utilities, Mensal());

    [Fact]
    public void Create_DeveDefinirValores()
    {
        var bill = CriarConta();

        Assert.Equal(UserId, bill.UserId);
        Assert.Equal("Luz - Celesc", bill.Name);
        Assert.Equal("Celesc", bill.BillerName);
        Assert.Equal(ETransactionCategory.Utilities, bill.Category);
        Assert.True(bill.Active);
        Assert.NotEqual(Guid.Empty, bill.Id);
    }

    [Fact]
    public void Create_ComCategoriaNaoDespesa_DeveLancar()
    {
        Assert.Throws<ArgumentException>(() =>
            new Bill(UserId, "Salário", "Empresa", ETransactionCategory.Salary, Mensal()));
    }

    [Fact]
    public void Create_ComValorFixoNegativo_DeveLancar()
    {
        Assert.Throws<ArgumentException>(() =>
            new Bill(UserId, "Aluguel", "Imobiliária", ETransactionCategory.Rent, Mensal(), fixedAmount: -1m));
    }

    [Theory]
    [InlineData("noreply@celesc.com.br", "Qualquer assunto", true)] // nome do fornecedor no remetente
    [InlineData("x@y.com", "Boletim Celesc", true)]                 // nome do fornecedor no assunto
    [InlineData("x@y.com", "Sua fatura chegou", false)]              // sem o nome do fornecedor
    [InlineData("x@y.com", "Newsletter", false)]                    // não casa
    public void MatchesEmail_AvaliaFornecedorNoRemetenteOuAssunto(string from, string subject, bool esperado)
    {
        var bill = CriarConta();

        Assert.Equal(esperado, bill.MatchesEmail(from, subject));
    }

    [Fact]
    public void Edit_AlteraApenasInformados()
    {
        var bill = CriarConta();
        var antes = bill.UpdatedAt;

        bill.Edit(name: "Energia - Celesc", active: false);

        Assert.Equal("Energia - Celesc", bill.Name);
        Assert.False(bill.Active);
        Assert.Equal("Celesc", bill.BillerName); // inalterado
        Assert.True(bill.UpdatedAt >= antes);
    }

    [Fact]
    public void Create_DefineBuscaAutomaticaEConsulta()
    {
        var bill = new Bill(UserId, "Luz - Celesc", "Celesc", ETransactionCategory.Utilities, Mensal(),
            autoSearch: true, searchQuery: "Celesc fatura newer_than:120d");

        Assert.True(bill.AutoSearch);
        Assert.Equal("Celesc fatura newer_than:120d", bill.SearchQuery);
    }

    [Fact]
    public void Edit_AtualizaBuscaAutomaticaEConsulta()
    {
        var bill = CriarConta();

        bill.Edit(autoSearch: true, searchQuery: "boleto Celesc");

        Assert.True(bill.AutoSearch);
        Assert.Equal("boleto Celesc", bill.SearchQuery);
    }

    [Fact]
    public void Deactivate_DesativaConta()
    {
        var bill = CriarConta();

        bill.Deactivate();

        Assert.False(bill.Active);
    }

    [Fact]
    public void Delete_MarcaComoExcluida()
    {
        var bill = CriarConta();

        bill.Delete();

        Assert.NotNull(bill.DeletedAt);
    }
}
