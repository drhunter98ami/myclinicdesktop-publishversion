using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
namespace MyClinic.Migrations;

public partial class FinancialWorkspace : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(name: "FinancialNote", table: "Visits", type: "TEXT", nullable: true);
        migrationBuilder.AddColumn<bool>(name: "IsFinancialOnly", table: "Visits", type: "INTEGER", nullable: false, defaultValue: false);
        migrationBuilder.AddColumn<DateTime>(name: "DueDate", table: "UpcomingPayments", type: "TEXT", nullable: true);
        migrationBuilder.AddColumn<DateTime>(name: "CompletedAt", table: "UpcomingPayments", type: "TEXT", nullable: true);
        migrationBuilder.AddColumn<int>(name: "ExpenseId", table: "UpcomingPayments", type: "INTEGER", nullable: true);
        migrationBuilder.AddColumn<string>(name: "Recipient", table: "UpcomingPayments", type: "TEXT", nullable: false, defaultValue: "");
        migrationBuilder.AddColumn<string>(name: "Status", table: "UpcomingPayments", type: "TEXT", nullable: false, defaultValue: "Pending");
        migrationBuilder.CreateTable(name: "FinancialAuditEntries", columns: table => new
        {
            Id = table.Column<int>(type: "INTEGER", nullable: false).Annotation("Sqlite:Autoincrement", true),
            OccurredAt = table.Column<DateTime>(type: "TEXT", nullable: false),
            Actor = table.Column<string>(type: "TEXT", nullable: false),
            Action = table.Column<string>(type: "TEXT", nullable: false),
            EntityType = table.Column<string>(type: "TEXT", nullable: false),
            EntityId = table.Column<int>(type: "INTEGER", nullable: false),
            Description = table.Column<string>(type: "TEXT", nullable: false),
            BeforeJson = table.Column<string>(type: "TEXT", nullable: false),
            AfterJson = table.Column<string>(type: "TEXT", nullable: false),
            Reason = table.Column<string>(type: "TEXT", nullable: false)
        }, constraints: table => table.PrimaryKey("PK_FinancialAuditEntries", x => x.Id));
        migrationBuilder.CreateIndex(name: "IX_FinancialAuditEntries_OccurredAt", table: "FinancialAuditEntries", column: "OccurredAt");
    }
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "FinancialAuditEntries");
        migrationBuilder.DropColumn(name: "FinancialNote", table: "Visits");
        migrationBuilder.DropColumn(name: "IsFinancialOnly", table: "Visits");
        migrationBuilder.DropColumn(name: "DueDate", table: "UpcomingPayments");
        migrationBuilder.DropColumn(name: "CompletedAt", table: "UpcomingPayments");
        migrationBuilder.DropColumn(name: "ExpenseId", table: "UpcomingPayments");
        migrationBuilder.DropColumn(name: "Recipient", table: "UpcomingPayments");
        migrationBuilder.DropColumn(name: "Status", table: "UpcomingPayments");
    }
}
