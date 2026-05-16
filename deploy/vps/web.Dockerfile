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
RUN pnpm --filter @radiopad/frontend build

FROM nginx:1.27-alpine
COPY deploy/vps/nginx.conf /etc/nginx/conf.d/default.conf
COPY --from=build /src/frontend/out /usr/share/nginx/html
EXPOSE 80
