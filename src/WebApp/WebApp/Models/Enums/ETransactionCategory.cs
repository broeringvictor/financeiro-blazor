using System.ComponentModel.DataAnnotations;

namespace WebApp.Models.Enums;

public enum ETransactionCategory
{
    // Income
    [Display(Name = "Salário")] Salary,
    [Display(Name = "Freelance")] Freelance,
    [Display(Name = "Investimentos")] Investment,
    [Display(Name = "Outras receitas")] OtherIncome,

    // Expense
    [Display(Name = "Aluguel")] Rent,
    [Display(Name = "Mercado")] Groceries,
    [Display(Name = "Contas")] Utilities,
    [Display(Name = "Transporte")] Transport,
    [Display(Name = "Lazer")] Leisure,
    [Display(Name = "Outras despesas")] OtherExpense,

    // Transfer
    [Display(Name = "Entre contas")] BetweenAccounts,
    [Display(Name = "Saque")] Withdrawal,
    [Display(Name = "Depósito")] Deposit,
}
