#!/usr/bin/env bash
# Back up the volumes holding data you cannot regenerate.
#
#     ./backup.sh [destination-dir]        (default: /var/backups/cleanarch)
#     0 3 * * *  /opt/cleanarch/observability/prod/backup.sh >> /var/log/cleanarch-backup.log 2>&1
#
# Backed up:      es-data (the audit trail — irreplaceable), grafana-data (users,
#                 dashboard edits).
# Not backed up:  prometheus/loki/tempo data. Telemetry is a stream with a 30-day
#                 window; restoring last night is rarely worth the disk.
#
# THE APPLICATION DATABASES ARE NOT ON THIS MACHINE. The SQLite files live on the
# IIS server and need their own job there. Backing up this VM is not backing up
# the application.
#
# A backup you have never restored is a hypothesis. The restore command is at the
# bottom of this file — try it into a throwaway VM at least once.
set -euo pipefail

DEST="${1:-/var/backups/cleanarch}"
STAMP="$(date +%Y%m%d-%H%M%S)"
PROJECT="cleanarch-prod"                 # matches "name:" in docker-compose.yml
KEEP_DAYS="${KEEP_DAYS:-14}"

mkdir -p "$DEST"

for vol in es-data grafana-data; do
  full="${PROJECT}_${vol}"
  out="${DEST}/${vol}-${STAMP}.tar.gz"

  if ! docker volume inspect "$full" >/dev/null 2>&1; then
    echo "skip   $full (not present)"
    continue
  fi

  # Mount the volume into a throwaway container and tar it from there — works
  # wherever Docker keeps its volumes.
  docker run --rm -v "${full}:/source:ro" -v "${DEST}:/backup" alpine:3.21 \
    tar czf "/backup/$(basename "$out")" -C /source .

  echo "saved  $full -> $out ($(du -h "$out" | cut -f1))"
done

find "$DEST" -name '*.tar.gz' -mtime "+${KEEP_DAYS}" -print -delete
echo "Done. ${KEEP_DAYS}-day retention applied to ${DEST}."
echo "OFFSITE: copy these elsewhere (rsync/rclone/S3) — they are on the same disk as the data."

cat <<'RESTORE'

To restore one volume:
  docker compose down
  docker volume rm cleanarch-prod_es-data
  docker volume create cleanarch-prod_es-data
  docker run --rm -v cleanarch-prod_es-data:/target -v /var/backups/cleanarch:/backup \
    alpine:3.21 tar xzf /backup/es-data-YYYYMMDD-HHMMSS.tar.gz -C /target
  docker compose up -d
RESTORE
