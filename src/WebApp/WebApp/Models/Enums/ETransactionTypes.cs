using System.ComponentModel.DataAnnotations;

namespace WebApp.Models.Enums;

public enum ETransactionTypes
{
    [Display(Name = "Receita")]
    Income,
    [Display(Name = "Despesa")]
    Expense,
    [Display(Name = "Transferência")]
    Transfer,
}