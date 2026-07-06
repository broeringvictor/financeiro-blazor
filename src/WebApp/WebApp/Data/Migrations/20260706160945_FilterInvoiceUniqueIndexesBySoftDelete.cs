using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WebApp.Data.Migrations
{
    /// <inheritdoc />
    public partial class FilterInvoiceUniqueIndexesBySoftDelete : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Invoices_BillId_ReferenceMonth",
                table: "Invoices");

            migrationBuilder.DropIndex(
                name: "IX_Invoices_SourceEmailMessageId",
                table: "Invoices");

            migrationBuilder.CreateIndex(
                name: "IX_Invoices_BillId_ReferenceMonth",
                table: "Invoices",
                columns: new[] { "BillId", "ReferenceMonth" },
                unique: true,
                filter: "\"DeletedAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Invoices_SourceEmailMessageId",
                table: "Invoices",
                column: "SourceEmailMessageId",
                unique: true,
                filter: "\"SourceEmailMessageId\" IS NOT NULL AND \"DeletedAt\" IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Invoices_BillId_ReferenceMonth",
                table: "Invoices");

            migrationBuilder.DropIndex(
                name: "IX_Invoices_SourceEmailMessageId",
                table: "Invoices");

            migrationBuilder.CreateIndex(
                name: "IX_Invoices_BillId_ReferenceMonth",
                table: "Invoices",
                columns: new[] { "BillId", "ReferenceMonth" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Invoices_SourceEmailMessageId",
                table: "Invoices",
                column: "SourceEmailMessageId",
                unique: true,
                filter: "\"SourceEmailMessageId\" IS NOT NULL");
        }
    }
}
