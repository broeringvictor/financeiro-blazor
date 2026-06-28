using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using MudBlazor.Services;
using WebApp.Components.Shared;
using WebApp.Models.Enums;

namespace Tests.WebApp.Components.Shared;

public class EnumSelectTests : BunitContext, IAsyncLifetime
{
    public EnumSelectTests()
    {
        Services.AddMudServices();
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    Task IAsyncLifetime.InitializeAsync() => Task.CompletedTask;

    // MudBlazor registra serviços que só implementam IAsyncDisposable; o Dispose
    // síncrono do BunitContext falharia. Descartamos de forma assíncrona aqui...
    Task IAsyncLifetime.DisposeAsync() => DisposeAsync().AsTask();

    // ...e neutralizamos o descarte síncrono.
    protected override void Dispose(bool disposing)
    {
    }

    // MudSelect usa um MudPopover, que exige um MudPopoverProvider na árvore.
    // Renderizamos o provider junto com o componente sob teste.
    private IRenderedComponent<IComponent> RenderEnumSelect(
        ETransactionTypes value = ETransactionTypes.Income,
        string? label = null,
        EventCallback<ETransactionTypes> valueChanged = default)
    {
        return Render(builder =>
        {
            builder.OpenComponent<MudPopoverProvider>(0);
            builder.CloseComponent();

            builder.OpenComponent<EnumSelect<ETransactionTypes>>(1);
            builder.AddAttribute(2, nameof(EnumSelect<ETransactionTypes>.Value), value);
            builder.AddAttribute(3, nameof(EnumSelect<ETransactionTypes>.Label), label);
            builder.AddAttribute(4, nameof(EnumSelect<ETransactionTypes>.ValueChanged), valueChanged);
            builder.CloseComponent();
        });
    }

    [Fact]
    public void DeveRenderizarUmItemPorValorDoEnum()
    {
        var cut = RenderEnumSelect();

        var items = cut.FindComponents<MudSelectItem<ETransactionTypes>>();

        Assert.Equal(Enum.GetValues<ETransactionTypes>().Length, items.Count);
    }

    [Theory]
    [InlineData(ETransactionTypes.Income, "Receita")]
    [InlineData(ETransactionTypes.Expense, "Despesa")]
    [InlineData(ETransactionTypes.Transfer, "Transferência")]
    public void DeveFormatarOsValoresComOsNomesDeExibicao(ETransactionTypes value, string esperado)
    {
        var cut = RenderEnumSelect();

        var select = cut.FindComponent<MudSelect<ETransactionTypes>>();
#pragma warning disable MUD0012 // acesso direto ao parâmetro em teste
        var toString = select.Instance.ToStringFunc;
#pragma warning restore MUD0012

        Assert.NotNull(toString);
        Assert.Equal(esperado, toString(value));
    }

    [Fact]
    public void DeveRepassarLabelEValorParaOMudSelect()
    {
        var cut = RenderEnumSelect(value: ETransactionTypes.Expense, label: "Tipo");

        var select = cut.FindComponent<MudSelect<ETransactionTypes>>();

        Assert.Equal("Tipo", select.Instance.Label);
#pragma warning disable MUD0012 // acesso direto ao parâmetro Value é aceitável em teste
        Assert.Equal(ETransactionTypes.Expense, select.Instance.Value);
#pragma warning restore MUD0012
    }

    [Fact]
    public async Task AoMudarOValor_DeveDispararValueChanged()
    {
        ETransactionTypes? capturado = null;
        var callback = EventCallback.Factory.Create<ETransactionTypes>(this, v => capturado = v);

        var cut = RenderEnumSelect(valueChanged: callback);

        var select = cut.FindComponent<MudSelect<ETransactionTypes>>();
        await cut.InvokeAsync(() => select.Instance.ValueChanged.InvokeAsync(ETransactionTypes.Transfer));

        Assert.Equal(ETransactionTypes.Transfer, capturado);
    }
}
