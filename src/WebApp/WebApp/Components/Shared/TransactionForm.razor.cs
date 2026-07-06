using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using WebApp.Models;
using WebApp.Models.Enums;
using WebApp.Services;

namespace WebApp.Components.Shared;

public partial class TransactionForm : ComponentBase
{
    [Inject] private IServiceScopeFactory ScopeFactory { get; set; } = default!;

    [Inject] private ISnackbar Snackbar { get; set; } = default!;

    /// <summary>Transação a editar; <c>null</c> cria uma nova.</summary>
    [Parameter] public Transaction? Transaction { get; set; }

    /// <summary>Dono da transação (obrigatório no modo criação).</summary>
    [Parameter] public string UserId { get; set; } = string.Empty;

    /// <summary>Recebe a transação criada/editada após submit válido.</summary>
    [Parameter] public EventCallback<Transaction> OnSubmit { get; set; }

    /// <summary>Disparado ao cancelar; o botão só aparece se houver handler.</summary>
    [Parameter] public EventCallback OnCancel { get; set; }

    private readonly FormModel _model = new();
    private IReadOnlyList<Category> _categories = [];

    private bool IsEdit => Transaction is not null;

    protected override async Task OnInitializedAsync()
    {
        if (Transaction is { } t)
        {
            _model.Type = t.Type;
            _model.CategoryId = t.CategoryId;
            _model.Title = t.Title;
            _model.Description = t.Description;
            _model.Amount = t.Amount;
        }

        await CarregarCategoriasAsync(_model.Type);
    }

    // Ao trocar o tipo, recarrega as categorias e garante uma seleção coerente com o novo tipo.
    private async Task OnTypeChanged(ETransactionTypes type)
    {
        _model.Type = type;
        await CarregarCategoriasAsync(type);

        if (_model.CategoryId is null || !ContémCategoria(_model.CategoryId.Value))
        {
            _model.CategoryId = _categories.FirstOrDefault()?.Id;
        }
    }

    private async Task CarregarCategoriasAsync(ETransactionTypes type)
    {
        await using var scope = ScopeFactory.CreateAsyncScope();
        var categorias = scope.ServiceProvider.GetRequiredService<CategoryService>();
        _categories = await categorias.GetTreeAsync(UserId, type);

        // No modo criação, começa com a primeira categoria selecionada.
        if (_model.CategoryId is null || !ContémCategoria(_model.CategoryId.Value))
        {
            _model.CategoryId = _categories.FirstOrDefault()?.Id;
        }
    }

    private bool ContémCategoria(Guid id) =>
        _categories.Any(c => c.Id == id || c.Children.Any(f => f.Id == id));

    private Category? ResolverCategoria(Guid id) =>
        _categories.Select(c => c.Id == id ? c : c.Children.FirstOrDefault(f => f.Id == id))
            .FirstOrDefault(c => c is not null);

    private async Task HandleValidSubmit()
    {
        if (_model.CategoryId is not { } categoryId || ResolverCategoria(categoryId) is not { } category)
        {
            Snackbar.Add("Selecione uma categoria.", Severity.Warning);
            return;
        }

        Transaction result;

        if (Transaction is { } existing)
        {
            existing.Edit(_model.Type, category, _model.Title, _model.Description, _model.Amount);
            result = existing;
        }
        else
        {
            result = new Transaction(UserId, _model.Type, category, _model.Title, _model.Description, _model.Amount);
        }

        await OnSubmit.InvokeAsync(result);
    }

    // Modelo editável do formulário (a Transaction tem setters privados).
    private sealed class FormModel
    {
        public ETransactionTypes Type { get; set; } = ETransactionTypes.Expense;

        [Required(ErrorMessage = "Selecione uma categoria.")]
        public Guid? CategoryId { get; set; }

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
