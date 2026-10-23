using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
namespace MyClinic.Migrations;

public partial class AddPatientDateOfBirth : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.AddColumn<System.DateTime>(
        name: "DateOfBirth", table: "Patients", type: "TEXT", nullable: true);
    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.DropColumn(name: "DateOfBirth", table: "Patients");
}
