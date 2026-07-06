using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using WebApp.Models;

namespace WebApp.Data;

public class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : IdentityDbContext<ApplicationUser>(options)
{
    public DbSet<Transaction> Transactions => Set<Transaction>();

    public DbSet<Bill> Bills => Set<Bill>();

    public DbSet<Invoice> Invoices => Set<Invoice>();

    public DbSet<Category> Categories => Set<Category>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<Category>(entity =>
        {
            entity.HasOne<ApplicationUser>()
                .WithMany()
                .HasForeignKey(c => c.UserId)
                .IsRequired()
                .OnDelete(DeleteBehavior.Cascade);

            // Auto-relação principal → subcategorias. Restrict: não apaga em cascata as filhas.
            entity.HasOne(c => c.Parent)
                .WithMany(c => c.Children)
                .HasForeignKey(c => c.ParentId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasIndex(c => new { c.UserId, c.ParentId });
            entity.Ignore(c => c.IsRoot);
        });

        builder.Entity<Transaction>(entity =>
        {
            entity.HasOne<ApplicationUser>()
                .WithMany()
                .HasForeignKey(t => t.UserId)
                .IsRequired()
                .OnDelete(DeleteBehavior.Cascade);

            // Categoria legada preservada na coluna "Category" para o backfill (removida numa migração futura).
            entity.Property(t => t.LegacyCategory).HasColumnName("Category");

            entity.HasOne(t => t.Category)
                .WithMany()
                .HasForeignKey(t => t.CategoryId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasIndex(t => t.UserId);
        });

        builder.Entity<Bill>(entity =>
        {
            entity.HasOne<ApplicationUser>()
                .WithMany()
                .HasForeignKey(b => b.UserId)
                .IsRequired()
                .OnDelete(DeleteBehavior.Cascade);

            // Recorrência como owned type (sem tabela própria).
            entity.OwnsOne(b => b.Recurrence);

            // Categoria legada preservada na coluna "Category" para o backfill (removida numa migração futura).
            entity.Property(b => b.LegacyCategory).HasColumnName("Category");

            entity.HasOne(b => b.Category)
                .WithMany()
                .HasForeignKey(b => b.CategoryId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasIndex(b => b.UserId);
            entity.HasIndex(b => b.Active);
        });

        builder.Entity<Invoice>(entity =>
        {
            entity.HasOne<ApplicationUser>()
                .WithMany()
                .HasForeignKey(i => i.UserId)
                .IsRequired()
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(i => i.Bill)
                .WithMany(b => b.Invoices)
                .HasForeignKey(i => i.BillId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne<Transaction>()
                .WithMany()
                .HasForeignKey(i => i.PaymentTransactionId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasIndex(i => new { i.UserId, i.DueDate });
            entity.HasIndex(i => i.Status);

            // Dedupe: um e-mail só vira uma fatura. Ignora faturas excluídas logicamente, senão
            // reprocessar o mesmo e-mail depois de excluir a fatura colidiria com o índice único.
            entity.HasIndex(i => i.SourceEmailMessageId)
                .IsUnique()
                .HasFilter("\"SourceEmailMessageId\" IS NOT NULL AND \"DeletedAt\" IS NULL");

            // Dedupe: uma fatura por conta/competência. Ignora faturas excluídas logicamente, para
            // permitir recriar a fatura de uma competência cuja fatura anterior foi excluída.
            entity.HasIndex(i => new { i.BillId, i.ReferenceMonth })
                .IsUnique()
                .HasFilter("\"DeletedAt\" IS NULL");

            entity.Ignore(i => i.IsOverdue);
        });
    }
}
