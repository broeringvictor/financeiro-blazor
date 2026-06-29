using System.ComponentModel.DataAnnotations;

namespace WebApp.Models.Enums;

/// <summary>Situação de uma fatura. "Vencida" não é um estado armazenado — é derivado de <c>DueDate</c>.</summary>
public enum EInvoiceStatus
{
    [Display(Name = "Pendente")] Pending,
    [Display(Name = "Paga")] Paid,
    [Display(Name = "Cancelada")] Canceled,
}
