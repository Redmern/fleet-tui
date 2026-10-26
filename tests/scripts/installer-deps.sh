#!/usr/bin/env sh
# shellcheck disable=SC1007,SC1090,SC2016,SC2034
# Tests the git/nvim/yazi detect-and-offer step of install.sh and
# scripts/get-fleet.sh. Each case sources the installer with
# FLEET_INSTALLER_LIB=1 (functions only, no install), points PATH at a folder of
# fake tools that log their calls, and runs check_deps.
#
#   sh tests/scripts/installer-deps.sh
#
# The interactive cases run under util-linux 'script' so stdout and /dev/tty are
# a real pty; they are skipped when 'script' is missing.

set -u

ROOT="$(CDPATH= cd -- "$(dirname -- "$0")/../.." && pwd)"
WORK="$(mktemp -d)"
trap 'rm -rf "$WORK"' EXIT

PASS=0
FAIL=0
REAL_SH="$(command -v sh)"

fail() {
    FAIL=$((FAIL + 1))
    printf 'FAIL %s: %s\n' "$CASE" "$1"
    printf '%s\n' '--- output' "$(cat "$OUT")" '--- calls' "$(cat "$LOG" 2>/dev/null)" '---'
}

pass() {
    PASS=$((PASS + 1))
    printf 'ok   %s\n' "$CASE"
}

