FROM node:22-bookworm-slim AS build
ENV NEXT_TELEMETRY_DISABLED=1
WORKDIR /src
# Pin pnpm to 9.x to match the lockfile format and avoid pnpm 10 breaking changes
RUN corepack enable && corepack prepare pnpm@9.15.9 --activate
COPY package.json pnpm-lock.yaml pnpm-workspace.yaml ./
COPY frontend/package.json frontend/package.json
COPY mobile/package.json mobile/package.json
RUN pnpm install --frozen-lockfile --filter @radiopad/frontend
COPY frontend frontend
# Web is the master-admin surface only — build the `web` surface bundle
# (RADIOPAD_SURFACE=web → frontend/out-web), which ships no reporting routes.
RUN pnpm --filter @radiopad/frontend build:web

FROM nginx:1.27-alpine
COPY deploy/vps/nginx.conf /etc/nginx/conf.d/default.conf
COPY --from=build /src/frontend/out-web /usr/share/nginx/html
EXPOSE 80
