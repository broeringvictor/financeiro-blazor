namespace WebApp.Models.Enums;

/// <summary>
/// Define quais <see cref="ETransactionCategory"/> pertencem a cada <see cref="ETransactionTypes"/>
/// e valida a combinação tipo/categoria.
/// </summary>
public static class TransactionCategories
{
    private static readonly Dictionary<ETransactionTypes, ETransactionCategory[]> ByType = new()
    {
        [ETransactionTypes.Income] =
        [
            ETransactionCategory.Salary,
            ETransactionCategory.Freelance,
            ETransactionCategory.Investment,
            ETransactionCategory.OtherIncome,
        ],
        [ETransactionTypes.Expense] =
        [
            ETransactionCategory.Rent,
            ETransactionCategory.Groceries,
            ETransactionCategory.Utilities,
            ETransactionCategory.Transport,
            ETransactionCategory.Leisure,
            ETransactionCategory.OtherExpense,
        ],
        [ETransactionTypes.Transfer] =
        [
            ETransactionCategory.BetweenAccounts,
            ETransactionCategory.Withdrawal,
            ETransactionCategory.Deposit,
        ],
    };

    /// <summary>Categorias disponíveis para o tipo informado.</summary>
    public static IReadOnlyList<ETransactionCategory> For(ETransactionTypes type) =>
        ByType.TryGetValue(type, out var categories) ? categories : [];

    /// <summary>Indica se a categoria pertence ao tipo.</summary>
    public static bool Belongs(ETransactionTypes type, ETransactionCategory category) =>
        For(type).Contains(category);

    /// <summary>Todos os pares (tipo, categoria), na ordem de declaração. Usado pelo seed inicial de categorias.</summary>
    public static IEnumerable<(ETransactionTypes Type, ETransactionCategory Category)> All =>
        ByType.SelectMany(kv => kv.Value.Select(category => (kv.Key, category)));

    /// <summary>Tipo ao qual a categoria (enum legado) pertence.</summary>
    public static ETransactionTypes TypeOf(ETransactionCategory category) =>
        ByType.First(kv => kv.Value.Contains(category)).Key;
}
