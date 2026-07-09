# CI/CD: build on GitHub → deploy to the Synology NAS

How a push to `main` becomes a running deploy on the DS220+ (the M1 deploy target, plan D21), with **no inbound
exposure** of the NAS and **no long-lived registry credentials** to manage.

> Supersedes the manual "build-and-push from the dev machine" idea — the same images, but built by CI and deployed by
> the NAS itself.

## The shape

```
 push to main ──▶ GitHub Actions (cloud runner)            ──▶ GHCR
                  └ build migrator/api/worker (linux/amd64)     ghcr.io/<owner>/zhua-*:{sha,latest}
                                                                      │
 GitHub Actions (self-hosted runner ON the NAS) ◀───────────────────┘
   └ docker compose pull + up -d  (talks to the host Docker via the mounted socket)
        └ migrator runs once → postgres + api + worker come up
```

**Why this shape**
- **Build in the cloud** — fast, free minutes, and the heavy ~2 GB Playwright Worker image isn't built on the NAS.
- **Deploy on the NAS via a self-hosted runner** — the runner makes an **outbound** connection to GitHub, so the NAS
  needs **no open ports, no SSH-from-internet, no tunnel**. The deploy is a normal CI job (gated, logged, re-runnable).
- **GHCR auth via `GITHUB_TOKEN`** — both push (build job) and pull (deploy job) use the workflow's built-in token, so
  there's **no PAT** stored anywhere. The only secret is the DB password.

## Images

`ghcr.io/<owner>/zhua-migrator`, `…/zhua-api`, `…/zhua-worker`. Tags: `latest` + the commit `sha` (deploys are pinned
to the exact `sha` that was built; `latest` is for humans). Postgres uses the official `postgres:16` image (not built).

---

## One-time setup

### A. NAS prep
1. **Container Manager** installed (DSM Package Center).
2. **Shared folder** `/volume1/docker/Zhua/` with `pgdata/` + `crawl-archive/` + `runner/` (bind-mount targets — DB +
   raw archive stay visible/backup-able from DSM). **Path is case-sensitive** — the folder is `Zhua` (capital Z), so
   the compose bind-mounts use `/volume1/docker/Zhua/...`.
3. The host Docker socket is at `/var/run/docker.sock` (Container Manager provides it).

### B. GitHub repo
4. **Actions secret** `POSTGRES_PASSWORD` (Settings → Secrets and variables → Actions). That's the *only* secret.
5. GHCR is automatic for the repo owner; after the first push the 3 packages appear under the owner's Packages. Set
   their visibility to **private** (default) — the deploy job authenticates, so private is fine.

### C. Self-hosted runner on the NAS
Run the runner as a container (Container Manager → Project, or SSH). It needs the **Docker socket** (to drive
`docker compose` on the host) and a registration token from **repo → Settings → Actions → Runners → New self-hosted
runner** (copy the `--token`). Label it **`nas`**.

```yaml
# runner-compose.yml on the NAS  (one-time; keep it running)
services:
  gh-runner:
    image: myoung34/github-runner:latest
    restart: unless-stopped
    environment:
      REPO_URL: https://github.com/<owner>/Zhua.Food
      RUNNER_NAME: nas
      RUNNER_TOKEN: <registration-token>     # from the "New self-hosted runner" page
      LABELS: nas
      RUNNER_SCOPE: repo
    volumes:
      - /var/run/docker.sock:/var/run/docker.sock   # drive the host Docker (sibling containers)
      - /volume1/docker/Zhua/runner:/tmp/runner      # work dir on the NAS
```

> The runner runs `docker compose` against the **host** daemon via the mounted socket, so the compose file's
> bind-mount paths (`/volume1/docker/zhua/...`) and the resulting containers live on the NAS host, not inside the runner.

---

## The pipeline

### `.github/workflows/deploy.yml`

