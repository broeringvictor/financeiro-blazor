using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Components;
using WebApp.Models;
using WebApp.Models.Enums;

namespace WebApp.Components.Shared;

public partial class TransactionForm : ComponentBase
{
    /// <summary>Transação a editar; <c>null</c> cria uma nova.</summary>
    [Parameter] public Transaction? Transaction { get; set; }

    /// <summary>Dono da transação (obrigatório no modo criação).</summary>
    [Parameter] public string UserId { get; set; } = string.Empty;

    /// <summary>Recebe a transação criada/editada após submit válido.</summary>
    [Parameter] public EventCallback<Transaction> OnSubmit { get; set; }

    /// <summary>Disparado ao cancelar; o botão só aparece se houver handler.</summary>
    [Parameter] public EventCallback OnCancel { get; set; }

    private readonly FormModel _model = new();

    private bool IsEdit => Transaction is not null;

    protected override void OnParametersSet()
    {
        if (Transaction is { } t)
        {
            _model.Type = t.Type;
            _model.Category = t.Category;
            _model.Title = t.Title;
            _model.Description = t.Description;
            _model.Amount = t.Amount;
        }
    }

    // Ao trocar o tipo, garante uma categoria coerente com ele.
    private void OnTypeChanged(ETransactionTypes type)
    {
        _model.Type = type;

        if (!TransactionCategories.Belongs(type, _model.Category))
        {
            _model.Category = TransactionCategories.For(type)[0];
        }
    }

    private async Task HandleValidSubmit()
    {
        Transaction result;

        if (Transaction is { } existing)
        {
            existing.Edit(_model.Type, _model.Category, _model.Title, _model.Description, _model.Amount);
            result = existing;
        }
        else
        {
            result = new Transaction(UserId, _model.Type, _model.Category, _model.Title, _model.Description, _model.Amount);
        }

        await OnSubmit.InvokeAsync(result);
    }

    // Modelo editável do formulário (a Transaction tem setters privados).
    private sealed class FormModel
    {
        public ETransactionTypes Type { get; set; } = ETransactionTypes.Expense;

        public ETransactionCategory Category { get; set; } = ETransactionCategory.Rent;

        [Length(5, 150, ErrorMessage = "O título deve ter entre 5 e 150 caracteres.")]
        public string Title { get; set; } = string.Empty;

        [StringLength(500, ErrorMessage = "A descrição não pode ter mais de 500 caracteres.")]
        public string? Description { get; set; }

        [Range(typeof(decimal), "0.01", "10000000",
            ParseLimitsInInvariantCulture = true,
            ErrorMessage = "O valor deve ser positivo e no máximo 10 milhões.")]
        public decimal Amount { get; set; } = 0.01m;
    }
}
