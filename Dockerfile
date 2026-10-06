# Floating 10.0 tag: always the current .NET 10 SDK patch. global.json (copied below) governs
# the minimum SDK feature band via rollForward=latestFeature.
FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG TARGETARCH
# .dockerignore excludes .git, so the SDK cannot derive the commit sha itself; CD passes it in.
ARG SOURCE_REVISION=
ARG VERSION=0.0.0
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
    -p:Version=$VERSION \
    -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0

ARG SOURCE_REVISION=
LABEL org.opencontainers.image.source="https://github.com/centrolabs/ObjeX" \
      org.opencontainers.image.revision="${SOURCE_REVISION}"

# Install curl for container healthcheck, then clean up
RUN apt-get update && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /app
# Named volumes take the owner of /data from the image. A bind mount needs chown 1654:1654 on the host.
RUN mkdir -p /data/db /data/blobs && chown -R app:app /data
COPY --from=build /app/publish .
USER $APP_UID

# The aspnet base image sets ASPNETCORE_HTTP_PORTS=8080. Ports come from Server:UiPort/Server:S3Port
# (Kestrel listeners in code); clearing this avoids an "Overriding address(es)" warning on every start.
ENV ASPNETCORE_HTTP_PORTS=
ENV ConnectionStrings__DefaultConnection="Data Source=/data/db/objex.db"
ENV Storage__BasePath="/data/blobs"
# Logs go to stdout only; docker logs and kubectl logs collect them.
ENV Log__FilePath=
VOLUME ["/data"]
EXPOSE 9001
EXPOSE 9000

HEALTHCHECK --interval=30s --timeout=5s --retries=3 \
    CMD curl -f http://localhost:9001/health || exit 1

ENTRYPOINT ["dotnet", "ObjeX.Api.dll"]
