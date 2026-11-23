#!/usr/bin/env bash
# Build the app image for the server (x86_64), ship it over SSH (no registry) and restart the app.
#   ./deploy.sh               build, ship, restart mqm
#   ./deploy.sh --image-only  build and ship only
set -euo pipefail

readonly HOST="${DEPLOY_HOST:-hz}"
readonly IMAGE="mqm:latest"
readonly PLATFORM="linux/amd64"
readonly REPO="$(cd "$(dirname "$0")" && pwd)"
# The repo is on an exFAT drive, whose "._*" files BuildKit can't read: build from a clean copy.
readonly CONTEXT="${TMPDIR:-/tmp}/mqm-build"

restart=true
case "${1:-}" in
    "") ;;
    --image-only) restart=false ;;
    *) echo "usage: $0 [--image-only]" >&2; exit 2 ;;
esac

revision="$(git -C "$REPO" rev-parse --short HEAD)"
if [[ -n "$(git -C "$REPO" status --porcelain)" ]]; then
    revision="$revision-dirty"
    echo "note: uncommitted changes are included ($revision)"
fi

echo "==> Staging build context in $CONTEXT"
mkdir -p "$CONTEXT"
rsync -a --delete \
    --exclude='._*' --exclude='.DS_Store' --exclude='.git/' --exclude='bin/' --exclude='obj/' \
    --exclude='RestaurantMenu.Tests/' --exclude='tools/' --exclude='deploy/' --exclude='.env' \
    "$REPO/" "$CONTEXT/"

echo "==> Building $IMAGE for $PLATFORM ($revision)"
docker buildx build --platform "$PLATFORM" \
    --label "org.opencontainers.image.revision=$revision" \
    -t "$IMAGE" --load "$CONTEXT"

echo "==> Shipping to $HOST"
docker save "$IMAGE" | gzip | ssh "$HOST" 'gunzip | docker load'

if $restart; then
    echo "==> Restarting mqm on $HOST"
    ssh "$HOST" 'cd /srv && docker compose up -d mqm && docker image prune -f'
fi
echo "==> Done ($revision)"
