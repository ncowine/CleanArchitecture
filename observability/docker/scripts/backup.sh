#!/usr/bin/env bash
# =============================================================================
#  Back up the volumes that hold data you cannot regenerate
# =============================================================================
#     ./scripts/backup.sh [destination-dir]      (default: /var/backups/cleanarch)
#
# Run it from cron, nightly:
#     0 3 * * * /opt/cleanarch/observability/docker/scripts/backup.sh >> /var/log/cleanarch-backup.log 2>&1
#
# ── What is backed up, and what is deliberately not ──────────────────────────
#   es-data             the audit trail (if ELK is running)  — IRREPLACEABLE
#   grafana-data        users, dashboard edits, preferences  — annoying to lose
#   caddy-data          TLS certificates and the ACME account key
#
# Not backed up: prometheus-data, loki-data, tempo-data. Telemetry is a stream
# with a retention window measured in weeks; restoring last night's metrics is
# rarely worth the disk. Add them if that is not true for you.
#
# ⚠️  THE APPLICATION DATABASES ARE NOT ON THIS MACHINE.
#     The four SQLite files live on the Windows/IIS server, and nothing in this
#     script touches them. They are the most irreplaceable data you have, and
#     they need their own backup job over there — see README-windows-iis.md.
#     Backing up this VM is not backing up the application.
#
# ── The part people skip ─────────────────────────────────────────────────────
# A backup you have never restored is a hypothesis, not a backup. Restore one
# into a throwaway VM at least once, and again whenever the stack changes.
# The restore command is at the bottom of this file.
# =============================================================================
set -euo pipefail

DEST="${1:-/var/backups/cleanarch}"
STAMP="$(date +%Y%m%d-%H%M%S)"
PROJECT="cleanarch-prod"          # matches "name:" in docker-compose.prod.yml
KEEP_DAYS="${KEEP_DAYS:-14}"

VOLUMES=(grafana-data caddy-data es-data)

mkdir -p "$DEST"

for vol in "${VOLUMES[@]}"; do
  full="${PROJECT}_${vol}"

  # es-data only exists when the ELK add-on has been started. Skip quietly.
  if ! docker volume inspect "$full" >/dev/null 2>&1; then
    echo "skip   $full (not present)"
    continue
  fi

  out="${DEST}/${vol}-${STAMP}.tar.gz"

  # Mount the volume into a throwaway container and tar it from there. This
  # works regardless of where Docker stores volumes on the host, and needs no
  # knowledge of /var/lib/docker.
  #
  docker run --rm \
    -v "${full}:/source:ro" \
    -v "${DEST}:/backup" \
    alpine:3.21 \
    tar czf "/backup/$(basename "$out")" -C /source .

  echo "backed up  $full  ->  $out  ($(du -h "$out" | cut -f1))"
done

# Prune old archives so the backup directory does not become the thing that
# fills the disk.
find "$DEST" -name '*.tar.gz' -mtime "+${KEEP_DAYS}" -print -delete

echo
echo "Done. ${KEEP_DAYS}-day retention applied to ${DEST}."
echo
echo "OFFSITE: these files are on the same machine as the data they protect,"
echo "so they survive a bad deploy but not a dead disk or a lost VM. Copy them"
echo "somewhere else (rsync/rclone/S3) for that to be a real backup."
echo
cat <<'RESTORE'
── To restore one volume ─────────────────────────────────────────────────────
  docker compose -f docker-compose.prod.yml down
  docker volume rm cleanarch-prod_api-data
  docker volume create cleanarch-prod_api-data
  docker run --rm -v cleanarch-prod_api-data:/target -v /var/backups/cleanarch:/backup \
    alpine:3.21 tar xzf /backup/api-data-YYYYMMDD-HHMMSS.tar.gz -C /target
  docker compose -f docker-compose.prod.yml up -d
RESTORE
