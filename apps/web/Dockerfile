# syntax=docker/dockerfile:1

FROM node:22-alpine AS build
WORKDIR /app
RUN corepack enable

COPY package.json pnpm-lock.yaml ./
RUN pnpm install --frozen-lockfile

COPY . .
# Client ID Google là định danh công khai (không phải secret) — đặt sẵn làm
# mặc định để build prod/Render không cần truyền build-arg (cùng 1 OAuth
# client, origins cấu hình trong Google Cloud Console).
ARG VITE_GOOGLE_CLIENT_ID=795347854507-jv3nqlosef2fkangcs4var5shu4pgq75.apps.googleusercontent.com
ENV VITE_GOOGLE_CLIENT_ID=$VITE_GOOGLE_CLIENT_ID
RUN pnpm build

FROM caddy:2
COPY Caddyfile /etc/caddy/Caddyfile
COPY --from=build /app/dist /srv
EXPOSE 80 443