# Fake bin folder holding exactly the named tools. Package managers and sudo log
# "name args" to $LOG; a manager exits 1 when FAKE_FAIL is set.
make_bin() {
    FAKEBIN="$WORK/bin.$$.$RANDOM_ID"
    RANDOM_ID=$((RANDOM_ID + 1))
    mkdir -p "$FAKEBIN"
    printf '#!%s\nif [ "${1:-}" = -m ]; then printf "x86_64\\n"; else printf "%%s\\n" "${FAKE_UNAME:-Linux}"; fi\n' "$REAL_SH" >"$FAKEBIN/uname"
    printf '#!%s\nprintf "1000\\n"\n' "$REAL_SH" >"$FAKEBIN/id"
    for tool in "$@"; do
        case "$tool" in
            git | nvim | yazi | code)
                printf '#!%s\nexit 0\n' "$REAL_SH" >"$FAKEBIN/$tool"
                ;;
            sudo)
                printf '#!%s\nprintf "sudo %%s\\n" "$*" >>"$LOG"\nexec "$@"\n' "$REAL_SH" >"$FAKEBIN/sudo"
                ;;
            *)
                printf '#!%s\nprintf "%s %%s\\n" "$*" >>"$LOG"\n[ -z "${FAKE_READ_STDIN:-}" ] || while read -r _; do :; done\n[ -z "${FAKE_FAIL:-}" ]\n' "$REAL_SH" "$tool" >"$FAKEBIN/$tool"
                ;;
        esac
    done
    chmod +x "$FAKEBIN"/*
}

RANDOM_ID=0

# run_deps <script> <with_deps> -> output in $OUT, calls in $LOG, status in $RC.
run_deps() {
    OUT="$WORK/out"
    LOG="$WORK/log"
    : >"$LOG"
    export LOG
    (
        FLEET_INSTALLER_LIB=1
        . "$1"
        WITH_DEPS="$2"
        PATH="$FAKEBIN"
        check_deps
        printf 'after check_deps\n'
    ) >"$OUT" 2>&1 </dev/null
    RC=$?
}

# run_tty <script> <answer> -> like run_deps with WITH_DEPS=0, on a pty, typing <answer>.
run_tty() {
    OUT="$WORK/out"
    LOG="$WORK/log"
    : >"$LOG"
    export LOG FAKEBIN
    SCRIPT_UNDER_TEST="$1"
    export SCRIPT_UNDER_TEST
    printf '%s\n' "$2" | env -u CI -u FLEET_NO_DEPS_PROMPT script -qec \
        "$REAL_SH -c 'FLEET_INSTALLER_LIB=1; . \"\$SCRIPT_UNDER_TEST\"; WITH_DEPS=0; PATH=\"\$FAKEBIN\"; check_deps; printf \"after check_deps\\n\"'" \
        /dev/null >"$OUT" 2>&1
    RC=$?
}

# The tools the manifest writer needs, next to the fakes.
with_manifest_utils() {
    for util in date mkdir dirname tr sed mv cat rm; do
        ln -sf "$(command -v "$util")" "$FAKEBIN/$util"
    done
}

# run_manifest <script> <with_deps> [keep] [config home] -> like run_deps with
# FLEET_CONFIG_HOME set; the manifest is $MANIFEST. Without keep it starts empty.
run_manifest() {
    CFG="${4:-$WORK/cfg}"
    MANIFEST="$CFG/installed-deps.json"
    [ "${3:-}" = keep ] || rm -rf "$WORK/cfg"
    FLEET_CONFIG_HOME="$CFG"
    export FLEET_CONFIG_HOME
    run_deps "$1" "$2"
    unset FLEET_CONFIG_HOME
}

manifest_has() { [ -f "$MANIFEST" ] && grep -qF -- "$1" "$MANIFEST"; }

manifest_valid() {
    [ -f "$MANIFEST" ] || return 1
    if command -v python3 >/dev/null 2>&1; then
        python3 -c 'import json,sys; d=json.load(open(sys.argv[1])); assert d["version"]==1 and isinstance(d["deps"],list)' "$MANIFEST"
    fi
}

has() { grep -qF -- "$1" "$OUT"; }
called() { grep -qF -- "$1" "$LOG"; }
no_calls() { [ ! -s "$LOG" ]; }

for SCRIPT in "$ROOT/install.sh" "$ROOT/scripts/get-fleet.sh"; do
    NAME="${SCRIPT#"$ROOT"/}"

    CASE="$NAME: all present is silent and installs nothing (AC-1, AC-2)"
    make_bin git nvim yazi pacman sudo
    run_deps "$SCRIPT" 0
    if [ "$RC" != 0 ]; then fail "exit $RC"
    elif [ "$(cat "$OUT")" != 'after check_deps' ]; then fail 'printed something'
    elif ! no_calls; then fail 'ran a package manager'
    else pass; fi

    CASE="$NAME: all present with --with-deps installs nothing (AC-2, AC-6)"
    run_deps "$SCRIPT" 1
    if [ "$(cat "$OUT")" != 'after check_deps' ] || ! no_calls; then fail 'not silent'; else pass; fi

    CASE="$NAME: non-interactive prints missing and the command only (AC-1, AC-5)"
    make_bin git pacman sudo
    run_deps "$SCRIPT" 0
    if [ "$RC" != 0 ]; then fail "exit $RC"
    elif ! has 'missing: nvim, yazi'; then fail 'missing list'
    elif ! has 'sudo pacman -S --needed --noconfirm neovim yazi'; then fail 'command'
    elif has '[y/N]'; then fail 'prompted'
    elif ! no_calls; then fail 'installed'
    elif ! has 'after check_deps'; then fail 'did not continue'
    else pass; fi

    CASE="$NAME: --with-deps installs only the missing tools without asking (AC-6)"
    make_bin git nvim pacman sudo
    run_deps "$SCRIPT" 1
    if [ "$RC" != 0 ]; then fail "exit $RC"
    elif ! called 'pacman -S --needed --noconfirm yazi'; then fail 'pacman call'
    elif called 'neovim' || called ' git'; then fail 'installed present tools'
    elif has '[y/N]'; then fail 'prompted'
    else pass; fi

    CASE="$NAME: dnf installs missing with sudo (AC-6)"
    make_bin nvim yazi dnf sudo
    run_deps "$SCRIPT" 1
    if ! called 'sudo dnf install -y git' || ! called 'dnf install -y git'; then fail 'dnf call'; else pass; fi

    CASE="$NAME: brew on macOS, no sudo (AC-7)"
    make_bin git brew pacman sudo
    FAKE_UNAME=Darwin
    export FAKE_UNAME
    run_deps "$SCRIPT" 1
    unset FAKE_UNAME
    if ! called 'brew install neovim yazi'; then fail 'brew call'
    elif called 'sudo' || called 'pacman'; then fail 'used sudo or pacman'
    else pass; fi

    CASE="$NAME: brew is ignored off macOS (AC-7)"
    make_bin git brew pacman sudo
    run_deps "$SCRIPT" 1
    if called 'brew'; then fail 'used brew on Linux'
    elif ! called 'pacman -S --needed --noconfirm neovim yazi'; then fail 'pacman call'
    else pass; fi

    CASE="$NAME: apt installs git and neovim, hints yazi (AC-8)"
    make_bin apt-get sudo
    run_deps "$SCRIPT" 1
    if ! called 'apt-get update'; then fail 'apt update'
    elif ! called 'apt-get install -y git neovim'; then fail 'apt install'
    elif called 'yazi'; then fail 'asked apt for yazi'
    elif ! has 'https://yazi-rs.github.io/docs/installation'; then fail 'yazi hint'
    elif ! has 'after check_deps'; then fail 'did not continue'
    else pass; fi

    CASE="$NAME: apt with only yazi missing hints without a command (AC-5, AC-8)"
    make_bin git nvim apt-get sudo
    run_deps "$SCRIPT" 0
    if ! has 'missing: yazi' || ! has 'yazi-rs.github.io'; then fail 'hint'
    elif has 'apt-get'; then fail 'printed an apt command'
    elif ! no_calls; then fail 'installed'
    else pass; fi

    CASE="$NAME: no package manager prints manual guidance and continues (AC-9)"
    make_bin git
    run_deps "$SCRIPT" 1
    if [ "$RC" != 0 ]; then fail "exit $RC"
    elif ! has 'missing: nvim, yazi'; then fail 'missing list'
    elif ! has 'no known package manager'; then fail 'guidance'
    elif ! has 'neovim.io'; then fail 'neovim link'
    elif ! has 'after check_deps'; then fail 'did not continue'
    else pass; fi

    CASE="$NAME: a failing install warns and continues (AC-10)"
    make_bin pacman sudo
    FAKE_FAIL=1
    export FAKE_FAIL
    run_deps "$SCRIPT" 1
    unset FAKE_FAIL
    if [ "$RC" != 0 ]; then fail "exit $RC"
    elif ! called 'pacman'; then fail 'did not try'
    elif ! has 'could not install'; then fail 'no warning'
    elif ! has 'after check_deps'; then fail 'did not continue'
    else pass; fi

    CASE="$NAME: under 'curl | sh' the package manager cannot read the piped script (AC-11)"
    make_bin git pacman sudo
    : >"$LOG"
    OUT="$WORK/out"
    FAKE_READ_STDIN=1
    export FAKE_READ_STDIN
    printf 'FLEET_INSTALLER_LIB=1\n. "%s"\nWITH_DEPS=1\nPATH="%s"\ncheck_deps\nprintf "after check_deps\\n"\n' "$SCRIPT" "$FAKEBIN" |
        "$REAL_SH" >"$OUT" 2>&1
    unset FAKE_READ_STDIN
    if ! called 'pacman'; then fail 'did not install'
    elif ! has 'after check_deps'; then fail 'the package manager swallowed the rest of the script'
    else pass; fi

    CASE="$NAME: CI env prints the command only (AC-5)"
    make_bin git pacman sudo
    if command -v script >/dev/null 2>&1; then
        : >"$LOG"
        printf 'y\n' | CI=1 script -qec \
            "$REAL_SH -c 'FLEET_INSTALLER_LIB=1; . \"$SCRIPT\"; WITH_DEPS=0; PATH=\"$FAKEBIN\"; check_deps'" \
            /dev/null >"$WORK/out" 2>&1
        OUT="$WORK/out"
        if grep -qF '[y/N]' "$OUT"; then fail 'prompted under CI'
        elif ! no_calls; then fail 'installed'
        elif ! has 'sudo pacman -S --needed --noconfirm neovim yazi'; then fail 'command'
        else pass; fi
    fi

    CASE="$NAME: a real install records the installed tools only (AC-11, AC-12)"
    make_bin git pacman sudo
    with_manifest_utils
    run_manifest "$SCRIPT" 1
    if [ "$RC" != 0 ]; then fail "exit $RC"
    elif ! manifest_valid; then fail 'manifest missing or not JSON'
    elif ! manifest_has '"tool":"nvim","manager":"pacman","package":"neovim","sudo":true,"installedAt":"20'; then fail 'nvim entry'
    elif ! manifest_has '"tool":"yazi","manager":"pacman","package":"yazi","sudo":true'; then fail 'yazi entry'
    elif manifest_has '"tool":"git"'; then fail 'recorded the present git'
    elif ! manifest_has '{"version":1,"deps":['; then fail 'shape'
    elif ! has 'after check_deps'; then fail 'did not continue'
    else pass; fi

    CASE="$NAME: the timestamp is UTC ISO-8601 (AC-12)"
    if ! grep -qE '"installedAt":"[0-9]{4}-[0-9]{2}-[0-9]{2}T[0-9]{2}:[0-9]{2}:[0-9]{2}Z"' "$MANIFEST"; then fail 'installedAt'; else pass; fi

    CASE="$NAME: all present records nothing (AC-11)"
    make_bin git nvim yazi pacman sudo
    with_manifest_utils
    run_manifest "$SCRIPT" 1
    if [ -e "$MANIFEST" ]; then fail 'wrote a manifest'; else pass; fi

    CASE="$NAME: a failed install records nothing (AC-12)"
    make_bin pacman sudo
    with_manifest_utils
    FAKE_FAIL=1
    export FAKE_FAIL
    run_manifest "$SCRIPT" 1
    unset FAKE_FAIL
    if [ -e "$MANIFEST" ]; then fail 'wrote a manifest'
    elif ! has 'after check_deps'; then fail 'did not continue'
    else pass; fi

    CASE="$NAME: only printing the command records nothing (AC-12)"
    make_bin git pacman sudo
    with_manifest_utils
    run_manifest "$SCRIPT" 0
    if [ -e "$MANIFEST" ]; then fail 'wrote a manifest'; else pass; fi

    CASE="$NAME: apt records git and neovim as apt, not the hinted yazi (AC-12)"
    make_bin apt-get sudo
    with_manifest_utils
    run_manifest "$SCRIPT" 1
    if ! manifest_has '"tool":"git","manager":"apt","package":"git","sudo":true'; then fail 'git entry'
    elif ! manifest_has '"tool":"nvim","manager":"apt","package":"neovim"'; then fail 'nvim entry'
    elif manifest_has 'yazi'; then fail 'recorded yazi'
    else pass; fi

    CASE="$NAME: brew records sudo false (AC-12)"
    make_bin git nvim brew sudo
    with_manifest_utils
    FAKE_UNAME=Darwin
    export FAKE_UNAME
    run_manifest "$SCRIPT" 1
    unset FAKE_UNAME
    if ! manifest_has '"tool":"yazi","manager":"brew","package":"yazi","sudo":false'; then fail 'brew entry'; else pass; fi

    CASE="$NAME: existing entries are kept and a reinstalled tool replaced (AC-12)"
    make_bin git pacman sudo
    with_manifest_utils
    mkdir -p "$WORK/cfg"
    printf '%s\n' '{' '  "version": 1,' '  "deps": [' \
        '    { "tool": "git", "manager": "dnf", "package": "git", "sudo": true, "installedAt": "2026-01-01T00:00:00Z" },' \
        '    { "tool": "nvim", "manager": "dnf", "package": "neovim", "sudo": true, "installedAt": "2026-01-01T00:00:00Z" }' \
        '  ]' '}' >"$WORK/cfg/installed-deps.json"
    run_manifest "$SCRIPT" 1 keep
    if ! manifest_valid; then fail 'not JSON'
    elif ! manifest_has '"tool": "git", "manager": "dnf"'; then fail 'dropped the git entry'
    elif [ "$(grep -o '"tool": *"nvim"' "$MANIFEST" | wc -l)" != 1 ]; then fail 'nvim not exactly once'
    elif ! manifest_has '"tool":"nvim","manager":"pacman"'; then fail 'nvim not replaced'
    elif ! manifest_has '"tool":"yazi","manager":"pacman"'; then fail 'yazi entry'
    else pass; fi

    CASE="$NAME: a manifest that cannot be written only warns (AC-12)"
    make_bin git pacman sudo
    with_manifest_utils
    : >"$WORK/notadir"
    run_manifest "$SCRIPT" 1 keep "$WORK/notadir/fleet"
    rm -f "$WORK/notadir"
    if [ "$RC" != 0 ]; then fail "exit $RC"
    elif ! has 'could not record'; then fail 'no warning'
    elif ! has 'after check_deps'; then fail 'did not continue'
    else pass; fi

    CASE="$NAME: a failed move leaves no temp file behind (AC-12)"
    make_bin git pacman sudo
    with_manifest_utils
    rm -f "$FAKEBIN/mv"
    printf '#!%s\nexit 1\n' "$REAL_SH" >"$FAKEBIN/mv"
    chmod +x "$FAKEBIN/mv"
    run_manifest "$SCRIPT" 1
    if [ -e "$MANIFEST" ]; then fail 'wrote a manifest'
    elif [ -e "$MANIFEST.tmp" ]; then fail 'left the temp file'
    elif ! has 'could not record'; then fail 'no warning'
    elif ! has 'after check_deps'; then fail 'did not continue'
    else pass; fi

    CASE="$NAME: an unreadable manifest is left alone and only warns (AC-12)"
    make_bin git pacman sudo
    with_manifest_utils
    mkdir -p "$WORK/cfg"
    printf 'not json\n' >"$WORK/cfg/installed-deps.json"
    run_manifest "$SCRIPT" 1 keep
    if [ "$(cat "$MANIFEST")" != 'not json' ]; then fail 'overwrote it'
    elif ! has 'could not record'; then fail 'no warning'
    else pass; fi

    if command -v script >/dev/null 2>&1; then
        CASE="$NAME: interactive yes installs the missing tools (AC-3)"
        make_bin git code pacman sudo
        run_tty "$SCRIPT" y
        if ! has 'nvim, yazi' || ! has '[y/N]'; then fail 'no prompt'
        elif ! called 'pacman -S --needed --noconfirm neovim yazi'; then fail 'did not install'
        elif ! has 'after check_deps'; then fail 'did not continue'
        else pass; fi

        CASE="$NAME: interactive Enter installs nothing and prints the command (AC-4)"
        make_bin git code pacman sudo
        run_tty "$SCRIPT" ''
        if ! has '[y/N]'; then fail 'no prompt'
        elif ! no_calls; then fail 'installed'
        elif ! has 'sudo pacman -S --needed --noconfirm neovim yazi'; then fail 'command'
        elif ! has 'after check_deps'; then fail 'did not continue'
        else pass; fi

        CASE="$NAME: interactive no installs nothing (AC-4)"
        make_bin git code pacman sudo
        run_tty "$SCRIPT" n
        if ! has '[y/N]'; then fail 'no prompt'
        elif ! no_calls; then fail 'installed'
        else pass; fi

        CASE="$NAME: FLEET_NO_DEPS_PROMPT skips the prompt (AC-3)"
        make_bin git code pacman sudo
        : >"$LOG"
        printf 'y\n' | FLEET_NO_DEPS_PROMPT=1 script -qec \
            "$REAL_SH -c 'FLEET_INSTALLER_LIB=1; . \"$SCRIPT\"; WITH_DEPS=0; PATH=\"$FAKEBIN\"; check_deps'" \
            /dev/null >"$WORK/out" 2>&1
        if has '[y/N]' || ! no_calls; then fail 'prompted or installed'; else pass; fi

        CASE="$NAME: interactive with all present does not prompt (AC-2)"
        make_bin git nvim yazi pacman sudo
        run_tty "$SCRIPT" y
        if has '[y/N]' || ! no_calls; then fail 'prompted or installed'; else pass; fi
    else
        printf 'skip interactive cases: no script(1)\n'
    fi
done

# The editor choice in install.sh: with neither nvim nor code on PATH it asks
# nvim / VS Code / skip on a terminal and records the answer in editor.json;
# anywhere else it prints both install commands and writes nothing.
SCRIPT="$ROOT/install.sh"
NAME=install.sh
EDITOR_JSON="$WORK/fleet-config/editor.json"
export FLEET_CONFIG_HOME="$WORK/fleet-config"

# editor_record needs mkdir and mv, which every real machine has.
editor_bin() {
    make_bin "$@"
    for util in mkdir mv; do
        ln -s "$(command -v "$util")" "$FAKEBIN/$util"
    done
}

expect_editor() {
    printf '{\n  "version": 1,\n  "editor": "%s"\n}\n' "$1" >"$WORK/expected-editor.json"
    cmp -s "$WORK/expected-editor.json" "$EDITOR_JSON"
}

CASE="$NAME: non-interactive with no editor prints both commands and writes nothing (AC-10)"
rm -rf "$FLEET_CONFIG_HOME"
editor_bin git yazi pacman sudo
run_deps "$SCRIPT" 0
if [ "$RC" != 0 ]; then fail "exit $RC"
elif ! has 'no editor found'; then fail 'did not say no editor'
elif ! has 'sudo pacman -S --needed --noconfirm neovim'; then fail 'nvim command'
elif ! has "VS Code and its 'code' shell command"; then fail 'vs code command'
elif [ -e "$EDITOR_JSON" ]; then fail 'wrote editor.json'
elif ! no_calls; then fail 'installed'
elif ! has 'after check_deps'; then fail 'did not continue'
else pass; fi

CASE="$NAME: code on PATH, nvim missing, does not ask for an editor (AC-11)"
rm -rf "$FLEET_CONFIG_HOME"
editor_bin git yazi code pacman sudo
run_deps "$SCRIPT" 0
if has 'no editor found' || has 'nvim / VS Code / skip'; then fail 'asked about the editor'
elif [ -e "$EDITOR_JSON" ]; then fail 'wrote editor.json'
elif ! has 'missing: nvim'; then fail 'dropped the existing nvim offer'
else pass; fi

if command -v script >/dev/null 2>&1; then
    CASE="$NAME: interactive with nvim present does not ask for an editor (AC-11)"
    rm -rf "$FLEET_CONFIG_HOME"
    editor_bin git nvim yazi pacman sudo
    run_tty "$SCRIPT" 2
    if has 'nvim / VS Code / skip' || [ -e "$EDITOR_JSON" ] || ! no_calls; then fail 'asked or wrote'; else pass; fi

    CASE="$NAME: interactive VS Code records vscode, hints the install and skips neovim (AC-10)"
    rm -rf "$FLEET_CONFIG_HOME"
    editor_bin git yazi pacman sudo
    run_tty "$SCRIPT" 2
    if ! has 'nvim / VS Code / skip'; then fail 'no editor prompt'
    elif ! expect_editor vscode; then fail "editor.json: $(cat "$EDITOR_JSON" 2>/dev/null)"
    elif ! has "VS Code and its 'code' shell command"; then fail 'vs code hint'
    elif called 'neovim'; then fail 'installed neovim'
    elif called 'pacman'; then fail 'ran a package manager for vs code'
    elif ! has 'after check_deps'; then fail 'did not continue'
    else pass; fi

    CASE="$NAME: interactive nvim records nvim and installs neovim on yes (AC-10)"
    rm -rf "$FLEET_CONFIG_HOME"
    editor_bin git yazi pacman sudo
    run_tty "$SCRIPT" "1
y"
    if ! has 'nvim / VS Code / skip'; then fail 'no editor prompt'
    elif ! expect_editor nvim; then fail "editor.json: $(cat "$EDITOR_JSON" 2>/dev/null)"
    elif ! called 'pacman -S --needed --noconfirm neovim'; then fail 'did not install neovim'
    else pass; fi

    CASE="$NAME: interactive skip records nothing and does not install neovim (AC-10)"
    rm -rf "$FLEET_CONFIG_HOME"
    editor_bin git yazi pacman sudo
    run_tty "$SCRIPT" 3
    if ! has 'nvim / VS Code / skip'; then fail 'no editor prompt'
    elif [ -e "$EDITOR_JSON" ]; then fail 'wrote editor.json'
    elif ! no_calls; then fail 'installed'
    elif ! has 'after check_deps'; then fail 'did not continue'
    else pass; fi
fi

unset FLEET_CONFIG_HOME

# End to end: the real scripts, so the check_deps call sites are covered too.
# No dotnet on PATH stops install.sh before it builds; curl is fake.
with_utils() {
    for util in sh cat dirname mktemp mkdir chmod mv rm grep tail ls; do
        ln -s "$(command -v "$util")" "$FAKEBIN/$util"
    done
    printf '#!%s\nurl=; out=\nwhile [ $# -gt 0 ]; do case "$1" in -o) out="$2"; shift ;; http*) url="$1" ;; esac; shift; done\ncase "$url" in\n    *install.sh) cat "%s/install.sh" >"$out" ;;\n    *) printf "#!%s\\nprintf \\"fleet %%%%s\\\\\\\\n\\" \\"\\$*\\" >>\\"\\$LOG\\"\\n" >"$out" ;;\nesac\n' \
        "$REAL_SH" "$ROOT" "$REAL_SH" >"$FAKEBIN/curl"
    chmod +x "$FAKEBIN/curl"
}

run_real() {
    OUT="$WORK/out"
    LOG="$WORK/log"
    : >"$LOG"
    export LOG
    rm -rf "${WORK:?}/home"
    mkdir -p "$WORK/home"
    env -u FLEET_INSTALLER_LIB HOME="$WORK/home" XDG_CONFIG_HOME="$WORK/home/.config" \
        FLEET_BIN_DIR="$WORK/home/bin" PATH="$FAKEBIN" "$REAL_SH" "$@" >"$OUT" 2>&1 </dev/null
    RC=$?
}

CASE="install.sh: a plain run checks deps before the build (AC-1, AC-5)"
make_bin git pacman sudo
with_utils
run_real "$ROOT/install.sh" --no-path
if ! has 'missing: nvim, yazi'; then fail 'no deps check'
elif ! has 'install them with: sudo pacman -S --needed --noconfirm neovim yazi'; then fail 'command'
elif ! has '.NET SDK is required'; then fail 'did not reach the dotnet check'
elif ! no_calls; then fail 'installed'
else pass; fi

CASE="install.sh: --deps-only installs the missing tools and stops (AC-6)"
make_bin git pacman sudo
with_utils
run_real "$ROOT/install.sh" --deps-only
if [ "$RC" != 0 ]; then fail "exit $RC"
elif ! called 'pacman -S --needed --noconfirm neovim yazi'; then fail 'pacman call'
elif has '.NET SDK'; then fail 'went on to build'
else pass; fi

CASE="get-fleet.sh: a plain run checks deps, then installs fleet (AC-5, AC-11)"
make_bin git pacman sudo
with_utils
run_real "$ROOT/scripts/get-fleet.sh" --no-path
if [ "$RC" != 0 ]; then fail "exit $RC"
elif ! has '==> Checking dependencies' || ! has 'missing: nvim, yazi'; then fail 'no deps check'
elif ! has 'install them with: sudo pacman -S --needed --noconfirm neovim yazi'; then fail 'command'
elif called 'pacman'; then fail 'installed'
elif ! called 'fleet setup'; then fail 'did not install fleet'
else pass; fi

CASE="get-fleet.sh: --with-deps delegates to install.sh and installs (AC-6, AC-11)"
make_bin git pacman sudo
with_utils
run_real "$ROOT/scripts/get-fleet.sh" --with-deps --no-path
if [ "$RC" != 0 ]; then fail "exit $RC"
elif ! has 'Fetching the dependency installer'; then fail 'did not delegate'
elif ! called 'pacman -S --needed --noconfirm neovim yazi'; then fail 'pacman call'
elif ! called 'fleet setup'; then fail 'did not install fleet'
else pass; fi

CASE="install.sh: --uninstall runs fleet uninstall when the binary exists (AC-14)"
make_bin git nvim yazi
with_utils
fake_fleet() {
    mkdir -p "$WORK/home/bin"
    printf '#!%s\nprintf "fleet %%s\\n" "$*" >>"$LOG"\nexit "${FAKE_FLEET_EXIT:-0}"\n' "$REAL_SH" >"$WORK/home/bin/fleet"
    chmod +x "$WORK/home/bin/fleet"
}
run_uninstall() {
    OUT="$WORK/out"
    LOG="$WORK/log"
    : >"$LOG"
    export LOG
    env -u FLEET_INSTALLER_LIB HOME="$WORK/home" XDG_CONFIG_HOME="$WORK/home/.config" SHELL=/bin/sh \
        FLEET_BIN_DIR="$WORK/home/bin" PATH="$FAKEBIN" "$REAL_SH" "$ROOT/install.sh" "$@" >"$OUT" 2>&1 </dev/null
    RC=$?
}
rm -rf "${WORK:?}/home"
fake_fleet
printf 'export A=1\nexport PATH=x # added by fleet installer\n' >"$WORK/home/.profile"
run_uninstall --uninstall
if [ "$RC" != 0 ]; then fail "exit $RC"
elif ! called 'fleet uninstall'; then fail 'did not delegate'
elif called 'fleet uninstall --yes'; then fail 'added --yes'
elif ! grep -qF 'added by fleet installer' "$WORK/home/.profile"; then fail 'also ran the old removal'
else pass; fi

CASE="install.sh: --uninstall --yes forwards --yes (AC-14)"
run_uninstall --uninstall --yes
if ! called 'fleet uninstall --yes'; then fail 'did not forward --yes'; else pass; fi

CASE="install.sh: --yes before --uninstall forwards it too (AC-14)"
run_uninstall --yes --uninstall
if ! called 'fleet uninstall --yes'; then fail 'did not delegate with --yes'; else pass; fi

CASE="install.sh: a failing fleet uninstall fails the uninstall (AC-14)"
FAKE_FLEET_EXIT=3
export FAKE_FLEET_EXIT
run_uninstall --uninstall
unset FAKE_FLEET_EXIT
if [ "$RC" != 3 ]; then fail "exit $RC, want 3"; else pass; fi

CASE="install.sh: --uninstall without the binary uses the old removal (AC-14)"
rm -f "$WORK/home/bin/fleet"
run_uninstall --uninstall --yes
if [ "$RC" != 0 ]; then fail "exit $RC"
elif called 'fleet'; then fail 'ran fleet'
elif ! has 'removed the PATH line'; then fail 'did not remove the PATH line'
elif grep -qF 'added by fleet installer' "$WORK/home/.profile"; then fail 'PATH line still there'
elif ! grep -qF 'export A=1' "$WORK/home/.profile"; then fail 'lost the rest of .profile'
else pass; fi

printf '\n%s passed, %s failed\n' "$PASS" "$FAIL"
[ "$FAIL" = 0 ]
