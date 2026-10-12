# Unique Lighting Studio API: container image for Render (or any Docker host).
# Build context is the repository root:  docker build -t unique-lights-api .

FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# Restore first so the package layer is cached between builds.
COPY UniqueLights.Api/UniqueLights.Api.csproj UniqueLights.Api/
RUN dotnet restore UniqueLights.Api/UniqueLights.Api.csproj

COPY UniqueLights.Api/ UniqueLights.Api/
RUN dotnet publish UniqueLights.Api/UniqueLights.Api.csproj -c Release -o /app/publish --no-restore /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .

# Serilog writes rolling files to /app/logs; give the non-root runtime user a writable folder.
RUN mkdir -p /app/logs && chown -R $APP_UID /app/logs
USER $APP_UID

ENV ASPNETCORE_ENVIRONMENT=Production \
    DOTNET_RUNNING_IN_CONTAINER=true

# Render injects PORT (10000 by default). Fall back to 8080 elsewhere.
EXPOSE 10000
CMD ["sh", "-c", "ASPNETCORE_HTTP_PORTS=${PORT:-8080} exec dotnet UniqueLights.Api.dll"]
