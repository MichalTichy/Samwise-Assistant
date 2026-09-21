# syntax=docker/dockerfile:1.7

FROM node:22-bookworm-slim AS node

FROM mcr.microsoft.com/dotnet/sdk:10.0.301-noble AS build
WORKDIR /source
COPY --from=node /usr/local/ /usr/local/

COPY Directory.Build.props global.json Samwise.slnx ./
COPY SharedInfrastructure/Shared.Infrastructure.CurrentTenancyProvider/Shared.Infrastructure.CurrentTenancyProvider.csproj SharedInfrastructure/Shared.Infrastructure.CurrentTenancyProvider/
COPY SharedInfrastructure/Shared.Infrastructure.Initializer/Shared.Infrastructure.Initializer.csproj SharedInfrastructure/Shared.Infrastructure.Initializer/
COPY SharedInfrastructure/Shared.Infrastructure.IoC/Shared.Infrastructure.IoC.csproj SharedInfrastructure/Shared.Infrastructure.IoC/
COPY SharedInfrastructure/Shared.Infrastructure.LeaderElection/Shared.Infrastructure.LeaderElection.csproj SharedInfrastructure/Shared.Infrastructure.LeaderElection/
COPY SharedInfrastructure/Shared.Infrastructure.Persistence.Marten/Shared.Infrastructure.Persistence.Marten.csproj SharedInfrastructure/Shared.Infrastructure.Persistence.Marten/
COPY SharedInfrastructure/Shared.Infrastructure.Persistence/Shared.Infrastructure.Persistence.csproj SharedInfrastructure/Shared.Infrastructure.Persistence/
COPY SharedInfrastructure/Shared.Infrastructure.UnitOfWork/Shared.Infrastructure.UnitOfWork.csproj SharedInfrastructure/Shared.Infrastructure.UnitOfWork/
COPY src/Samwise.Domain/Samwise.Domain.csproj src/Samwise.Domain/
COPY src/Samwise.Infrastructure.AgentRuntimes/Samwise.Infrastructure.AgentRuntimes.csproj src/Samwise.Infrastructure.AgentRuntimes/
COPY src/Samwise.Infrastructure.CodexGateway/Samwise.Infrastructure.CodexGateway.csproj src/Samwise.Infrastructure.CodexGateway/
COPY src/Samwise.Infrastructure.Email.Imap/Samwise.Infrastructure.Email.Imap.csproj src/Samwise.Infrastructure.Email.Imap/
COPY src/Samwise.Infrastructure.Email.Microsoft365/Samwise.Infrastructure.Email.Microsoft365.csproj src/Samwise.Infrastructure.Email.Microsoft365/
COPY src/Samwise.Infrastructure.Mcp/Samwise.Infrastructure.Mcp.csproj src/Samwise.Infrastructure.Mcp/
COPY src/Samwise.Infrastructure.Teams/Samwise.Infrastructure.Teams.csproj src/Samwise.Infrastructure.Teams/
COPY src/Samwise.Infrastructure.Todoist/Samwise.Infrastructure.Todoist.csproj src/Samwise.Infrastructure.Todoist/
COPY src/Samwise.Infrastructure.Persistence.Identity/Samwise.Infrastructure.Persistence.Identity.csproj src/Samwise.Infrastructure.Persistence.Identity/
COPY src/Samwise.Infrastructure.Persistence.Marten/Samwise.Infrastructure.Persistence.Marten.csproj src/Samwise.Infrastructure.Persistence.Marten/
COPY src/Samwise.Inputs.Email/Samwise.Inputs.Email.csproj src/Samwise.Inputs.Email/
COPY src/Samwise.Inputs.Manual/Samwise.Inputs.Manual.csproj src/Samwise.Inputs.Manual/
COPY src/Samwise.Inputs.Teams/Samwise.Inputs.Teams.csproj src/Samwise.Inputs.Teams/
COPY src/Samwise.Inputs.Todoist/Samwise.Inputs.Todoist.csproj src/Samwise.Inputs.Todoist/
COPY src/Samwise.Logic/Samwise.Logic.csproj src/Samwise.Logic/
COPY src/Samwise.ServiceDefaults/Samwise.ServiceDefaults.csproj src/Samwise.ServiceDefaults/
COPY src/Samwise.Web/Samwise.Web.csproj src/Samwise.Web/
RUN dotnet restore src/Samwise.Web/Samwise.Web.csproj

COPY SharedInfrastructure/ SharedInfrastructure/
COPY src/ src/
# LumexUI 2.4.0's theme-copy target uses Windows path separators. Copy the
# restored package theme explicitly so Tailwind can build on Linux.
RUN mkdir -p src/Samwise.Web/bin/lumexui \
    && cp -R /root/.nuget/packages/lumexui/*/theme/. src/Samwise.Web/bin/lumexui/
RUN npm ci --prefix src/Samwise.Web
RUN dotnet publish src/Samwise.Web/Samwise.Web.csproj \
    --configuration Release \
    --no-restore \
    --output /app/publish \
    /p:UseAppHost=false \
    && test -f /app/publish/wwwroot/_framework/blazor.web.js

FROM mcr.microsoft.com/dotnet/aspnet:10.0.9-noble AS runtime
WORKDIR /app

RUN apt-get update \
    && apt-get install --yes --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/* \
    && mkdir -p /app/data-protection \
    && chown -R "$APP_UID:$APP_UID" /app

COPY --from=build --chown=$APP_UID:$APP_UID /app/publish .

ENV ASPNETCORE_HTTP_PORTS=8080 \
    DataProtection__KeysPath=/app/data-protection

VOLUME ["/app/data-protection"]
EXPOSE 8080
USER $APP_UID

HEALTHCHECK --interval=30s --timeout=5s --start-period=30s --retries=3 \
    CMD curl --fail --silent --show-error http://127.0.0.1:8080/health || exit 1

ENTRYPOINT ["dotnet", "Samwise.Web.dll"]
