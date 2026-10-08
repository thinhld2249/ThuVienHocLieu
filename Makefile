.PHONY: dev-deps api web gen-api migration test e2e up down logs

dev-deps: ## db + gotenberg cho dev (docker compose.dev)
	docker compose -f docker-compose.dev.yml up -d

api: ## chạy API (dotnet watch) — cần `make dev-deps` trước
	cd apps/api && dotnet watch --project src/HocLieu.Api

web: ## chạy FE (vite dev :5173)
	cd apps/web && pnpm dev

gen-api: ## sinh src/lib/api-types.ts từ http://localhost:8080/openapi/v1.json (API phải đang chạy)
	cd apps/web && pnpm gen:api

migration: ## dotnet ef migrations add $(name) — ví dụ: make migration name=AddFoo
	cd apps/api && dotnet ef migrations add $(name) -p src/HocLieu.Api

test: ## test API + web
	cd apps/api && dotnet test && cd ../web && pnpm test

e2e: ## Playwright E2E
	cd apps/web && pnpm exec playwright test

up: ## build & chạy production stack (cần deploy/.env)
	docker compose -f deploy/docker-compose.yml --env-file deploy/.env up -d --build

down: ## dừng production stack
	docker compose -f deploy/docker-compose.yml --env-file deploy/.env down

logs: ## xem log production stack
	docker compose -f deploy/docker-compose.yml --env-file deploy/.env logs -f
