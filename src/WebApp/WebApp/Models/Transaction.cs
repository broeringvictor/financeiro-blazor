using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using WebApp.Models.Enums;
using WebApp.Models.Shared;

namespace WebApp.Models;

/// <summary>
/// Transação financeira (receita, despesa ou transferência).
/// O estado só é alterado pelos construtores e por <see cref="Edit"/>, que mantêm os invariantes.
/// </summary>
/// <remarks>
/// Propriedades: <see cref="Type"/>, <see cref="Title"/>, <see cref="Description"/>, <see cref="Amount"/>.
/// Métodos: construtor de criação, construtor de edição e <see cref="Edit"/> (atualização parcial).
/// Identidade e datas (Id, CreatedAt, UpdatedAt, DeletedAt) vêm de <see cref="BaseModel"/>.
/// </remarks>
public class Transaction : BaseModel
{
    /// <summary>Natureza da transação: receita, despesa ou transferência.</summary>
    [DisplayName("Tipo de transação")]
    [EnumDataType(typeof(ETransactionTypes), ErrorMessage = "Tipo de transação inválido.")]
    public ETransactionTypes Type { get; private set; }

    /// <summary>Título (5–150 caracteres); espaços nas extremidades são removidos.</summary>
    [DisplayName("Título")]
    [Length(5, 150, ErrorMessage = "O título deve ter entre 5 e 150 caracteres.")]
    public string Title
    {
        get;
        private set => field = value.Trim();
    } = string.Empty;

    /// <summary>Descrição opcional (até 500 caracteres); texto em branco vira <c>null</c>.</summary>
    [DisplayName("Descrição")]
    [StringLength(500, ErrorMessage = "A descrição não pode ter mais de 500 caracteres.")]
    public string? Description
    {
        get;
        private set => field = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    /// <summary>Valor monetário positivo (0,01 a 10 milhões).</summary>
    [DisplayName("Valor")]
    [Range(typeof(decimal), "0.01", "10000000", ErrorMessage = "O valor deve ser positivo e no máximo 10 milhões.")]
    public decimal Amount { get; private set; }

    /// <summary>Cria uma nova transação.</summary>
    public Transaction(ETransactionTypes type, string title, string? description, decimal amount)
    {
        Type = type;
        Title = title;
        Description = description;
        Amount = amount;
    }

    /// <summary>Reconstrói uma transação existente para edição.</summary>
    public Transaction(
        Guid id,
        DateTime createdAt,
        ETransactionTypes? type = null,
        string? title = null,
        string? description = null,
        decimal? amount = null)
    {
        ConfigureForEdit(id, createdAt);
        Edit(type, title, description, amount);
    }

    /// <summary>Aplica apenas os campos informados; marca como atualizada se algo mudou.</summary>
    public void Edit(
        ETransactionTypes? type = null,
        string? title = null,
        string? description = null,
        decimal? amount = null)
    {
        var hasChanges = false;

        if (type is { } newType && Type != newType)
        {
            Type = newType;
            hasChanges = true;
        }

        if (!string.IsNullOrWhiteSpace(title) && Title != title.Trim())
        {
            Title = title;
            hasChanges = true;
        }

        if (description is not null && Description != description.Trim())
        {
            Description = description;
            hasChanges = true;
        }

        if (amount is { } newAmount && Amount != newAmount)
        {
            Amount = newAmount;
            hasChanges = true;
        }

        if (hasChanges)
        {
            MarkAsUpdated();
        }
    }
}
