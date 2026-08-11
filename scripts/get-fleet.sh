#!/usr/bin/env sh
# Install fleet on Linux from a published release. No .NET SDK needed.
#
# The release repository is not baked in: set FLEET_REPO, or pass it as the first
# argument.
#
#   FLEET_REPO=trivium/fleet sh get-fleet.sh
#   FLEET_REPO=trivium/fleet sh get-fleet.sh --with-deps
#   curl -fsSL https://raw.githubusercontent.com/trivium/fleet/main/scripts/get-fleet.sh | FLEET_REPO=trivium/fleet sh

set -eu

WITH_DEPS=0
for arg in "$@"; do
    case "$arg" in
        --with-deps) WITH_DEPS=1 ;;
        *) [ -z "${REPO_ARG:-}" ] && REPO_ARG="$arg" ;;
    esac
done

REPO="${REPO_ARG:-${FLEET_REPO:-}}"
VERSION="${FLEET_VERSION:-latest}"
BIN_DIR="${FLEET_BIN_DIR:-$HOME/.local/bin}"

if [ -z "$REPO" ]; then
    echo "fleet: no release repository. Set FLEET_REPO=<owner/name> or pass it as \$1." >&2
    exit 2
fi

case "$(uname -m)" in
    x86_64|amd64) ASSET=fleet-linux-x64 ;;
    *)
        echo "fleet: no published binary for $(uname -m). Build from source with ./install.sh." >&2
        exit 1
        ;;
esac

if [ "$VERSION" = latest ]; then
    URL="https://github.com/$REPO/releases/latest/download/$ASSET"
else
    URL="https://github.com/$REPO/releases/download/$VERSION/$ASSET"
fi

if [ "$WITH_DEPS" = 1 ]; then
    echo "==> Fetching the dependency installer"
    DEPS="$(mktemp)"
    curl -fsSL "https://raw.githubusercontent.com/$REPO/main/install.sh" -o "$DEPS"
    # install.sh carries the dependency logic; --deps-only stops before building.
    sh "$DEPS" --with-deps --deps-only || true
    rm -f "$DEPS"
fi

echo "==> Downloading $ASSET"
mkdir -p "$BIN_DIR"
TMP="$(mktemp)"
curl -fsSL "$URL" -o "$TMP"
echo "    $URL"

echo "==> Installing"
chmod +x "$TMP"
mv "$TMP" "$BIN_DIR/fleet"
echo "    $BIN_DIR/fleet"

case ":$PATH:" in
    *":$BIN_DIR:"*) echo "    already on PATH: $BIN_DIR" ;;
    *) echo "    add to PATH: export PATH=\"$BIN_DIR:\$PATH\"" ;;
esac

echo "==> Setting up"
"$BIN_DIR/fleet" setup || true

echo
echo "fleet installed. Run 'fleet' to open a project."
