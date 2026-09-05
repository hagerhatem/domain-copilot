using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace DomainCopilot.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddDrugInteractionTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DocumentChunks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DocumentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ChunkIndex = table.Column<int>(type: "int", nullable: false),
                    Text = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Section = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    PageNumber = table.Column<int>(type: "int", nullable: true),
                    DocumentVersion = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DocumentChunks", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Documents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SourceKey = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false),
                    FileName = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false),
                    Format = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    ContentHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Version = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    FailureReason = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    GuidelineVersionLabel = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    GuidelineEffectiveDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ChunkCount = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Documents", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DrugInteractionRules",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RuleId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    DrugANormalized = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    DrugBNormalized = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    Severity = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DrugInteractionRules", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "KnownMedications",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    NameNormalized = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_KnownMedications", x => x.Id);
                });

            migrationBuilder.InsertData(
                table: "DrugInteractionRules",
                columns: new[] { "Id", "Description", "DrugANormalized", "DrugBNormalized", "RuleId", "Severity" },
                values: new object[,]
                {
                    { 1, "ACE inhibitor + potassium-sparing diuretic combination carries a hyperkalemia risk; recommend potassium monitoring, especially at reduced renal function.", "lisinopril", "spironolactone", "RULE_ACEI_KSPARING_HYPERKALEMIA", "Caution" },
                    { 2, "Fluoroquinolone antibiotics are among the drug classes that can increase bleeding risk in patients on warfarin; recommend closer INR monitoring than the routine schedule.", "warfarin", "ciprofloxacin", "RULE_FLUOROQUINOLONE_WARFARIN_BLEEDING", "Caution" },
                    { 3, "Fluoroquinolone antibiotics are among the drug classes that can increase bleeding risk in patients on warfarin; recommend closer INR monitoring than the routine schedule.", "warfarin", "levofloxacin", "RULE_FLUOROQUINOLONE_WARFARIN_BLEEDING", "Caution" }
                });

            migrationBuilder.InsertData(
                table: "KnownMedications",
                columns: new[] { "Id", "NameNormalized" },
                values: new object[,]
                {
                    { 1, "metformin" },
                    { 2, "lisinopril" },
                    { 3, "spironolactone" },
                    { 4, "warfarin" },
                    { 5, "atorvastatin" },
                    { 6, "simvastatin" },
                    { 7, "amlodipine" },
                    { 8, "apixaban" },
                    { 9, "empagliflozin" },
                    { 10, "insulin glargine" },
                    { 11, "ciprofloxacin" },
                    { 12, "levofloxacin" },
                    { 13, "nitrofurantoin" },
                    { 14, "trimethoprim-sulfamethoxazole" },
                    { 15, "pioglitazone" }
                });

            migrationBuilder.CreateIndex(
                name: "IX_DocumentChunks_DocumentId",
                table: "DocumentChunks",
                column: "DocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_DocumentChunks_DocumentId_DocumentVersion",
                table: "DocumentChunks",
                columns: new[] { "DocumentId", "DocumentVersion" });

            migrationBuilder.CreateIndex(
                name: "IX_Documents_SourceKey",
                table: "Documents",
                column: "SourceKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DrugInteractionRules_DrugANormalized_DrugBNormalized",
                table: "DrugInteractionRules",
                columns: new[] { "DrugANormalized", "DrugBNormalized" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_KnownMedications_NameNormalized",
                table: "KnownMedications",
                column: "NameNormalized",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DocumentChunks");

            migrationBuilder.DropTable(
                name: "Documents");

            migrationBuilder.DropTable(
                name: "DrugInteractionRules");

            migrationBuilder.DropTable(
                name: "KnownMedications");
        }
    }
}
