using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace WebApp.Components.Shared;

/// <summary>Modal com todas as faturas encontradas para uma conta (Bill) específica.</summary>
public partial class BillInvoicesDialog : ComponentBase
{
    [CascadingParameter] private IMudDialogInstance MudDialog { get; set; } = default!;

    [Parameter, EditorRequired] public string UserId { get; set; } = string.Empty;

    [Parameter, EditorRequired] public Guid BillId { get; set; }

    private void Fechar() => MudDialog.Close(DialogResult.Ok(true));
}
