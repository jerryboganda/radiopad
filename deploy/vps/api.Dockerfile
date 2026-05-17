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

FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*
COPY --from=build /out ./
ENV ASPNETCORE_ENVIRONMENT=Production
ENV RADIOPAD_BIND=http://0.0.0.0:7457
EXPOSE 7457
ENTRYPOINT ["dotnet", "RadioPad.Api.dll"]
