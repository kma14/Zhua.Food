# Ingestion Worker image (plan D7) — Quartz-scheduled crawl+match driving a HEADED Playwright Chromium.
# Headless Chromium is WAF-blocked (D2/D17), so even on a headless server we must run a real headed browser;
# xvfb gives it an in-memory display nobody looks at. Build context = repo root.
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# Shared build settings (TFM/Nullable/etc.) — every csproj imports this (D19); copy it before restore.
COPY Directory.Build.props ./

# Restore against the csproj graph first (better layer caching).
COPY src/Zhua.Domain/Zhua.Domain.csproj src/Zhua.Domain/
COPY src/Zhua.Application/Zhua.Application.csproj src/Zhua.Application/
COPY src/Zhua.Infrastructure/Zhua.Infrastructure.csproj src/Zhua.Infrastructure/
COPY src/Zhua.Crawling/Zhua.Crawling.csproj src/Zhua.Crawling/
COPY src/Zhua.Worker/Zhua.Worker.csproj src/Zhua.Worker/
RUN dotnet restore src/Zhua.Worker/Zhua.Worker.csproj -r linux-x64

# Publish SELF-CONTAINED for linux-x64: the Playwright image below ships .NET 8, but we target net9.0, so we
# bundle the .NET 9 runtime into the app (produces a native ./Zhua.Worker apphost) and ignore the image's dotnet.
COPY src/ src/
RUN dotnet publish src/Zhua.Worker/Zhua.Worker.csproj -c Release -r linux-x64 --self-contained true -o /app --no-restore

# Final stage: the official Playwright .NET image, PINNED to match our Microsoft.Playwright 1.49.0 NuGet pin.
# It ships Chromium 1.49.0 + every browser OS dependency pre-installed (PLAYWRIGHT_BROWSERS_PATH is preset).
# Its bundled .NET is 8.x — irrelevant, because our app is self-contained (carries its own .NET 9). We add xvfb.
FROM mcr.microsoft.com/playwright/dotnet:v1.49.0-noble AS final
WORKDIR /app

RUN apt-get update \
    && apt-get install -y --no-install-recommends xvfb x11-utils \
    && rm -rf /var/lib/apt/lists/*

COPY --from=build /app ./

# entrypoint.sh starts Xvfb directly (xvfb-run hangs in non-TTY containers) and execs the self-contained
# Worker. Strip any CRLF (file may be authored on Windows) so the shebang resolves, then make it executable.
COPY src/Zhua.Worker/entrypoint.sh /app/entrypoint.sh
RUN sed -i 's/\r$//' /app/entrypoint.sh && chmod +x /app/entrypoint.sh

# No CLI args = Quartz scheduled mode (crawl + match on a cron; D4/D7). Args (crawl/match/recon) pass through.
ENTRYPOINT ["/app/entrypoint.sh"]
