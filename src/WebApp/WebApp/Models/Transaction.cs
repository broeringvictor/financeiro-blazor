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
/// Propriedades: <see cref="Type"/>, <see cref="Category"/>, <see cref="Title"/>, <see cref="Description"/>, <see cref="Amount"/>.
/// Métodos: construtor de criação, construtor de edição e <see cref="Edit"/> (atualização parcial).
/// Identidade e datas (Id, CreatedAt, UpdatedAt, DeletedAt) vêm de <see cref="BaseModel"/>.
/// </remarks>
public class Transaction : BaseModel
{
    /// <summary>Id do usuário dono da transação (FK para AspNetUsers / IdentityUser.Id).</summary>
    [Required]
    public string UserId { get; private set; } = string.Empty;

    /// <summary>Natureza da transação: receita, despesa ou transferência.</summary>
    [DisplayName("Tipo de transação")]
    [EnumDataType(typeof(ETransactionTypes), ErrorMessage = "Tipo de transação inválido.")]
    public ETransactionTypes Type { get; private set; }

    /// <summary>
    /// Categoria (principal ou subcategoria); precisa pertencer ao <see cref="Type"/>. Nula apenas em linhas
    /// legadas ainda não migradas; toda transação criada pelo domínio recebe uma categoria obrigatória.
    /// </summary>
    public Guid? CategoryId { get; private set; }

    /// <summary>Navegação para a categoria.</summary>
    public Category? Category { get; private set; }

    /// <summary>
    /// Categoria legada (enum) da versão anterior, mantida apenas para o backfill de <see cref="CategoryId"/>.
    /// Não é mais usada pelo domínio e será removida numa migração futura.
    /// </summary>
    public ETransactionCategory LegacyCategory { get; private set; }

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
    [Range(typeof(decimal), "0.01", "10000000",
        ParseLimitsInInvariantCulture = true,
        ErrorMessage = "O valor deve ser positivo e no máximo 10 milhões.")]
    public decimal Amount { get; private set; }

    /// <summary>Construtor usado pelo Entity Framework para materializar a entidade.</summary>
    private Transaction() { }

    /// <summary>Cria uma nova transação para o usuário informado.</summary>
    public Transaction(string userId, ETransactionTypes type, Category category, string title, string? description, decimal amount)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        EnsureCategoryMatchesType(type, category);

        UserId = userId;
        Type = type;
        CategoryId = category.Id;
        Title = title;
        Description = description;
        Amount = amount;
    }

    /// <summary>Reconstrói uma transação existente para edição.</summary>
    public Transaction(
        Guid id,
        string userId,
        DateTime createdAt,
        ETransactionTypes? type = null,
        Category? category = null,
        string? title = null,
        string? description = null,
        decimal? amount = null)
    {
        UserId = userId;
        ConfigureForEdit(id, createdAt);
        Edit(type, category, title, description, amount);
    }

    /// <summary>Aplica apenas os campos informados; marca como atualizada se algo mudou.</summary>
    public void Edit(
        ETransactionTypes? type = null,
        Category? category = null,
        string? title = null,
        string? description = null,
        decimal? amount = null)
    {
        // Valida a combinação final (tipo/categoria) antes de qualquer mutação.
        EnsureCategoryMatchesType(type ?? Type, category ?? Category);

        var hasChanges = false;

        if (type is { } newType && Type != newType)
        {
            Type = newType;
            hasChanges = true;
        }

        if (category is { } newCategory && CategoryId != newCategory.Id)
        {
            CategoryId = newCategory.Id;
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

    /// <summary>Exclusão lógica: marca <see cref="BaseModel.DeletedAt"/> sem remover do banco.</summary>
    public void Delete() => MarkAsDeleted();

    /// <summary>Garante que a categoria pertence ao tipo; lança <see cref="ArgumentException"/> caso contrário.</summary>
    private static void EnsureCategoryMatchesType(ETransactionTypes type, Category category)
    {
        ArgumentNullException.ThrowIfNull(category);

        if (category.Type != type)
        {
            throw new ArgumentException(
                $"A categoria '{category.Name}' (tipo {category.Type}) não pertence ao tipo '{type}'.", nameof(category));
        }
    }
}
