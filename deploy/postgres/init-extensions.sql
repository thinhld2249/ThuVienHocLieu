-- Chạy khi lần đầu tạo volume pgdata (initdb). Migrations EF cũng declare extensions (IF NOT EXISTS).
CREATE EXTENSION IF NOT EXISTS citext;
CREATE EXTENSION IF NOT EXISTS unaccent;
CREATE EXTENSION IF NOT EXISTS pg_trgm;
