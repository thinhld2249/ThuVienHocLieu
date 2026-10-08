using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HocLieu.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSearchText : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "search_text",
                table: "quizzes",
                type: "text",
                nullable: true,
                computedColumnSql: "f_unaccent(lower(regexp_replace(coalesce(\"title\",'') || ' ' || coalesce(\"description_html\",''), '<[^>]+>', ' ', 'g')))",
                stored: true);

            migrationBuilder.AddColumn<string>(
                name: "search_text",
                table: "documents",
                type: "text",
                nullable: true,
                computedColumnSql: "f_unaccent(lower(coalesce(\"title\",'') || ' ' || coalesce(\"summary\",'')))",
                stored: true);

            // f_unaccent là function tự viết (migration AddSearchVectors) → tìm kiếm không dấu qua ILIKE + trigram
            migrationBuilder.Sql("CREATE INDEX ix_docs_search_text_trgm ON documents USING gin (search_text gin_trgm_ops);");
            migrationBuilder.Sql("CREATE INDEX ix_quizzes_search_text_trgm ON quizzes USING gin (search_text gin_trgm_ops);");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP INDEX IF EXISTS ix_docs_search_text_trgm;");
            migrationBuilder.Sql("DROP INDEX IF EXISTS ix_quizzes_search_text_trgm;");

            migrationBuilder.DropColumn(
                name: "search_text",
                table: "quizzes");

            migrationBuilder.DropColumn(
                name: "search_text",
                table: "documents");
        }
    }
}
