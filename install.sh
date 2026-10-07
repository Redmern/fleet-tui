#!/usr/bin/env sh
# Build fleet from source and install it. The Linux and macOS counterpart of
# install.ps1; for a machine without the .NET SDK use scripts/get-fleet.sh.
#
#   ./install.sh                build, install, run 'fleet setup'
#   ./install.sh --with-deps    install wezterm, neovim and yazi first
#   ./install.sh --deps-only    install only those dependencies
#   ./install.sh --uninstall    remove the binary and the PATH line (configuration is kept)
#   ./install.sh --no-path      don't add the bin folder to PATH in a shell startup file
#
# When the bin folder isn't on PATH, a marked line adding it goes into the startup
# file of $SHELL: ~/.bashrc (at the top, ahead of the non-interactive guard, so
# 'ssh host fleet bridge' finds it), ~/.zshenv, fish's conf.d or ~/.profile.
# FLEET_NO_PATH=1 does the same as --no-path.
#
# --with-deps uses the package manager it can find. fleet's own neovim config ships
# inside the binary and 'fleet setup' writes it to ~/.config/fleet-nvim; set
# FLEET_NVIM_CONFIG to a git URL to also clone a config into ~/.config/nvim
# (for the 'user' nvim config setting).

set -eu

WITH_DEPS=0
DEPS_ONLY=0
NO_PATH=0
if [ -n "${FLEET_NO_PATH:-}" ] && [ "$FLEET_NO_PATH" != 0 ]; then
    NO_PATH=1
fi
PATH_MARK='# added by fleet installer'

REPO_ROOT="$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)"
BIN_DIR="${FLEET_BIN_DIR:-$HOME/.local/bin}"
BIN="$BIN_DIR/fleet"

# Progress bar and spinner only on an interactive terminal; anywhere else
# (redirected, CI, TERM=dumb, NO_COLOR, FLEET_NO_ANIMATION) the plain
# '==> step' lines are printed.
FANCY=0
if [ -t 1 ] && [ "${TERM:-dumb}" != dumb ] && [ -z "${NO_COLOR:-}" ] &&
    [ -z "${CI:-}" ] && [ -z "${FLEET_NO_ANIMATION:-}" ]; then
    FANCY=1
fi
STEP=0
STEPS=4
SPIN_PID=
SPIN_LOG=

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

# Clears the spinner line and gives the cursor back, also on Ctrl+C.
spin_cleanup() {
    if [ -n "$SPIN_PID" ]; then
        kill "$SPIN_PID" 2>/dev/null || true
        SPIN_PID=
        printf '\r\033[K\033[?25h'
    fi
    if [ -n "$SPIN_LOG" ]; then
        rm -f "$SPIN_LOG"
    fi
}
trap spin_cleanup EXIT
trap 'spin_cleanup; exit 130' INT
trap 'spin_cleanup; exit 143' TERM

# Runs a command with its output in a log (printed when it fails) and a spinner
# while it runs. Returns the command's exit code.
with_spinner() {
    label="$1"
    shift
    log="$(mktemp)"
    SPIN_LOG="$log"
    "$@" >"$log" 2>&1 &
    SPIN_PID=$!
    printf '\033[?25l'
    i=0
    while kill -0 "$SPIN_PID" 2>/dev/null; do
        case $((i % 4)) in
            0) c='|' ;;
            1) c='/' ;;
            2) c='-' ;;
            *) c='\' ;;
        esac
        printf '\r    %s %s' "$c" "$label"
        i=$((i + 1))
        sleep 0.2 2>/dev/null || sleep 1
    done
    rc=0
    wait "$SPIN_PID" || rc=$?
    SPIN_PID=
    printf '\r\033[K\033[?25h'
    if [ "$rc" != 0 ]; then
        cat "$log" >&2
    fi
    rm -f "$log"
    SPIN_LOG=
    return "$rc"
}

for arg in "$@"; do
    case "$arg" in
        --with-deps) WITH_DEPS=1 ;;
        --deps-only) DEPS_ONLY=1; WITH_DEPS=1 ;;
        --no-path) NO_PATH=1 ;;
    esac
done

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

remove_from_path() {
    for rc in "$HOME/.bashrc" "${ZDOTDIR:-$HOME}/.zshenv" "$HOME/.profile" \
        "${XDG_CONFIG_HOME:-$HOME/.config}/fish/conf.d/fleet.fish"; do
        if [ ! -f "$rc" ] || ! grep -qF "$PATH_MARK" "$rc"; then
            continue
        fi
        tmp="$(mktemp)"
        grep -vF "$PATH_MARK" "$rc" >"$tmp" || true
        if [ "${rc##*/}" = fleet.fish ] && [ ! -s "$tmp" ]; then
            rm -f "$rc"
        else
            cat "$tmp" >"$rc"
        fi
        rm -f "$tmp"
        ok "removed the PATH line from $rc"
    done
}

