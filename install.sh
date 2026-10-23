#!/usr/bin/env sh
# Build fleet from source and install it. The Linux and macOS counterpart of
# install.ps1; for a machine without the .NET SDK use scripts/get-fleet.sh.
#
#   ./install.sh                build, install, run 'fleet setup'
#   ./install.sh --with-deps    install missing git, neovim and yazi first, without asking
#   ./install.sh --deps-only    install only those dependencies
#   ./install.sh --uninstall    run 'fleet uninstall' (--yes skips its question); without
#                               the binary, remove only the PATH line (configuration is kept)
#   ./install.sh --no-path      don't add the bin folder to PATH in a shell startup file
#
# When the bin folder isn't on PATH, a marked line adding it goes into the startup
# file of $SHELL: ~/.bashrc (at the top, ahead of the non-interactive guard, so
# 'ssh host fleet bridge' finds it), ~/.zshenv, fish's conf.d or ~/.profile.
# FLEET_NO_PATH=1 does the same as --no-path.
#
# Without --with-deps a missing git, nvim or yazi is listed; on a terminal it asks
# y/N before installing (CI or FLEET_NO_DEPS_PROMPT=1 only print the command).
# Package managers: brew (macOS), pacman, dnf, apt-get (no yazi there).
# --with-deps uses the package manager it can find. fleet's own neovim config ships
# inside the binary and 'fleet setup' writes it to ~/.config/fleet-nvim; set
# FLEET_NVIM_CONFIG to a git URL to also clone a config into ~/.config/nvim
# (for the 'user' nvim config setting).

set -eu

WITH_DEPS=0
DEPS_ONLY=0
NO_PATH=0
UNINSTALL=0
YES=0
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
        --uninstall) UNINSTALL=1 ;;
        --yes | -y) YES=1 ;;
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
    DEPS_RECORD=
    for tool in $DEPS_MISSING; do
        case "$tool" in
            nvim) pkg=neovim ;;
            yazi) [ "$DEPS_PM" = apt-get ] && continue; pkg=yazi ;;
            *) pkg="$tool" ;;
        esac
        pkgs="$pkgs $pkg"
        DEPS_RECORD="$DEPS_RECORD $tool $pkg"
    done
    DEPS_SUDO=false
    if [ -n "$sudo" ] && [ "$DEPS_PM" != brew ]; then
        DEPS_SUDO=true
    fi
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

deps_config_home() {
    if [ -n "${FLEET_CONFIG_HOME:-}" ]; then
        printf '%s\n' "$FLEET_CONFIG_HOME"
    else
        printf '%s\n' "${XDG_CONFIG_HOME:-$HOME/.config}/fleet"
    fi
}

# Records the tools just installed in installed-deps.json, which 'fleet uninstall'
# reads. One entry per tool: a newer one replaces it, entries for other tools stay.
# Plain sed and tr, no jq; a file it cannot read is left alone. Never fatal.
deps_record() {
    [ -n "$DEPS_RECORD" ] || return 0
    m_file="$(deps_config_home)/installed-deps.json"
    if ! (deps_write_manifest "$m_file") 2>/dev/null; then
        ok "could not record the installed tools in $m_file"
    fi
}

deps_write_manifest() {
    m_file="$1"
    m_at="$(date -u +%Y-%m-%dT%H:%M:%SZ)" || return 1
    m_manager="$DEPS_PM"
    [ "$m_manager" != apt-get ] || m_manager=apt
    m_entries=
    m_tools=' '
    # shellcheck disable=SC2086
    set -- $DEPS_RECORD
    while [ $# -ge 2 ]; do
        m_entries="$m_entries${m_entries:+,}{\"tool\":\"$1\",\"manager\":\"$m_manager\",\"package\":\"$2\",\"sudo\":$DEPS_SUDO,\"installedAt\":\"$m_at\"}"
        m_tools="$m_tools$1 "
        shift 2
    done
    m_kept=
    if [ -e "$m_file" ]; then
        m_flat="$(tr -d '\r\n' <"$m_file")" || return 1
        case "$m_flat" in
            *'"deps"'*'['*']'*) ;;
            *) return 1 ;;
        esac
        m_objs="$(printf '%s\n' "$m_flat" |
            sed -e 's/^.*"deps"[[:space:]]*:[[:space:]]*\[//' -e 's/\][^]]*$//' |
            tr '}' '\n' | sed -e 's/^[[:space:],]*//')" || return 1
        while read -r m_obj; do
            case "$m_obj" in
                '{'*) ;;
                *) continue ;;
            esac
            m_tool="$(printf '%s\n' "$m_obj" | sed -n 's/.*"tool"[[:space:]]*:[[:space:]]*"\([^"]*\)".*/\1/p')"
            case "$m_tools" in
                *" $m_tool "*) continue ;;
            esac
            m_kept="$m_kept${m_kept:+,}$m_obj}"
        done <<M_OBJS
$m_objs
M_OBJS
    fi
    mkdir -p "$(dirname -- "$m_file")" || return 1
    printf '{"version":1,"deps":[%s]}\n' "$m_kept${m_kept:+,}$m_entries" >"$m_file.tmp" || return 1
    mv -f "$m_file.tmp" "$m_file"
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
                deps_record
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

install_deps() {
    step 'Installing dependencies'

    check_deps

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

if [ -n "${FLEET_INSTALLER_LIB:-}" ]; then
    return 0
fi

if [ "$UNINSTALL" = 1 ] && [ -x "$BIN" ]; then
    if [ "$YES" = 1 ]; then
        exec "$BIN" uninstall --yes
    fi
    exec "$BIN" uninstall
fi

if [ "$UNINSTALL" = 1 ]; then
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

if [ "$WITH_DEPS" = 0 ]; then
    check_deps
fi

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
