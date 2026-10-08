using Microsoft.EntityFrameworkCore.Migrations;

namespace HocLieu.Infrastructure.Data.Migrations;

/// <summary>
/// spec §9: extension unaccent + pg_trgm, hàm f_unaccent IMMUTABLE,
/// cột search_vector sinh tự động (documents, quizzes) và index GIN.
/// Cột search_vector không map vào entity (DB-only, dùng cho tìm kiếm).
/// </summary>
public partial class AddSearchVectors : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // citext đã được tạo ở InitialCreate (annotation Npgsql:PostgresExtension)
        migrationBuilder.Sql("CREATE EXTENSION IF NOT EXISTS unaccent;");
        migrationBuilder.Sql("CREATE EXTENSION IF NOT EXISTS pg_trgm;");

        // unaccent() không IMMUTABLE → wrapper IMMUTABLE để dùng trong generated column (spec §9)
        migrationBuilder.Sql(
            "CREATE OR REPLACE FUNCTION f_unaccent(text) RETURNS text " +
            "LANGUAGE sql IMMUTABLE PARALLEL SAFE STRICT " +
            "AS $$ SELECT public.unaccent('public.unaccent'::regdictionary, $1) $$;");

        migrationBuilder.Sql(
            "ALTER TABLE documents " +
            "ADD COLUMN search_vector tsvector GENERATED ALWAYS AS (" +
            "to_tsvector('simple', f_unaccent(lower(coalesce(title, '') || ' ' || coalesce(summary, ''))))" +
            ") STORED;");

        migrationBuilder.Sql(
            "ALTER TABLE quizzes " +
            "ADD COLUMN search_vector tsvector GENERATED ALWAYS AS (" +
            "to_tsvector('simple', f_unaccent(lower(coalesce(title, '') || ' ' || coalesce(description_html, ''))))" +
            ") STORED;");

        migrationBuilder.Sql("CREATE INDEX ix_docs_search ON documents USING gin (search_vector);");
        migrationBuilder.Sql("CREATE INDEX ix_quizzes_search ON quizzes USING gin (search_vector);");
        migrationBuilder.Sql("CREATE INDEX ix_docs_title_trgm ON documents USING gin (f_unaccent(lower(title)) gin_trgm_ops);");
        migrationBuilder.Sql("CREATE INDEX ix_quizzes_title_trgm ON quizzes USING gin (f_unaccent(lower(title)) gin_trgm_ops);");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("DROP INDEX IF EXISTS ix_quizzes_title_trgm;");
        migrationBuilder.Sql("DROP INDEX IF EXISTS ix_docs_title_trgm;");
        migrationBuilder.Sql("DROP INDEX IF EXISTS ix_quizzes_search;");
        migrationBuilder.Sql("DROP INDEX IF EXISTS ix_docs_search;");
        migrationBuilder.Sql("ALTER TABLE quizzes DROP COLUMN IF EXISTS search_vector;");
        migrationBuilder.Sql("ALTER TABLE documents DROP COLUMN IF EXISTS search_vector;");
        migrationBuilder.Sql("DROP FUNCTION IF EXISTS f_unaccent(text);");
        // Không drop extension unaccent/pg_trgm: các migration khác có thể đang dùng
    }
}
