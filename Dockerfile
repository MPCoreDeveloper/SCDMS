# ── SCDMS ──
# Multi-stage build for the .NET 10 production container.
# Image: ghcr.io/mpcoredeveloper/scdms
#
# Container defaults: plain HTTP on :8080 (TLS terminated by a reverse proxy that
# holds a publicly trusted certificate, e.g. Let's Encrypt via the bundled Caddy).
# All runtime configuration is supplied through SCDMS__* environment variables.

# ── Stage 1: Build ──
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy solution-level files first for layer caching
COPY Directory.Build.props Directory.Packages.props NuGet.Config ./

# Copy the project file (restore layer)
COPY src/SCDMS/SCDMS.csproj src/SCDMS/

RUN dotnet restore src/SCDMS/SCDMS.csproj --configfile NuGet.Config

# Copy source and publish (framework-dependent; the aspnet runtime image supplies the framework)
COPY src/ src/
RUN dotnet publish src/SCDMS/SCDMS.csproj \
    -c Release \
    --no-restore \
    -o /app/publish

# ── Stage 2: Runtime ──
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# Install curl for the container HEALTHCHECK
RUN apt-get update && \
    apt-get install -y --no-install-recommends curl && \
    rm -rf /var/lib/apt/lists/*

# Labels
LABEL maintainer="MPCoreDeveloper" \
      org.opencontainers.image.title="SCDMS" \
      org.opencontainers.image.description="SCDMS - Sharp Core Database Management System (web studio)" \
      org.opencontainers.image.source="https://github.com/MPCoreDeveloper/SCDMS"

# Create a non-root user (uid 1000: group/user reuse keeps bind-mounted volumes usable)
RUN if getent group 1000 >/dev/null; then \
      group_name=$(getent group 1000 | cut -d: -f1); \
    else \
      groupadd --gid 1000 scdms; \
      group_name=scdms; \
    fi && \
    if getent passwd 1000 >/dev/null; then \
      user_name=$(getent passwd 1000 | cut -d: -f1); \
    else \
      useradd --uid 1000 --gid "$group_name" --shell /bin/false scdms; \
      user_name=scdms; \
    fi && \
    mkdir -p /app/data && \
    chown -R "$user_name":"$group_name" /app

# Copy published application
COPY --from=build /app/publish .

# Environment variables (overridable). Container defaults: plain HTTP on :8080,
# data root on the /app/data volume.
ENV ASPNETCORE_URLS="http://+:8080" \
    ASPNETCORE_ENVIRONMENT="Production" \
    SCDMS__EnableHttps="false" \
    SCDMS__BindAddress="0.0.0.0" \
    SCDMS__DataDirectory="/app/data" \
    DOTNET_EnableDiagnostics=0

# Expose HTTP (8080) - TLS is terminated at the reverse proxy
EXPOSE 8080

# Health check
HEALTHCHECK --interval=30s --timeout=5s --start-period=10s --retries=3 \
    CMD curl -fs http://localhost:8080/health || exit 1

# Run as non-root
USER 1000:1000

ENTRYPOINT ["dotnet", "scdms.dll"]
