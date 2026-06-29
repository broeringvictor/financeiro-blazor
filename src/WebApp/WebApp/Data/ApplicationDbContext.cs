using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using WebApp.Models;

namespace WebApp.Data;

public class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : IdentityDbContext<ApplicationUser>(options)
{
    public DbSet<Transaction> Transactions => Set<Transaction>();

    public DbSet<Bill> Bills => Set<Bill>();

    public DbSet<Invoice> Invoices => Set<Invoice>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<Transaction>(entity =>
        {
            entity.HasOne<ApplicationUser>()
                .WithMany()
                .HasForeignKey(t => t.UserId)
                .IsRequired()
                .OnDelete(DeleteBehavior.Cascade);

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

            entity.HasOne<Bill>()
                .WithMany()
                .HasForeignKey(i => i.BillId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne<Transaction>()
                .WithMany()
                .HasForeignKey(i => i.PaymentTransactionId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasIndex(i => new { i.UserId, i.DueDate });
            entity.HasIndex(i => i.Status);

            // Dedupe: um e-mail só vira uma fatura.
            entity.HasIndex(i => i.SourceEmailMessageId)
                .IsUnique()
                .HasFilter("\"SourceEmailMessageId\" IS NOT NULL");

            // Dedupe: uma fatura por conta/competência.
            entity.HasIndex(i => new { i.BillId, i.ReferenceMonth }).IsUnique();

            entity.Ignore(i => i.IsOverdue);
        });
    }
}
