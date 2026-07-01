using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace WebApp.Components.Shared;

/// <summary>Modal com abas "Contas" e "Faturas", aberto a partir do menu de navegação.</summary>
public partial class ContasFaturasDialog : ComponentBase
{
    [CascadingParameter] private IMudDialogInstance MudDialog { get; set; } = default!;

    [Parameter, EditorRequired] public string UserId { get; set; } = string.Empty;

    private void Fechar() => MudDialog.Close(DialogResult.Ok(true));
}
