using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DomainCopilot.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddClinicalCaseTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ClinicalCases",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CaseReference = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    PresentingComplaint = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                    ProposedMedication = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    PatientContext = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    IsSyntheticData = table.Column<bool>(type: "bit", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Medications = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClinicalCases", x => x.Id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ClinicalCases");
        }
    }
}
