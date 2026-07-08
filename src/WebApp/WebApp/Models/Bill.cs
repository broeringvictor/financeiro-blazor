using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using WebApp.Models.Enums;
using WebApp.Models.Shared;
using WebApp.Models.ValueObjects;

namespace WebApp.Models;

/// <summary>
/// Lançamento recorrente do usuário: uma saída (ex.: "Luz - Celesc") ou uma entrada (ex.: "Salário").
/// Define a fonte/fornecedor, a categoria (cujo <see cref="ETransactionTypes"/> determina se é entrada ou saída),
/// a recorrência e as regras para reconhecer a fatura no e-mail. As cobranças concretas são <see cref="Invoice"/>.
/// </summary>
public class Bill : BaseModel
{
    /// <summary>Id do usuário dono da conta (FK para AspNetUsers).</summary>
    [Required]
    public string UserId { get; private set; } = string.Empty;

    /// <summary>Nome amigável da conta (3–150 caracteres).</summary>
    [DisplayName("Nome")]
    [Length(3, 150, ErrorMessage = "O nome deve ter entre 3 e 150 caracteres.")]
    public string Name
    {
        get;
        private set => field = value.Trim();
    } = string.Empty;

    /// <summary>Fornecedor/credor (ex.: "Celesc", "Águas de Palhoça").</summary>
    [DisplayName("Fornecedor")]
    [Length(2, 150, ErrorMessage = "O fornecedor deve ter entre 2 e 150 caracteres.")]
    public string BillerName
    {
        get;
        private set => field = value.Trim();
    } = string.Empty;

    /// <summary>
    /// Categoria do lançamento (despesa ou receita, principal ou subcategoria). Nula apenas em linhas legadas
    /// ainda não migradas; todo lançamento criado pelo domínio recebe uma categoria obrigatória.
    /// </summary>
    public Guid? CategoryId { get; private set; }

    /// <summary>Navegação para a categoria.</summary>
    public Category? Category { get; private set; }

    /// <summary>
    /// Tipo do lançamento (entrada/saída), derivado da categoria. Null apenas em linhas legadas sem categoria
    /// — que são tratadas como despesa.
    /// </summary>
    public ETransactionTypes? Type => Category?.Type;

    /// <summary>
    /// Categoria legada (enum) da versão anterior, mantida apenas para o backfill de <see cref="CategoryId"/>.
    /// Não é mais usada pelo domínio e será removida numa migração futura.
    /// </summary>
    public ETransactionCategory LegacyCategory { get; private set; }

    /// <summary>Regra de recorrência (owned).</summary>
    public RecurrenceRule Recurrence { get; private set; } = null!;

    /// <summary>Valor fixo, apenas para recorrências de valor constante (ex.: aluguel). Null = valor varia.</summary>
    [DisplayName("Valor fixo")]
    public decimal? FixedAmount { get; private set; }

    /// <summary>Conta ativa (entra na varredura/expectativa).</summary>
    public bool Active { get; private set; } = true;

    /// <summary>Quando true, o agente procura faturas desta conta automaticamente.</summary>
    [DisplayName("Busca automática")]
    public bool AutoSearch { get; private set; }

