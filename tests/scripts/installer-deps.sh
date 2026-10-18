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
    printf '#!%s\nprintf "%%s\\n" "${FAKE_UNAME:-Linux}"\n' "$REAL_SH" >"$FAKEBIN/uname"
    printf '#!%s\nprintf "1000\\n"\n' "$REAL_SH" >"$FAKEBIN/id"
    for tool in "$@"; do
        case "$tool" in
            git | nvim | yazi)
                printf '#!%s\nexit 0\n' "$REAL_SH" >"$FAKEBIN/$tool"
                ;;
            sudo)
                printf '#!%s\nprintf "sudo %%s\\n" "$*" >>"$LOG"\nexec "$@"\n' "$REAL_SH" >"$FAKEBIN/sudo"
                ;;
            *)
                printf '#!%s\nprintf "%s %%s\\n" "$*" >>"$LOG"\n[ -z "${FAKE_FAIL:-}" ]\n' "$REAL_SH" "$tool" >"$FAKEBIN/$tool"
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

    if command -v script >/dev/null 2>&1; then
        CASE="$NAME: interactive yes installs the missing tools (AC-3)"
        make_bin git pacman sudo
        run_tty "$SCRIPT" y
        if ! has 'nvim, yazi' || ! has '[y/N]'; then fail 'no prompt'
        elif ! called 'pacman -S --needed --noconfirm neovim yazi'; then fail 'did not install'
        elif ! has 'after check_deps'; then fail 'did not continue'
        else pass; fi

        CASE="$NAME: interactive Enter installs nothing and prints the command (AC-4)"
        make_bin git pacman sudo
        run_tty "$SCRIPT" ''
        if ! has '[y/N]'; then fail 'no prompt'
        elif ! no_calls; then fail 'installed'
        elif ! has 'sudo pacman -S --needed --noconfirm neovim yazi'; then fail 'command'
        elif ! has 'after check_deps'; then fail 'did not continue'
        else pass; fi

        CASE="$NAME: interactive no installs nothing (AC-4)"
        make_bin git pacman sudo
        run_tty "$SCRIPT" n
        if ! no_calls; then fail 'installed'; else pass; fi

        CASE="$NAME: FLEET_NO_DEPS_PROMPT skips the prompt (AC-3)"
        make_bin git pacman sudo
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

printf '\n%s passed, %s failed\n' "$PASS" "$FAIL"
[ "$FAIL" = 0 ]
