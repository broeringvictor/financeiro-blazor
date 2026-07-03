using Microsoft.AspNetCore.Components;
using MudBlazor;
using WebApp.Models;

namespace WebApp.Components.Shared;

/// <summary>Edição manual de valor/vencimento/emissão de uma fatura ainda não paga.</summary>
public partial class InvoiceFormDialog : ComponentBase
{
    [CascadingParameter] private IMudDialogInstance MudDialog { get; set; } = default!;

    [Parameter, EditorRequired] public Invoice Invoice { get; set; } = default!;

    /// <summary>Resultado devolvido ao fechar o diálogo com sucesso.</summary>
    public sealed record Resultado(decimal Amount, DateOnly DueDate, DateOnly? IssueDate);

    private decimal _amount;
    private DateTime? _dueDate;
    private DateTime? _issueDate;
    private string? _error;

    protected override void OnInitialized()
    {
        _amount = Invoice.Amount;
        _dueDate = Invoice.DueDate.ToDateTime(TimeOnly.MinValue);
        _issueDate = Invoice.IssueDate?.ToDateTime(TimeOnly.MinValue);
    }

    private void Submit()
    {
        _error = null;

        if (_dueDate is null)
        {
            _error = "Informe o vencimento.";
            return;
        }

        if (_amount < 0)
        {
            _error = "O valor não pode ser negativo.";
            return;
        }

        var resultado = new Resultado(
            _amount,
            DateOnly.FromDateTime(_dueDate.Value),
            _issueDate is { } emissao ? DateOnly.FromDateTime(emissao) : null);

        MudDialog.Close(DialogResult.Ok(resultado));
    }

    private void Cancel() => MudDialog.Cancel();
}