install_deps() {
    step 'Installing dependencies'

    if command -v pacman >/dev/null 2>&1; then
        sudo pacman -S --needed --noconfirm git neovim wezterm yazi || ok 'pacman could not install everything'
    elif command -v dnf >/dev/null 2>&1; then
        sudo dnf install -y git neovim yazi || ok 'dnf could not install everything'
        command -v wezterm >/dev/null 2>&1 || ok 'wezterm: see https://wezterm.org/installation'
    elif command -v apt-get >/dev/null 2>&1; then
        sudo apt-get update
        sudo apt-get install -y git neovim || ok 'apt could not install everything'
        # yazi is not packaged for Debian or Ubuntu either.
        command -v yazi >/dev/null 2>&1 || ok 'yazi: see https://yazi-rs.github.io/docs/installation'
        # Debian and Ubuntu carry no wezterm package; it ships its own .deb.
        command -v wezterm >/dev/null 2>&1 || ok 'wezterm: see https://wezterm.org/installation'
    else
        ok 'no known package manager - install git, neovim, wezterm and yazi yourself'
    fi

    url="${FLEET_NVIM_CONFIG:-}"
    target="${XDG_CONFIG_HOME:-$HOME/.config}/nvim"

    step 'Neovim config'

    if [ -z "$url" ]; then
        ok "fleet's own config ships with fleet; 'fleet setup' writes it to ${XDG_CONFIG_HOME:-$HOME/.config}/fleet-nvim"
    elif [ -d "$target/.git" ]; then
        remote="$(git -C "$target" remote get-url origin 2>/dev/null || true)"

        if [ "$remote" = "$url" ]; then
            git -C "$target" pull --ff-only >/dev/null
            ok "updated $target"
        else
            ok "$target already holds a different config ($remote) - left alone"
        fi
    elif [ -d "$target" ] && [ -n "$(ls -A "$target" 2>/dev/null)" ]; then
        ok "$target exists and is not a git checkout - left alone"
    else
        git clone --depth 1 "$url" "$target" && ok "cloned $url into $target"
    fi

    if [ -f "$target/bootstrap.sh" ]; then
        ok "this config ships bootstrap.sh for extra tooling - run it yourself if you want it"
    fi
}

if [ "${1:-}" = "--uninstall" ]; then
    STEPS=1
    step 'Uninstalling'
    rm -f "$BIN"
    ok "removed $BIN"
    remove_from_path
    ok "configuration kept in ${XDG_CONFIG_HOME:-$HOME/.config}/fleet"
    steps_done
    exit 0
fi

if [ "$DEPS_ONLY" = 1 ]; then
    STEPS=2
elif [ "$WITH_DEPS" = 1 ]; then
    STEPS=6
fi

if [ "$WITH_DEPS" = 1 ]; then
    install_deps
fi

if [ "$DEPS_ONLY" = 1 ]; then
    steps_done
    exit 0
fi

step 'Checking prerequisites'

if ! command -v dotnet >/dev/null 2>&1; then
    echo "fleet: the .NET SDK is required to build from source." >&2
    echo "       install it, or use scripts/get-fleet.sh for a published binary." >&2
    exit 1
fi

ok "dotnet $(dotnet --version)"

# NativeAOT links with clang and needs zlib headers. Say so before the linker does.
for tool in clang; do
    command -v "$tool" >/dev/null 2>&1 || ok "missing $tool - NativeAOT needs it (apt install clang zlib1g-dev)"
done

case "$(uname -s)" in
    Darwin)
        case "$(uname -m)" in
            arm64) RID=osx-arm64 ;;
            *) RID=osx-x64 ;;
        esac
        ;;
    *)
        case "$(uname -m)" in
            aarch64|arm64) RID=linux-arm64 ;;
            *) RID=linux-x64 ;;
        esac
        ;;
esac

ok "target $RID"

step 'Publishing (NativeAOT)'
OUT="$REPO_ROOT/out/$RID"
if [ "$FANCY" = 1 ]; then
    with_spinner 'dotnet publish' dotnet publish "$REPO_ROOT/src/Fleet/Fleet.csproj" -c Release -r "$RID" -o "$OUT" --nologo
else
    dotnet publish "$REPO_ROOT/src/Fleet/Fleet.csproj" -c Release -r "$RID" -o "$OUT" --nologo
fi
ok "published to $OUT"

step 'Installing'
mkdir -p "$BIN_DIR"
pkill -x fleet 2>/dev/null && ok 'stopped a running fleet' || true
install -m 755 "$OUT/fleet" "$BIN"
ok "$BIN"

add_to_path

step 'Setting up'
"$BIN" setup || true

steps_done
echo
echo "fleet installed. Run 'fleet' to open a project."
