FROM node:22-bookworm-slim AS build
ENV ASTRO_TELEMETRY_DISABLED=1
WORKDIR /src
# Pin pnpm to 9.x to match the lockfile format and avoid pnpm 10 breaking changes
RUN corepack enable && corepack prepare pnpm@9.15.9 --activate
COPY package.json pnpm-lock.yaml pnpm-workspace.yaml ./
COPY marketing/package.json marketing/package.json
RUN pnpm install --frozen-lockfile --filter @radiopad/marketing
COPY marketing marketing
RUN pnpm --filter @radiopad/marketing build

FROM nginx:1.27-alpine
COPY deploy/vps/nginx-marketing.conf /etc/nginx/conf.d/default.conf
COPY --from=build /src/marketing/dist /usr/share/nginx/html
EXPOSE 80
