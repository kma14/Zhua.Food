# One-shot migrator image (plan D5). Build context = repo root.
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# Restore against the csproj graph first (better layer caching).
COPY src/Zhua.Domain/Zhua.Domain.csproj src/Zhua.Domain/
COPY src/Zhua.Application/Zhua.Application.csproj src/Zhua.Application/
COPY src/Zhua.Infrastructure/Zhua.Infrastructure.csproj src/Zhua.Infrastructure/
COPY src/Zhua.Migrator/Zhua.Migrator.csproj src/Zhua.Migrator/
RUN dotnet restore src/Zhua.Migrator/Zhua.Migrator.csproj

# Publish the migrator (and the projects it references).
COPY src/ src/
RUN dotnet publish src/Zhua.Migrator/Zhua.Migrator.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/runtime:9.0 AS final
WORKDIR /app
COPY --from=build /app ./
ENTRYPOINT ["dotnet", "Zhua.Migrator.dll"]
