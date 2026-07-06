using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using WebApp.Models;
using WebApp.Models.Enums;
using WebApp.Services;

namespace WebApp.Components.Shared;

/// <summary>Gestão das categorias do usuário: principais agrupadas por tipo, com subcategorias e CRUD.</summary>
public partial class CategoriasGrid : ComponentBase
{
    [Inject] private IServiceScopeFactory ScopeFactory { get; set; } = default!;

    [Inject] private IDialogService DialogService { get; set; } = default!;

    [Inject] private ISnackbar Snackbar { get; set; } = default!;

    [Inject] private ILogger<CategoriasGrid> Logger { get; set; } = default!;

    /// <summary>Dono das categorias.</summary>
    [Parameter, EditorRequired] public string UserId { get; set; } = string.Empty;

    private IReadOnlyList<Category> _tree = [];

    private static readonly ETransactionTypes[] _types =
        [ETransactionTypes.Income, ETransactionTypes.Expense, ETransactionTypes.Transfer];

    protected override async Task OnInitializedAsync()
    {
        if (!RendererInfo.IsInteractive)
        {
            return;
        }

        await CarregarAsync();
    }

    private async Task CarregarAsync()
    {
        await using var scope = ScopeFactory.CreateAsyncScope();
        var categorias = scope.ServiceProvider.GetRequiredService<CategoryService>();
        _tree = await categorias.GetTreeAsync(UserId);
    }

    private IEnumerable<Category> RaizesDe(ETransactionTypes type) =>
        _tree.Where(c => c.Type == type).OrderBy(c => c.Name);

    private async Task NovaPrincipal(ETransactionTypes type)
    {
        var parameters = new DialogParameters<CategoryFormDialog>
        {
            { x => x.Type, type },
            { x => x.ShowTypeSelect, true },
        };
        var dialog = await DialogService.ShowAsync<CategoryFormDialog>("Nova categoria", parameters, OpcoesDialogo());
        if (await dialog.Result is { Canceled: false, Data: CategoryFormDialog.CategoryFormResult r })
        {
            await ExecutarAsync(s => s.CriarAsync(UserId, r.Name, r.Type), "Categoria criada.");
        }
    }

    private async Task NovaSub(Category parent)
    {
        var parameters = new DialogParameters<CategoryFormDialog> { { x => x.Type, parent.Type } };
        var dialog = await DialogService.ShowAsync<CategoryFormDialog>(
            $"Nova subcategoria de \"{parent.Name}\"", parameters, OpcoesDialogo());
        if (await dialog.Result is { Canceled: false, Data: CategoryFormDialog.CategoryFormResult r })
        {
            await ExecutarAsync(s => s.CriarAsync(UserId, r.Name, parent.Type, parent.Id), "Subcategoria criada.");
        }
    }

    private async Task Editar(Category category)
    {
        var parameters = new DialogParameters<CategoryFormDialog>
        {
            { x => x.Name, category.Name },
            { x => x.Type, category.Type },
        };
        var dialog = await DialogService.ShowAsync<CategoryFormDialog>("Editar categoria", parameters, OpcoesDialogo());
        if (await dialog.Result is { Canceled: false, Data: CategoryFormDialog.CategoryFormResult r })
        {
            await ExecutarAsync(s => s.EditarAsync(category.Id, UserId, r.Name), "Categoria atualizada.");
        }
    }

    private async Task Excluir(Category category)
    {
        var confirmado = await DialogService.ShowMessageBoxAsync(
            "Excluir categoria",
            $"Deseja excluir \"{category.Name}\"?",
            yesText: "Excluir",
            cancelText: "Cancelar");

        if (confirmado == true)
        {
            await ExecutarAsync(s => s.ExcluirAsync(category.Id, UserId), "Categoria excluída.");
        }
    }

    private async Task ExecutarAsync(Func<CategoryService, Task> acao, string sucesso)
    {
        try
        {
            await using var scope = ScopeFactory.CreateAsyncScope();
            var categorias = scope.ServiceProvider.GetRequiredService<CategoryService>();
            await acao(categorias);

            Snackbar.Add(sucesso, Severity.Success);
            await CarregarAsync();
        }
        catch (InvalidOperationException ex)
        {
            Snackbar.Add(ex.Message, Severity.Warning);
        }
        catch (ArgumentException ex)
        {
            Snackbar.Add(ex.Message, Severity.Warning);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Falha ao gerir categoria do usuário {UserId}.", UserId);
            Snackbar.Add("Não foi possível concluir a operação.", Severity.Error);
        }
    }

    private static DialogOptions OpcoesDialogo() => new()
    {
        CloseOnEscapeKey = true,
        MaxWidth = MaxWidth.ExtraSmall,
        FullWidth = true,
    };
}
