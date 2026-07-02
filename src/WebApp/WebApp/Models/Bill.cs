using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using WebApp.Models.Enums;
using WebApp.Models.Shared;
using WebApp.Models.ValueObjects;

namespace WebApp.Models;

/// <summary>
/// Conta: obrigação recorrente do usuário (ex.: "Luz - Celesc"). Define o fornecedor, a categoria,
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

    /// <summary>Categoria (precisa pertencer ao tipo Despesa).</summary>
    [DisplayName("Categoria")]
    [EnumDataType(typeof(ETransactionCategory), ErrorMessage = "Categoria inválida.")]
    public ETransactionCategory Category { get; private set; }

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

    /// <summary>Faturas (cobranças concretas) emitidas para esta conta (1:N).</summary>
    public ICollection<Invoice> Invoices { get; private set; } = new List<Invoice>();

    /// <summary>Construtor usado pelo Entity Framework.</summary>
    private Bill() { }

    /// <summary>Cria uma nova conta recorrente.</summary>
    public Bill(
        string userId,
        string name,
        string billerName,
        ETransactionCategory category,
        RecurrenceRule recurrence,
        decimal? fixedAmount = null,
        bool autoSearch = false,
        string? searchQuery = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        ArgumentNullException.ThrowIfNull(recurrence);
        EnsureExpenseCategory(category);
        EnsureFixedAmountValid(fixedAmount);

        UserId = userId;
        Name = name;
        BillerName = billerName;
        Category = category;
        Recurrence = recurrence;
        FixedAmount = fixedAmount;
        AutoSearch = autoSearch;
        SearchQuery = searchQuery;
    }

    /// <summary>Aplica apenas os campos informados; marca como atualizada se algo mudou.</summary>
    public void Edit(
        string? name = null,
        string? billerName = null,
        ETransactionCategory? category = null,
        RecurrenceRule? recurrence = null,
        decimal? fixedAmount = null,
        bool? active = null,
        bool? autoSearch = null,
        string? searchQuery = null)
    {
        if (category is { } cat)
            EnsureExpenseCategory(cat);
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

        if (category is { } newCategory && Category != newCategory)
        {
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

    private static void EnsureExpenseCategory(ETransactionCategory category)
    {
        if (!TransactionCategories.Belongs(ETransactionTypes.Expense, category))
        {
            throw new ArgumentException(
                $"A categoria '{category}' não é uma categoria de despesa.", nameof(category));
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
