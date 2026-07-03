using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using WebApp.Data;

namespace WebApp.Endpoints;

/// <summary>
/// Serve o PDF de uma fatura sem nunca expor o caminho físico do arquivo ao cliente
/// — o front só conhece a URL "/faturas/{id}/pdf", nunca o Invoice.PdfPath.
/// </summary>
public static class FaturaEndpoints
{
    public static IEndpointRouteBuilder MapFaturaEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/faturas/{id:guid}/pdf", async (Guid id, ClaimsPrincipal user, ApplicationDbContext db) =>
        {
            var userId = user.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId))
            {
                return Results.Unauthorized();
            }

            var invoice = await db.Invoices.AsNoTracking().FirstOrDefaultAsync(
                i => i.Id == id && i.UserId == userId && i.DeletedAt == null);

            if (invoice?.PdfPath is null)
            {
                return Results.NotFound();
            }

            try
            {
                // Abre o arquivo já aqui (em vez de passar só o caminho pro Results.File) pra não
                // deixar uma janela entre "existe" e "abrir" — se for excluído nesse meio-tempo
                // (ex.: exclusão concorrente da fatura), cai no catch como 404 em vez de erro 500.
                var stream = File.OpenRead(invoice.PdfPath);
                return Results.File(stream, "application/pdf");
            }
            catch (IOException)
            {
                return Results.NotFound();
            }
        }).RequireAuthorization();

        return app;
    }
}
