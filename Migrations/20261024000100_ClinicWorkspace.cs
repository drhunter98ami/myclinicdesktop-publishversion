using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
namespace MyClinic.Migrations;

public partial class ClinicWorkspace : Migration
{
    protected override void Up(MigrationBuilder m)
    {
        m.AddColumn<bool>("PriceKnown", "Shortages", "INTEGER", nullable: false, defaultValue: true);
        m.AddColumn<bool>("IsDeleted", "Shortages", "INTEGER", nullable: false, defaultValue: false);
        m.AddColumn<int>("Quantity", "Shortages", "INTEGER", nullable: false, defaultValue: 1);
        m.AddColumn<string>("Phone", "LabNames", "TEXT", nullable: false, defaultValue: "");
        m.AddColumn<bool>("IsArchived", "LabNames", "INTEGER", nullable: false, defaultValue: false);
        m.AddColumn<int>("PatientId", "LabWorks", "INTEGER", nullable: true);
        m.AddColumn<int>("LabNameId", "LabWorks", "INTEGER", nullable: true);
        m.AddColumn<string>("WorkType", "LabWorks", "TEXT", nullable: false, defaultValue: "");
        m.AddColumn<string>("WorkStatus", "LabWorks", "TEXT", nullable: false, defaultValue: "تم الإرسال");
        m.AddColumn<string>("Currency", "LabWorks", "TEXT", nullable: false, defaultValue: "USD");
        m.AddColumn<System.DateTime>("ExpectedDelivery", "LabWorks", "TEXT", nullable: true);
        m.AddColumn<decimal>("RateSnapshot", "LabWorks", "TEXT", nullable: false, defaultValue: 15000m);
        m.AddColumn<int>("Revision", "LabWorks", "INTEGER", nullable: false, defaultValue: 0);
        m.AddColumn<bool>("RequiresCanals", "TreatmentCosts", "INTEGER", nullable: false, defaultValue: false);
        m.CreateTable("ClinicOperations", table => new
        {
            Id = table.Column<int>(type: "INTEGER", nullable: false).Annotation("Sqlite:Autoincrement", true),
            RequestKey = table.Column<string>(type: "TEXT", nullable: false), Kind = table.Column<string>(type: "TEXT", nullable: false),
            SourceId = table.Column<int>(type: "INTEGER", nullable: false), Description = table.Column<string>(type: "TEXT", nullable: false),
            Quantity = table.Column<int>(type: "INTEGER", nullable: false), Amount = table.Column<decimal>(type: "TEXT", nullable: false),
            Currency = table.Column<string>(type: "TEXT", nullable: false), RateSnapshot = table.Column<decimal>(type: "TEXT", nullable: false),
            AmountSyp = table.Column<decimal>(type: "TEXT", nullable: false), OccurredAt = table.Column<System.DateTime>(type: "TEXT", nullable: false),
            ExpenseId = table.Column<int>(type: "INTEGER", nullable: true), Actor = table.Column<string>(type: "TEXT", nullable: false)
        }, constraints: table => table.PrimaryKey("PK_ClinicOperations", x => x.Id));
        m.CreateIndex("IX_ClinicOperations_RequestKey", "ClinicOperations", "RequestKey", unique: true);
        m.CreateIndex("IX_ClinicOperations_Kind_SourceId", "ClinicOperations", new[] { "Kind", "SourceId" });
        m.Sql("UPDATE Shortages SET PriceKnown = 0 WHERE CAST(Price AS REAL) = 0");
        m.Sql("UPDATE LabWorks SET WorkStatus = CASE WHEN DateReceived IS NOT NULL OR Status IN ('تم الإستلام','تم الاستلام','تم الدفع') THEN 'تم الاستلام' ELSE 'تم الإرسال' END");
        m.Sql("UPDATE LabWorks SET AmountPaid = Cost WHERE CAST(AmountPaid AS REAL) = 0 AND Status IN ('تم الدفع','تم الدفع ولم يتم الاستلام')");
        m.Sql("UPDATE LabWorks SET RateSnapshot = COALESCE((SELECT UsdToSypRate FROM AppSettings LIMIT 1),15000)");
        m.Sql("UPDATE TreatmentCosts SET RequiresCanals = 1 WHERE TreatmentName LIKE '%سحب%عصب%' OR TreatmentName LIKE '%معالجة%لبية%' OR lower(TreatmentName) LIKE '%root%canal%' OR upper(TreatmentName) = 'RCT'");
    }
    protected override void Down(MigrationBuilder m)
    {
        m.DropTable("ClinicOperations");
        foreach (string column in new[] { "PriceKnown", "IsDeleted", "Quantity" }) m.DropColumn(column, "Shortages");
        foreach (string column in new[] { "Phone", "IsArchived" }) m.DropColumn(column, "LabNames");
        foreach (string column in new[] { "PatientId", "LabNameId", "WorkType", "WorkStatus", "Currency", "ExpectedDelivery", "RateSnapshot", "Revision" }) m.DropColumn(column, "LabWorks");
        m.DropColumn("RequiresCanals", "TreatmentCosts");
    }
}
