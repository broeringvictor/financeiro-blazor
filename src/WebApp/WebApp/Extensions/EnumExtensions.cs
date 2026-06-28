using System.ComponentModel.DataAnnotations;
using System.Reflection;

namespace WebApp.Extensions;

public static class EnumExtensions
{
    /// <summary>
    /// Retorna o texto de <see cref="DisplayAttribute.Name"/> do membro do enum;
    /// se não houver atributo, devolve o próprio nome do membro.
    /// </summary>
    public static string GetDisplayName(this Enum value)
    {
        var member = value.GetType().GetMember(value.ToString()).FirstOrDefault();

        return member?.GetCustomAttribute<DisplayAttribute>()?.Name ?? value.ToString();
    }
}
