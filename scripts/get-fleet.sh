#!/usr/bin/env sh
# Install fleet on Linux from a published release. No .NET SDK needed.
#
# Defaults to the upstream release repository. Override it with FLEET_REPO, or
# pass it as the first argument, if you're running this from a fork.
#
#   sh get-fleet.sh
#   sh get-fleet.sh --with-deps
#   curl -fsSL https://raw.githubusercontent.com/Redmern/fleet-tui/main/scripts/get-fleet.sh | sh
#   FLEET_REPO=<owner>/fleet sh get-fleet.sh

set -eu

DEFAULT_REPO="Redmern/fleet-tui"

WITH_DEPS=0
for arg in "$@"; do
    case "$arg" in
        --with-deps) WITH_DEPS=1 ;;
        *) [ -z "${REPO_ARG:-}" ] && REPO_ARG="$arg" ;;
    esac
done

# Progress bar and curl's download meter only on an interactive terminal; anywhere
# else (redirected, CI, TERM=dumb, NO_COLOR, FLEET_NO_ANIMATION) the plain
# '==> step' lines are printed. Inlined so 'curl ... | sh' keeps working.
FANCY=0
if [ -t 1 ] && [ "${TERM:-dumb}" != dumb ] && [ -z "${NO_COLOR:-}" ] &&
    [ -z "${CI:-}" ] && [ -z "${FLEET_NO_ANIMATION:-}" ]; then
    FANCY=1
fi
STEP=0
STEPS=3
if [ "$WITH_DEPS" = 1 ]; then
    STEPS=4
fi

bar() {
    filled=$(($1 * 24 / $2))
    out='['
    n=0
    while [ "$n" -lt 24 ]; do
        if [ "$n" -lt "$filled" ]; then out="$out#"; else out="$out-"; fi
        n=$((n + 1))
    done
    printf '%s]' "$out"
}

step() {
    STEP=$((STEP + 1))
    if [ "$FANCY" = 1 ]; then
        printf '%s %s/%s %s\n' "$(bar $((STEP - 1)) "$STEPS")" "$STEP" "$STEPS" "$1"
    else
        printf '==> %s\n' "$1"
    fi
}

steps_done() {
    if [ "$FANCY" = 1 ]; then
        printf '%s %s/%s Done\n' "$(bar 1 1)" "$STEPS" "$STEPS"
    fi
}

REPO="${REPO_ARG:-${FLEET_REPO:-$DEFAULT_REPO}}"
VERSION="${FLEET_VERSION:-latest}"
BIN_DIR="${FLEET_BIN_DIR:-$HOME/.local/bin}"

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
    step 'Fetching the dependency installer'
    DEPS="$(mktemp)"
    curl -fsSL "https://raw.githubusercontent.com/$REPO/main/install.sh" -o "$DEPS"
    # install.sh carries the dependency logic; --deps-only stops before building.
    sh "$DEPS" --with-deps --deps-only || true
    rm -f "$DEPS"
fi

step "Downloading $ASSET"
mkdir -p "$BIN_DIR"
TMP="$(mktemp)"
if [ "$FANCY" = 1 ]; then
    curl -fL -# "$URL" -o "$TMP"
else
    curl -fsSL "$URL" -o "$TMP"
fi
echo "    $URL"

step 'Installing'
chmod +x "$TMP"
mv "$TMP" "$BIN_DIR/fleet"
echo "    $BIN_DIR/fleet"

case ":$PATH:" in
    *":$BIN_DIR:"*) echo "    already on PATH: $BIN_DIR" ;;
    *) echo "    add to PATH: export PATH=\"$BIN_DIR:\$PATH\"" ;;
esac

step 'Setting up'
"$BIN_DIR/fleet" setup || true

steps_done
echo
echo "fleet installed. Run 'fleet' to open a project."
