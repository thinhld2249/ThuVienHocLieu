using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HocLieu.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddFileQuizId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "quiz_id",
                table: "files",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_files_quiz_id",
                table: "files",
                column: "quiz_id");

            migrationBuilder.AddForeignKey(
                name: "fk_files_quizzes_quiz_id",
                table: "files",
                column: "quiz_id",
                principalTable: "quizzes",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_files_quizzes_quiz_id",
                table: "files");

            migrationBuilder.DropIndex(
                name: "ix_files_quiz_id",
                table: "files");

            migrationBuilder.DropColumn(
                name: "quiz_id",
                table: "files");
        }
    }
}
