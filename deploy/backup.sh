#!/usr/bin/env bash
# Nightly database backup (cron, 03:00): /srv/backups/pg/mqm-YYYYmmdd-HHMMSS.dump, kept 14 days.
# Restore: docker compose exec -T mqm-db pg_restore -U mqm -d mqm --clean --if-exists --no-owner --no-acl < FILE
# Uploads (mqm_data volume) are not in here.
set -euo pipefail

readonly COMPOSE_DIR=/srv
readonly BACKUP_DIR=/srv/backups/pg
readonly KEEP_DAYS=14

cd "$COMPOSE_DIR"
mkdir -p "$BACKUP_DIR"
umask 077

target="$BACKUP_DIR/mqm-$(date -u +%Y%m%d-%H%M%S).dump"
partial="$target.partial"
trap 'rm -f "$partial"' EXIT

docker compose exec -T mqm-db pg_dump -U mqm -d mqm -Fc > "$partial"
if [[ ! -s "$partial" ]]; then
    echo "backup: pg_dump wrote nothing" >&2
    exit 1
fi
mv "$partial" "$target"
echo "backup: $(basename "$target") $(du -h "$target" | cut -f1)"

# Only this script's own files: render.dump and other manual dumps stay.
find "$BACKUP_DIR" -maxdepth 1 -name 'mqm-*.dump' -mtime +$((KEEP_DAYS - 1)) -print -delete
