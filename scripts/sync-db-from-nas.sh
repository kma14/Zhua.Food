#!/usr/bin/env bash
#
# sync-db-from-nas.sh — copy the NAS (production) Postgres DB down to the local dev DB.
#
# Direction is ONE-WAY: NAS -> local. The NAS is the source of truth (real twice-daily
# crawl history); local dev data is disposable. This script OVERWRITES the local DB and
# NEVER writes to the NAS. Do not "sync" the other way — dev data must not touch prod.
#
# How it works (the NAS Postgres has no published host port, by design):
#   ssh NAS -> `docker exec ... pg_dump -Fc`  ==(binary over ssh)==>  local file
#   local: `docker exec -i ... pg_restore --clean --if-exists`  <== that file
# So you only need `docker` + `ssh` locally — no psql client on Windows. Run it in Git Bash.
#
# ⚠️  SCHEMA VERSIONS MUST MATCH. A full dump/restore also copies the NAS *schema*, so if
#     local is on a newer migration than the NAS (e.g. local has `Sku`, NAS still `SourceSku`
#     before the rename is deployed), this REVERTS local's schema to the NAS's. Sync only when
#     both sides are on the same migration (deploy pending migrations to the NAS first), or
#     re-run `dotnet ef database update` locally afterwards.
#
# Usage:
#   NAS_SSH=user@192.168.1.5 ./scripts/sync-db-from-nas.sh
# Override any of these via env vars (defaults shown):
set -euo pipefail

NAS_SSH="${NAS_SSH:?set NAS_SSH, e.g. NAS_SSH=kevin@192.168.1.5}"   # ssh target for the NAS
NAS_CONTAINER="${NAS_CONTAINER:-zhua-food-postgres-1}"   # verify with: ssh $NAS_SSH docker ps
LOCAL_CONTAINER="${LOCAL_CONTAINER:-zhuafood-postgres-1}"           # local dev postgres container
DB="${DB:-zhua}"
DB_USER="${DB_USER:-zhua}"

dump="$(mktemp -t nas-zhua.XXXXXX.dump)"
trap 'rm -f "$dump"' EXIT

echo ">> Dumping '$DB' from NAS ($NAS_SSH : $NAS_CONTAINER) ..."
ssh "$NAS_SSH" "docker exec $NAS_CONTAINER pg_dump -U $DB_USER -d $DB -Fc" > "$dump"
echo "   got $(wc -c < "$dump") bytes"

echo ">> This will OVERWRITE the local DB in container '$LOCAL_CONTAINER'. Ctrl-C to abort."
read -r -p "   Type 'yes' to continue: " ok
[ "$ok" = "yes" ] || { echo "aborted."; exit 1; }

echo ">> Restoring into local ..."
docker exec -i "$LOCAL_CONTAINER" pg_restore -U "$DB_USER" -d "$DB" --clean --if-exists < "$dump"

echo ">> Done. Local '$DB' now mirrors the NAS."
echo "   If local was on a newer migration, re-apply it:"
echo "     dotnet ef database update -p src/Zhua.Infrastructure --startup-project src/Zhua.Infrastructure"
