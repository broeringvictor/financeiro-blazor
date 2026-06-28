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
}
