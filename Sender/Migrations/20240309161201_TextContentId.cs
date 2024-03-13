using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sender.Migrations
{
    /// <inheritdoc />
    public partial class TextContentId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "PK_ReportTextContents",
                table: "ReportTextContents");

            migrationBuilder.RenameColumn(
                name: "Report_Id",
                table: "ReportTextContents",
                newName: "ReportId");

            migrationBuilder.AlterColumn<int>(
                name: "ReportId",
                table: "ReportTextContents",
                type: "int",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "int")
                .OldAnnotation("SqlServer:Identity", "1, 1");

            migrationBuilder.AddColumn<int>(
                name: "Id",
                table: "ReportTextContents",
                type: "int",
                nullable: false,
                defaultValue: 0)
                .Annotation("SqlServer:Identity", "1, 1");

            migrationBuilder.AddPrimaryKey(
                name: "PK_ReportTextContents",
                table: "ReportTextContents",
                column: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "PK_ReportTextContents",
                table: "ReportTextContents");

            migrationBuilder.DropColumn(
                name: "Id",
                table: "ReportTextContents");

            migrationBuilder.RenameColumn(
                name: "ReportId",
                table: "ReportTextContents",
                newName: "Report_Id");

            migrationBuilder.AlterColumn<int>(
                name: "Report_Id",
                table: "ReportTextContents",
                type: "int",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "int")
                .Annotation("SqlServer:Identity", "1, 1");

            migrationBuilder.AddPrimaryKey(
                name: "PK_ReportTextContents",
                table: "ReportTextContents",
                column: "Report_Id");
        }
    }
}
