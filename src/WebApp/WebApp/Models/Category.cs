using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using WebApp.Models.Enums;
using WebApp.Models.Shared;

namespace WebApp.Models;

/// <summary>
/// Categoria de classificação de transações/contas, organizada em dois níveis: categoria principal
/// (<see cref="ParentId"/> nulo) e subcategoria (aponta para a principal via <see cref="ParentId"/>).
/// Pertence a um <see cref="ETransactionTypes"/>; a subcategoria herda o tipo da principal.
/// </summary>
public class Category : BaseModel
{
    /// <summary>Id do usuário dono da categoria (FK para AspNetUsers).</summary>
    [Required]
    public string UserId { get; private set; } = string.Empty;

    /// <summary>Nome exibido (2–100 caracteres); espaços nas extremidades são removidos.</summary>
    [DisplayName("Nome")]
    [Length(2, 100, ErrorMessage = "O nome deve ter entre 2 e 100 caracteres.")]
    public string Name
    {
        get;
        private set => field = value.Trim();
    } = string.Empty;

    /// <summary>Tipo ao qual a categoria pertence (Receita/Despesa/Transferência).</summary>
    [DisplayName("Tipo")]
    [EnumDataType(typeof(ETransactionTypes), ErrorMessage = "Tipo inválido.")]
    public ETransactionTypes Type { get; private set; }

    /// <summary>Categoria principal (null = esta é uma categoria principal; preenchido = subcategoria).</summary>
    public Guid? ParentId { get; private set; }

    /// <summary>Navegação para a categoria principal (quando esta é uma subcategoria).</summary>
    public Category? Parent { get; private set; }

    /// <summary>Subcategorias desta categoria principal (1:N).</summary>
    public ICollection<Category> Children { get; private set; } = new List<Category>();

    /// <summary>True quando é uma categoria principal (sem pai).</summary>
    [NotMapped]
    public bool IsRoot => ParentId is null;

    /// <summary>Construtor usado pelo Entity Framework.</summary>
    private Category() { }

    /// <summary>
    /// Cria uma categoria. Se <paramref name="parent"/> for informado, cria uma subcategoria que herda
    /// o tipo do pai; caso contrário cria uma categoria principal do <paramref name="type"/> indicado.
    /// </summary>
    public Category(string userId, string name, ETransactionTypes type, Category? parent = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);

        if (parent is not null)
        {
            EnsureCanBeParent(parent);
            type = parent.Type; // a subcategoria sempre herda o tipo da principal
            ParentId = parent.Id;
            Parent = parent;
        }

        UserId = userId;
        Name = name;
        Type = type;
    }

    /// <summary>Renomeia a categoria (o tipo e a hierarquia são imutáveis).</summary>
    public void Edit(string name)
    {
        if (!string.IsNullOrWhiteSpace(name) && Name != name.Trim())
        {
            Name = name;
            MarkAsUpdated();
        }
    }

    /// <summary>Exclusão lógica.</summary>
    public void Delete() => MarkAsDeleted();

    /// <summary>Uma subcategoria não pode ter filhos (profundidade máxima de dois níveis).</summary>
    private static void EnsureCanBeParent(Category parent)
    {
        if (parent.ParentId is not null)
        {
            throw new ArgumentException(
                "Não é possível criar subcategoria de uma subcategoria (máximo de dois níveis).", nameof(parent));
        }
    }
}
