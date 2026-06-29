using System.ComponentModel.DataAnnotations;

namespace WebApp.Models.Enums;

/// <summary>Frequência de recorrência de uma conta.</summary>
public enum ERecurrenceFrequency
{
    [Display(Name = "Mensal")] Monthly,
    [Display(Name = "Semanal")] Weekly,
    [Display(Name = "Anual")] Yearly,
}
