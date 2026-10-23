using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
namespace MyClinic.Migrations;

public partial class AddPatientBirthYear : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.AddColumn<int>(
        name: "BirthYear", table: "Patients", type: "INTEGER", nullable: true);
    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.DropColumn(name: "BirthYear", table: "Patients");
}