```yaml
name: deploy
on:
  push:
    branches: [main]
  workflow_dispatch:                 # manual run + rollback (pick a sha)
    inputs:
      sha:
        description: "Image sha to deploy (blank = this run's sha)"
        required: false

permissions:
  contents: read
  packages: write                    # push to GHCR with GITHUB_TOKEN

jobs:
  build:
    runs-on: ubuntu-latest
    strategy:
      matrix:
        include:
          - { image: zhua-migrator, dockerfile: src/Zhua.Migrator/Dockerfile }
          - { image: zhua-api,      dockerfile: src/Zhua.Api/Dockerfile }
          - { image: zhua-worker,   dockerfile: src/Zhua.Worker/Dockerfile }
    steps:
      - uses: actions/checkout@v4
      - uses: docker/login-action@v3
        with:
          registry: ghcr.io
          username: ${{ github.actor }}
          password: ${{ secrets.GITHUB_TOKEN }}
      - uses: docker/build-push-action@v6
        with:
          context: .                 # Dockerfiles COPY Directory.Build.props from the root (D19)
          file: ${{ matrix.dockerfile }}
          platforms: linux/amd64     # DS220+ is x86-64
          push: true
          tags: |
            ghcr.io/${{ github.repository_owner }}/${{ matrix.image }}:latest
            ghcr.io/${{ github.repository_owner }}/${{ matrix.image }}:${{ github.sha }}
          cache-from: type=gha
          cache-to: type=gha,mode=max

  deploy:
    needs: build
    runs-on: [self-hosted, nas]      # the runner ON the NAS
    steps:
      - uses: actions/checkout@v4    # get docker-compose.nas.yml
      - uses: docker/login-action@v3
        with:
          registry: ghcr.io
          username: ${{ github.actor }}
          password: ${{ secrets.GITHUB_TOKEN }}
      - name: Pull + up
        env:
          IMAGE_OWNER: ${{ github.repository_owner }}
          IMAGE_TAG: ${{ github.event.inputs.sha || github.sha }}
          POSTGRES_PASSWORD: ${{ secrets.POSTGRES_PASSWORD }}
        run: |
          docker compose -f docker-compose.nas.yml pull
          docker compose -f docker-compose.nas.yml up -d --remove-orphans
```

### `docker-compose.nas.yml` (written — Phase B done)

Same four services as `docker-compose.yml`, but **`image:` (pulled) not `build:`**, env-interpolated. The real file is
at the repo root; the shape:

```yaml
# sketch — the deploy job sets IMAGE_OWNER / IMAGE_TAG / POSTGRES_PASSWORD
  api:
    image: ghcr.io/${IMAGE_OWNER}/zhua-api:${IMAGE_TAG:-latest}
    environment:
      ConnectionStrings__Default: "Host=postgres;Port=5432;Database=zhua;Username=zhua;Password=${POSTGRES_PASSWORD}"
      ASPNETCORE_ENVIRONMENT: Production
  postgres:
    volumes: [ /volume1/docker/zhua/pgdata:/var/lib/postgresql/data ]   # bind-mount, not a named volume
  worker:
    image: ghcr.io/${IMAGE_OWNER}/zhua-worker:${IMAGE_TAG:-latest}
    shm_size: "1gb"
    volumes: [ /volume1/docker/zhua/crawl-archive:/app/crawl-archive ]
```

The migrator stays a one-shot (`restart: "no"`); `up -d` recreates + re-runs it when its image `sha` changes, so new
migrations apply on every deploy before api/worker start (D5).

---

## Day-to-day

- **Deploy:** merge/push to `main` → build + deploy run automatically. `main` = what's on the NAS.
- **Rollback:** Actions → `deploy` → *Run workflow* → enter a previous `sha`. (Migrations don't auto-rollback — a
  rollback that crosses a schema change needs a down-migration; avoid by keeping migrations additive.)
- **Logs/health:** `curl http://<nas-ip>:8080/health` `/health/db`; `docker compose -f docker-compose.nas.yml logs -f worker`.

## Open decisions (call these before we wire it)
- **Trigger:** deploy on *every* push to `main` (simple, "main = prod") vs deploy only on a `v*` **tag** (more
  deliberate). Default here is push-to-`main`; easy to switch to tags.
- **Admin auth must land before the API is reachable beyond the LAN** (it's currently open). Keep it LAN-only until then.
- **HTTPS** via DSM reverse proxy + a domain — later.

## Lighter alternative (no runner)
If a self-hosted runner feels heavy: run **Watchtower** on the NAS to poll GHCR and auto-pull/restart when `:latest`
moves. Zero deploy job, but you lose per-deploy control + logs and it redeploys on any `latest` push. The runner is the
recommended, more controllable path.
