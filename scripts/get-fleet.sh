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
#
# When the bin folder isn't on PATH, a marked line adding it goes into the startup
# file of $SHELL (see install.sh). --no-path or FLEET_NO_PATH=1 only prints the hint;
# ./install.sh --uninstall removes the line again.

set -eu

DEFAULT_REPO="Redmern/fleet-tui"

WITH_DEPS=0
NO_PATH=0
if [ -n "${FLEET_NO_PATH:-}" ] && [ "$FLEET_NO_PATH" != 0 ]; then
    NO_PATH=1
fi
PATH_MARK='# added by fleet installer'
for arg in "$@"; do
    case "$arg" in
        --with-deps) WITH_DEPS=1 ;;
        --no-path) NO_PATH=1 ;;
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

ok() { printf '    %s\n' "$1"; }

path_rc_file() {
    case "${SHELL:-}" in
        */bash) echo "$HOME/.bashrc" ;;
        */zsh) echo "${ZDOTDIR:-$HOME}/.zshenv" ;;
        */fish) echo "${XDG_CONFIG_HOME:-$HOME/.config}/fish/conf.d/fleet.fish" ;;
        *) echo "$HOME/.profile" ;;
    esac
}

add_to_path() {
    case ":$PATH:" in
        *":$BIN_DIR:"*) ok "already on PATH: $BIN_DIR"; return ;;
    esac
    if [ "$NO_PATH" = 1 ]; then
        ok "add to PATH: export PATH=\"$BIN_DIR:\$PATH\""
        return
    fi
    rc="$(path_rc_file)"
    if [ -f "$rc" ] && grep -qF "$PATH_MARK" "$rc"; then
        ok "PATH is already set in $rc - open a new shell, or run: . $rc"
        return
    fi
    case "$rc" in
        *.fish) line="contains -- '$BIN_DIR' \$PATH; or set -gx PATH '$BIN_DIR' \$PATH $PATH_MARK" ;;
        *) line="case \":\$PATH:\" in *\":$BIN_DIR:\"*) ;; *) export PATH=\"$BIN_DIR:\$PATH\" ;; esac $PATH_MARK" ;;
    esac
    mkdir -p "$(dirname -- "$rc")"
    if [ "${rc##*/}" = .bashrc ] && [ -s "$rc" ]; then
        tmp="$(mktemp)"
        { printf '%s\n' "$line"; cat "$rc"; } >"$tmp"
        cat "$tmp" >"$rc"
        rm -f "$tmp"
    else
        if [ -s "$rc" ] && [ -n "$(tail -c 1 "$rc")" ]; then
            printf '\n' >>"$rc"
        fi
        printf '%s\n' "$line" >>"$rc"
    fi
    ok "added $BIN_DIR to PATH in $rc"
    ok "open a new shell, or run: . $rc"
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

add_to_path

step 'Setting up'
"$BIN_DIR/fleet" setup || true

steps_done
echo
echo "fleet installed. Run 'fleet' to open a project."