    /// <summary>Consulta/contexto que o agente usa para procurar a fatura no e-mail e associá-la.</summary>
    [DisplayName("Consulta de busca")]
    public string? SearchQuery
    {
        get;
        private set => field = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    /// <summary>Chave/conta Pix do fornecedor, para contas pagas manualmente (sem boleto por e-mail).</summary>
    [DisplayName("Chave Pix do fornecedor")]
    public string? PixKey
    {
        get;
        private set => field = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    /// <summary>Faturas (cobranças concretas) emitidas para esta conta (1:N).</summary>
    public ICollection<Invoice> Invoices { get; private set; } = new List<Invoice>();

    /// <summary>Construtor usado pelo Entity Framework.</summary>
    private Bill() { }

    /// <summary>Cria uma nova conta recorrente.</summary>
    public Bill(
        string userId,
        string name,
        string billerName,
        Category category,
        RecurrenceRule recurrence,
        decimal? fixedAmount = null,
        bool autoSearch = false,
        string? searchQuery = null,
        string? pixKey = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        ArgumentNullException.ThrowIfNull(recurrence);
        EnsureLancamentoCategory(category);
        EnsureFixedAmountValid(fixedAmount);

        UserId = userId;
        Name = name;
        BillerName = billerName;
        CategoryId = category.Id;
        // Não atribuímos a navegação Category aqui de propósito: a categoria vem AsNoTracking da UI,
        // e anexá-la a um Bill novo faz o EF tentar INSERT nela (UNIQUE constraint em Categories.Id).
        // O tipo (entrada/saída) é derivado depois via query com Include(b => b.Category).
        Recurrence = recurrence;
        FixedAmount = fixedAmount;
        AutoSearch = autoSearch;
        SearchQuery = searchQuery;
        PixKey = pixKey;
    }

    /// <summary>Aplica apenas os campos informados; marca como atualizada se algo mudou.</summary>
    public void Edit(
        string? name = null,
        string? billerName = null,
        Category? category = null,
        RecurrenceRule? recurrence = null,
        decimal? fixedAmount = null,
        bool? active = null,
        bool? autoSearch = null,
        string? searchQuery = null,
        string? pixKey = null)
    {
        if (category is { } cat)
            EnsureLancamentoCategory(cat);
        if (fixedAmount is not null)
            EnsureFixedAmountValid(fixedAmount);

        var hasChanges = false;

        if (!string.IsNullOrWhiteSpace(name) && Name != name.Trim())
        {
            Name = name;
            hasChanges = true;
        }

        if (!string.IsNullOrWhiteSpace(billerName) && BillerName != billerName.Trim())
        {
            BillerName = billerName;
            hasChanges = true;
        }

        if (category is { } newCategory && CategoryId != newCategory.Id)
        {
            CategoryId = newCategory.Id;
            // Mantém a navegação coerente com o FK: sem isso, o "relationship fixup" do EF no Update
            // usaria a navegação antiga (carregada via Include) e reverteria a troca de categoria.
            Category = newCategory;
            hasChanges = true;
        }

        if (recurrence is not null && !recurrence.Equals(Recurrence))
        {
            Recurrence = recurrence;
            hasChanges = true;
        }

        if (fixedAmount is { } newFixed && FixedAmount != newFixed)
        {
            FixedAmount = newFixed;
            hasChanges = true;
        }

        if (active is { } newActive && Active != newActive)
        {
            Active = newActive;
            hasChanges = true;
        }

        if (autoSearch is { } newAutoSearch && AutoSearch != newAutoSearch)
        {
            AutoSearch = newAutoSearch;
            hasChanges = true;
        }

        if (searchQuery is not null && SearchQuery != (string.IsNullOrWhiteSpace(searchQuery) ? null : searchQuery.Trim()))
        {
            SearchQuery = searchQuery;
            hasChanges = true;
        }

        if (pixKey is not null && PixKey != (string.IsNullOrWhiteSpace(pixKey) ? null : pixKey.Trim()))
        {
            PixKey = pixKey;
            hasChanges = true;
        }

        if (hasChanges)
        {
            MarkAsUpdated();
        }
    }

    public void Deactivate()
    {
        if (Active)
        {
            Active = false;
            MarkAsUpdated();
        }
    }

    /// <summary>Exclusão lógica.</summary>
    public void Delete() => MarkAsDeleted();

    /// <summary>Indica se um e-mail (remetente/assunto) pertence a esta conta: nome do fornecedor aparece no remetente ou assunto.</summary>
    public bool MatchesEmail(string? from, string? subject)
    {
        if (string.IsNullOrWhiteSpace(BillerName))
        {
            return false;
        }

        var f = from ?? string.Empty;
        var s = subject ?? string.Empty;

        return f.Contains(BillerName, StringComparison.OrdinalIgnoreCase)
               || s.Contains(BillerName, StringComparison.OrdinalIgnoreCase);
    }

    private static void EnsureLancamentoCategory(Category category)
    {
        ArgumentNullException.ThrowIfNull(category);

        if (category.Type == ETransactionTypes.Transfer)
        {
            throw new ArgumentException(
                $"A categoria '{category.Name}' é de transferência e não pode ser usada em um lançamento.",
                nameof(category));
        }
    }

    private static void EnsureFixedAmountValid(decimal? fixedAmount)
    {
        if (fixedAmount is { } value && value <= 0)
        {
            throw new ArgumentException("O valor fixo deve ser positivo.", nameof(fixedAmount));
        }
    }
}
