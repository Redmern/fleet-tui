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
# A missing git, nvim or yazi is listed; on a terminal it asks y/N (reading the
# terminal, so it works under 'curl | sh'), --with-deps installs without asking,
# and CI or FLEET_NO_DEPS_PROMPT=1 only print the install command.
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
STEPS=4

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

# git, nvim and yazi: list the missing ones, then install them (--with-deps), ask
# first (a terminal, no CI or FLEET_NO_DEPS_PROMPT) or print the command. Never fatal.
deps_missing() {
    DEPS_MISSING=
    for tool in git nvim yazi; do
        command -v "$tool" >/dev/null 2>&1 || DEPS_MISSING="$DEPS_MISSING $tool"
    done
    DEPS_MISSING="${DEPS_MISSING# }"
}

deps_manager() {
    if [ "$(uname -s 2>/dev/null || true)" = Darwin ] && command -v brew >/dev/null 2>&1; then
        echo brew
    elif command -v pacman >/dev/null 2>&1; then
        echo pacman
    elif command -v dnf >/dev/null 2>&1; then
        echo dnf
    elif command -v apt-get >/dev/null 2>&1; then
        echo apt-get
    fi
}

deps_plan() {
    DEPS_PM="$(deps_manager)"
    DEPS_CMD=
    DEPS_HINT=
    sudo='sudo '
    if [ "$(id -u 2>/dev/null || echo 1)" = 0 ]; then
        sudo=
    fi
    pkgs=
    for tool in $DEPS_MISSING; do
        case "$tool" in
            nvim) pkgs="$pkgs neovim" ;;
            yazi) [ "$DEPS_PM" = apt-get ] || pkgs="$pkgs yazi" ;;
            *) pkgs="$pkgs $tool" ;;
        esac
    done
    case "$DEPS_PM" in
        brew) DEPS_CMD="brew install$pkgs" ;;
        pacman) DEPS_CMD="${sudo}pacman -S --needed --noconfirm$pkgs" ;;
        dnf) DEPS_CMD="${sudo}dnf install -y$pkgs" ;;
        apt-get)
            if [ -n "$pkgs" ]; then
                DEPS_CMD="${sudo}apt-get update && ${sudo}apt-get install -y$pkgs"
            fi
            case " $DEPS_MISSING " in
                *' yazi '*) DEPS_HINT='yazi is not packaged for apt: see https://yazi-rs.github.io/docs/installation' ;;
            esac
            ;;
        *)
            DEPS_HINT='no known package manager - install them yourself: git https://git-scm.com/downloads, neovim https://neovim.io, yazi https://yazi-rs.github.io/docs/installation'
            ;;
    esac
}

deps_interactive() {
    [ -t 1 ] && [ -z "${CI:-}" ] && [ -z "${FLEET_NO_DEPS_PROMPT:-}" ] &&
        (: </dev/tty) 2>/dev/null
}

deps_ask() {
    printf '    install them with %s? [y/N] ' "$DEPS_PM"
    answer=
    read -r answer </dev/tty || answer=
    case "$answer" in
        [yY] | [yY][eE][sS]) return 0 ;;
    esac
    return 1
}

check_deps() {
    deps_missing
    if [ -z "$DEPS_MISSING" ]; then
        return 0
    fi
    list=
    for tool in $DEPS_MISSING; do
        list="${list:+$list, }$tool"
    done
    ok "missing: $list"
    deps_plan
    if [ -n "$DEPS_CMD" ]; then
        if [ "$WITH_DEPS" = 1 ] || { deps_interactive && deps_ask; }; then
            ok "running: $DEPS_CMD"
            deps_stdin=/dev/null
            if (: </dev/tty) 2>/dev/null; then
                deps_stdin=/dev/tty
            fi
            if eval "$DEPS_CMD" <"$deps_stdin"; then
                ok "installed with $DEPS_PM"
            else
                ok "$DEPS_PM could not install everything - run it yourself: $DEPS_CMD"
            fi
        else
            ok "install them with: $DEPS_CMD"
        fi
    fi
    if [ -n "$DEPS_HINT" ]; then
        ok "$DEPS_HINT"
    fi
}

if [ -n "${FLEET_INSTALLER_LIB:-}" ]; then
    return 0
fi

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
else
    step 'Checking dependencies'
    check_deps
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
