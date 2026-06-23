# Query API image (plan D20). Build context = repo root.
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# Shared build settings (TFM/Nullable/etc.) — every csproj imports this (D19); copy it before restore.
COPY Directory.Build.props ./

# Restore against the csproj graph first (better layer caching).
COPY src/Zhua.Domain/Zhua.Domain.csproj src/Zhua.Domain/
COPY src/Zhua.Application/Zhua.Application.csproj src/Zhua.Application/
COPY src/Zhua.Infrastructure/Zhua.Infrastructure.csproj src/Zhua.Infrastructure/
COPY src/Zhua.Api/Zhua.Api.csproj src/Zhua.Api/
RUN dotnet restore src/Zhua.Api/Zhua.Api.csproj

# Publish the Api (and the projects it references).
COPY src/ src/
RUN dotnet publish src/Zhua.Api/Zhua.Api.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS final
WORKDIR /app
COPY --from=build /app ./
EXPOSE 8080
ENTRYPOINT ["dotnet", "Zhua.Api.dll"]
