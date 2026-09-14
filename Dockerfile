# Floating 10.0 tag: always the current .NET 10 SDK patch. global.json (copied below) governs
# the minimum SDK feature band via rollForward=latestFeature.
FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG TARGETARCH
# .dockerignore excludes .git, so the SDK cannot derive the commit sha itself; CD passes it in.
ARG SOURCE_REVISION=
WORKDIR /src

# Repo-wide build settings must be an ancestor of the project dirs, otherwise MSBuild silently
# builds without TreatWarningsAsErrors / lockfile enforcement / LangVersion inside the image.
COPY Directory.Build.props global.json ./

# Copy project files + lockfiles first for layer caching on NuGet restore.
# Lockfiles make the restore in this layer resolve the same graph the repo pins.
COPY src/ObjeX.Api/ObjeX.Api.csproj src/ObjeX.Api/packages.lock.json ObjeX.Api/
COPY src/ObjeX.Core/ObjeX.Core.csproj src/ObjeX.Core/packages.lock.json ObjeX.Core/
COPY src/ObjeX.Infrastructure/ObjeX.Infrastructure.csproj src/ObjeX.Infrastructure/packages.lock.json ObjeX.Infrastructure/
COPY src/ObjeX.Migrations.PostgreSql/ObjeX.Migrations.PostgreSql.csproj src/ObjeX.Migrations.PostgreSql/packages.lock.json ObjeX.Migrations.PostgreSql/
COPY src/ObjeX.Web/ObjeX.Web.csproj src/ObjeX.Web/packages.lock.json ObjeX.Web/
RUN dotnet restore ObjeX.Api/ObjeX.Api.csproj -a $TARGETARCH

# Copy remaining source and publish
COPY src/ .
RUN dotnet publish ObjeX.Api/ObjeX.Api.csproj \
    -c Release \
    -a $TARGETARCH \
    --no-self-contained \
    --no-restore \
    -p:SourceRevisionId=$SOURCE_REVISION \
    -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0

# Install curl for container healthcheck, then clean up
RUN apt-get update && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /app
RUN mkdir -p /data/db /data/blobs && chown -R app:app /data
COPY --from=build --chown=app:app /app/publish .

# Entrypoint: fix bind mount ownership, then drop to non-root
RUN printf '#!/bin/sh\nif [ "$(id -u)" = "0" ]; then\n  chown -R app:app /data 2>/dev/null || true\n  exec setpriv --reuid=app --regid=app --init-groups dotnet ObjeX.Api.dll "$@"\nelse\n  exec dotnet ObjeX.Api.dll "$@"\nfi\n' > /entrypoint.sh && chmod 755 /entrypoint.sh

# The aspnet base image sets ASPNETCORE_HTTP_PORTS=8080. Ports come from Server:UiPort/Server:S3Port
# (Kestrel listeners in code); clearing this avoids an "Overriding address(es)" warning on every start.
ENV ASPNETCORE_HTTP_PORTS=
ENV ConnectionStrings__DefaultConnection="Data Source=/data/db/objex.db"
ENV Storage__BasePath="/data/blobs"
VOLUME ["/data"]
EXPOSE 9001
EXPOSE 9000

HEALTHCHECK --interval=30s --timeout=5s --retries=3 \
    CMD curl -f http://localhost:9001/health || exit 1

ENTRYPOINT ["/entrypoint.sh"]
