#!/usr/bin/env bash
# ==============================================================================
# Fleptix Observer — Automated Installation & Container Replacement Script
# ==============================================================================
set -euo pipefail

IMAGE="ghcr.io/fleptor/fleptix:latest"
CONTAINER_NAME="fleptix-observer"
PORT="${FLEPTIX_PORT:-7373}"

echo ""
echo "  ███████╗██╗     ███████╗██████╗ ████████╗██╗██╗  ██╗"
echo "  ██╔════╝██║     ██╔════╝██╔══██╗╚══██╔══╝██║╚██╗██╔╝"
echo "  █████╗  ██║     █████╗  ██████╔╝   ██║   ██║ ╚███╔╝ "
echo "  ██╔══╝  ██║     ██╔══╝  ██╔═══╝    ██║   ██║ ██╔██╗ "
echo "  ██║     ███████╗███████╗██║        ██║   ██║██╔╝ ██╗"
echo "  ╚═╝     ╚══════╝╚══════╝╚═╝        ╚═╝   ╚═╝╚═╝  ╚═╝"
echo "           Lightweight Docker Infrastructure Observer"
echo "=================================================================="
echo ""

# 1. Check Docker installation
if ! command -v docker >/dev/null 2>&1; then
    echo "[ERROR] Docker CLI was not found on your system."
    echo "Please install Docker first: https://docs.docker.com/engine/install/"
    exit 1
fi

# 2. Check Docker daemon connectivity
if ! docker info >/dev/null 2>&1; then
    echo "[ERROR] Cannot connect to Docker daemon via /var/run/docker.sock."
    echo "Please ensure Docker is running and your user has permission to access the socket."
    exit 1
fi

echo "==> Pulling latest Fleptix Observer image ($IMAGE)..."
docker pull "$IMAGE"

# 3. Clean container switching: Stop and remove previous container if it exists
if docker ps -a --format '{{.Names}}' | grep -Eq "^${CONTAINER_NAME}\$"; then
    echo "==> Detected existing '$CONTAINER_NAME' container."
    echo "==> Gracefully removing old container to apply updates..."
    docker rm -f "$CONTAINER_NAME" >/dev/null 2>&1 || true
fi

# 4. Run new container with persistent volume for snapshots & license persistence
echo "==> Starting Fleptix Observer on port $PORT..."
docker run -d \
  --name "$CONTAINER_NAME" \
  -p "${PORT}:80" \
  -v /var/run/docker.sock:/var/run/docker.sock \
  -v fleptix-data:/app/snapshots \
  --restart unless-stopped \
  "$IMAGE"

echo ""
echo "=================================================================="
echo "  [SUCCESS] Fleptix Observer is active and monitoring!"
echo "  Dashboard: http://localhost:${PORT}"
echo "=================================================================="
echo ""
