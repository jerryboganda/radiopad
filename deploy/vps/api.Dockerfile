FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY backend/RadioPad.Api/Directory.Build.props backend/RadioPad.Api/
COPY backend/RadioPad.Api/src/RadioPad.Domain/RadioPad.Domain.csproj backend/RadioPad.Api/src/RadioPad.Domain/
COPY backend/RadioPad.Api/src/RadioPad.Application/RadioPad.Application.csproj backend/RadioPad.Api/src/RadioPad.Application/
COPY backend/RadioPad.Api/src/RadioPad.Validation/RadioPad.Validation.csproj backend/RadioPad.Api/src/RadioPad.Validation/
COPY backend/RadioPad.Api/src/RadioPad.Infrastructure/RadioPad.Infrastructure.csproj backend/RadioPad.Api/src/RadioPad.Infrastructure/
COPY backend/RadioPad.Api/src/RadioPad.Api/RadioPad.Api.csproj backend/RadioPad.Api/src/RadioPad.Api/
RUN dotnet restore backend/RadioPad.Api/src/RadioPad.Api/RadioPad.Api.csproj
COPY backend/RadioPad.Api backend/RadioPad.Api
COPY rulebooks rulebooks
COPY templates templates
RUN dotnet publish backend/RadioPad.Api/src/RadioPad.Api/RadioPad.Api.csproj -c Release -o /out --no-restore \
    && mkdir -p /out/rulebooks /out/templates \
    && cp -R rulebooks/. /out/rulebooks/ \
    && cp -R templates/. /out/templates/

# Gemini CLI (provider adapter gemini-cli) — Node runtime + globally installed
# @google/gemini-cli. Version pinned so image rebuilds are reproducible.
# OAuth credentials are NOT baked in: the container mounts the operator's
# ~/.gemini (oauth_creds.json) as a volume — see the VPS docker-compose.yml.
FROM node:22-bookworm-slim AS geminicli
RUN npm install -g @google/gemini-cli@0.50.0

FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*
COPY --from=geminicli /usr/local/bin/node /usr/local/bin/node
COPY --from=geminicli /usr/local/lib/node_modules /usr/local/lib/node_modules
# Auth: gemini-api-key (Google discontinued oauth-personal for this CLI,
# 2026-07: IneligibleTierError UNSUPPORTED_CLIENT). The key itself comes from
# GEMINI_API_KEY in the VPS .secrets.env — never baked into the image. The
# trust env silences the headless workspace-trust gate.
RUN ln -sf /usr/local/lib/node_modules/@google/gemini-cli/dist/index.js /usr/local/bin/gemini \
    && chmod +x /usr/local/lib/node_modules/@google/gemini-cli/dist/index.js \
    && /usr/local/bin/gemini --version \
    && mkdir -p /root/.gemini \
    && printf '{\n  "selectedAuthType": "gemini-api-key",\n  "security": { "auth": { "selectedType": "gemini-api-key" } }\n}\n' > /root/.gemini/settings.json
ENV GEMINI_CLI_TRUST_WORKSPACE=true
COPY --from=build /out ./
ENV ASPNETCORE_ENVIRONMENT=Production
ENV RADIOPAD_BIND=http://0.0.0.0:7457
EXPOSE 7457
ENTRYPOINT ["dotnet", "RadioPad.Api.dll"]
