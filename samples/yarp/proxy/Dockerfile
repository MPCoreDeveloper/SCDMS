# ── YARP reverse proxy (SCDMS + SharpCoreDB) ──
# All-.NET TLS-terminating proxy sample.
# Image: yarp-proxy (local build from samples/yarp/proxy)

# ── Stage 1: Build ──
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY YarpProxy.csproj .
RUN dotnet restore YarpProxy.csproj

COPY . .
RUN dotnet publish YarpProxy.csproj \
    -c Release \
    --no-restore \
    -o /app/publish

# ── Stage 2: Runtime ──
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# curl for the container HEALTHCHECK
RUN apt-get update && \
    apt-get install -y --no-install-recommends curl && \
    rm -rf /var/lib/apt/lists/*

COPY --from=build /app/publish .

# Kestrel endpoints are configured in Program.cs from environment variables (YARP_HTTP_PORT /
# YARP_HTTPS_PORT). Exposed for documentation; the HTTPS listener terminates TLS for both the
# SCDMS web UI and the SharpCoreDB gRPC endpoint.
EXPOSE 80 443

# Health check (http listener; /health is excluded from the https redirect).
HEALTHCHECK --interval=30s --timeout=5s --start-period=10s --retries=3 \
    CMD curl -fs http://localhost:80/health || exit 1

# The proxy runs as a non-root user; binding 80/443 needs the NET_BIND_SERVICE capability
# (added via cap_add in docker-compose.yml). Map higher ports (YARP_HTTP_PORT/YARP_HTTPS_PORT)
# when capabilities cannot be granted.
USER 1000:1000

ENTRYPOINT ["dotnet", "YarpProxy.dll"]
