#!/usr/bin/env bash
# Tests scripts/sandbox-smoke.sh against a fake fleet binary that logs how it was called.
#
#   bash tests/scripts/sandbox-smoke.sh
#
# The fake never touches the network or a real daemon. Its "daemon" is a background sh
# that creates the endpoint file. Cases: the happy path, the refusal when the sandbox
# endpoint or runtime dir equals the real one, a failing step, and cleanup of a daemon
# that ignores 'daemon stop'.

set -u

ROOT="$(CDPATH= cd -- "$(dirname -- "$0")/../.." && pwd)"
SCRIPT="$ROOT/scripts/sandbox-smoke.sh"
WORK="$(mktemp -d)"
trap 'rm -rf "$WORK"' EXIT

PASS=0
FAIL=0

fail() {
    FAIL=$((FAIL + 1))
    printf 'FAIL %s: %s\n' "$CASE" "$1"
    printf '%s\n' '--- output' "$(cat "$OUT" 2>/dev/null)" '--- calls' "$(cat "$LOG" 2>/dev/null)" '---'
}

pass() {
    PASS=$((PASS + 1))
    printf 'ok   %s\n' "$CASE"
}

# make_fake <doctor exit> <stop mode: stops|ignores> -> $FAKE, $LOG
# The fake appends "args | HOME | XDG_RUNTIME_DIR | FLEET_ENDPOINT" per call to $LOG and
# dumps its whole environment to $LOG.env on 'doctor'.
make_fake() {
    FAKE="$WORK/fleet"
    LOG="$WORK/calls.log"
    : >"$LOG"
    cat >"$FAKE" <<EOF
#!/bin/sh
printf '%s | %s | %s | %s\n' "\$*" "\${HOME:-}" "\${XDG_RUNTIME_DIR:-}" "\${FLEET_ENDPOINT:-}" >>"$LOG"
case "\$*" in
"--help") echo "fleet usage"; exit 0 ;;
doctor)
    env >"$LOG.env"
    echo "fleet doctor"
    echo "  config        \${FLEET_CONFIG_HOME:-}"
    if [ -e "\$FLEET_ENDPOINT" ]; then echo "  fleetd        pid \$(cat "\$FLEET_ENDPOINT")"; else echo "  fleetd        not running (fleet attach starts it)"; fi
    exit $1 ;;
daemon)
    echo \$\$ >"\$FLEET_ENDPOINT"
    echo \$\$ >"$LOG.daemon"
    while :; do sleep 1; done ;;
"daemon stop")
    if [ "$2" = ignores ]; then echo "stop ignored"; exit 1; fi
    pid=\$(cat "\$FLEET_ENDPOINT" 2>/dev/null) && kill "\$pid" 2>/dev/null
    rm -f "\$FLEET_ENDPOINT"
    echo "fleetd (pid \$pid) stopped"
    exit 0 ;;
*) exit 64 ;;
esac
EOF
    chmod +x "$FAKE"
}

# run_smoke [env assignments...] -> $RC, $OUT. FLEET_ENDPOINT and XDG_RUNTIME_DIR stand
# in for the real daemon's; the fake must never see them.
run_smoke() {
    OUT="$WORK/out.log"
    REAL_RUNTIME="$WORK/real-runtime"
    mkdir -p "$REAL_RUNTIME/fleet"
    : >"$REAL_RUNTIME/custom.sock"
    : >"$REAL_RUNTIME/fleet/embedded.sock"
    env HOME="$WORK/real-home" XDG_RUNTIME_DIR="$REAL_RUNTIME" FLEET_ENDPOINT="$REAL_RUNTIME/custom.sock" \
        TMPDIR="$WORK" "$@" bash "$SCRIPT" "$FAKE" >"$OUT" 2>&1
    RC=$?
}

has() { grep -qF -- "$1" "$OUT"; }
called() { grep -qF -- "$1" "$LOG"; }
leftover_dirs() { find "$WORK" -maxdepth 1 -name 'fleet-smoke.*' | grep -q .; }
daemon_alive() { [ -f "$LOG.daemon" ] && kill -0 "$(cat "$LOG.daemon")" 2>/dev/null; }

CASE="happy path runs help, doctor, daemon start and stop in the sandbox"
make_fake 0 stops
run_smoke
if [ "$RC" != 0 ]; then fail "exit $RC"
elif ! called '--help |' || ! called 'doctor |' || ! called 'daemon |' || ! called 'daemon stop |'; then fail 'a step did not run'
elif grep -qF "$REAL_RUNTIME" "$LOG" || grep -qF "$WORK/real-home" "$LOG"; then fail 'the fake saw a real path'
elif daemon_alive; then fail 'daemon still alive'
elif leftover_dirs; then fail 'temp dir left behind'
else pass; fi

CASE="the fake sees a cleared environment: no caller variables leak in"
make_fake 0 stops
run_smoke SECRET_TOKEN=leak FLEET_CONFIG_HOME="$WORK/real-cfg" CLAUDE_CONFIG_DIR="$WORK/real-claude"
if [ "$RC" != 0 ]; then fail "exit $RC"
elif grep -qE 'SECRET_TOKEN|real-cfg|real-claude|real-home|real-runtime' "$LOG.env"; then fail "leaked: $(grep -E 'SECRET_TOKEN|real-' "$LOG.env" | head -3)"
elif ! grep -q '^HOME=.*fleet-smoke\.' "$LOG.env" || ! grep -q '^XDG_RUNTIME_DIR=.*fleet-smoke\.' "$LOG.env" || ! grep -q '^FLEET_ENDPOINT=.*fleet-smoke\.' "$LOG.env"; then fail 'HOME, XDG_RUNTIME_DIR or FLEET_ENDPOINT not in the sandbox'
elif ! grep -q '^FLEET_CONFIG_HOME=.*fleet-smoke\.' "$LOG.env" || ! grep -q '^CLAUDE_CONFIG_DIR=.*fleet-smoke\.' "$LOG.env" \
    || ! grep -q '^CODEX_HOME=.*fleet-smoke\.' "$LOG.env" || ! grep -q '^PI_CODING_AGENT_DIR=.*fleet-smoke\.' "$LOG.env"; then fail 'a config home is not in the sandbox'
elif ! grep -q '^XDG_CONFIG_HOME=.*fleet-smoke\.' "$LOG.env" || ! grep -q '^XDG_DATA_HOME=.*fleet-smoke\.' "$LOG.env" \
    || ! grep -q '^XDG_STATE_HOME=.*fleet-smoke\.' "$LOG.env" || ! grep -q '^XDG_CACHE_HOME=.*fleet-smoke\.' "$LOG.env"; then fail 'an XDG dir is not in the sandbox'
else pass; fi

CASE="refuses when the sandbox endpoint equals the real FLEET_ENDPOINT, runs nothing"
make_fake 0 stops
run_smoke SANDBOX_FLEET_ENDPOINT="$WORK/real-runtime/custom.sock"
if [ "$RC" = 0 ]; then fail 'exit 0'
elif [ -s "$LOG" ]; then fail 'the binary was run'
elif ! has 'refusing'; then fail 'no refusal message'
elif leftover_dirs; then fail 'temp dir left behind'
else pass; fi

CASE="refuses when the sandbox endpoint is the real one plus trailing whitespace (the binary trims it)"
make_fake 0 stops
run_smoke SANDBOX_FLEET_ENDPOINT="$WORK/real-runtime/custom.sock "
if [ "$RC" != 2 ]; then fail "exit $RC"
elif [ -s "$LOG" ]; then fail 'the binary was run'
elif ! has 'refusing'; then fail 'no refusal message'
else pass; fi

CASE="refuses when the sandbox endpoint reaches the real one through a symlink"
make_fake 0 stops
mkdir -p "$WORK/real-runtime"
ln -sfn "$WORK/real-runtime" "$WORK/link-runtime"
run_smoke SANDBOX_FLEET_ENDPOINT="$WORK/link-runtime/custom.sock"
if [ "$RC" != 2 ]; then fail "exit $RC"
elif [ -s "$LOG" ]; then fail 'the binary was run'
elif ! has 'refusing'; then fail 'no refusal message'
else pass; fi

CASE="exits 2 and runs nothing when the temp dir cannot be created"
make_fake 0 stops
run_smoke TMPDIR="$WORK/does-not-exist"
if [ "$RC" != 2 ]; then fail "exit $RC"
elif [ -s "$LOG" ]; then fail 'the binary was run'
else pass; fi

CASE="refuses when the sandbox runtime dir is the real XDG_RUNTIME_DIR"
make_fake 0 stops
run_smoke SANDBOX_XDG_RUNTIME_DIR="$WORK/real-runtime"
if [ "$RC" = 0 ]; then fail 'exit 0'
elif [ -s "$LOG" ]; then fail 'the binary was run'
elif ! has 'refusing'; then fail 'no refusal message'
else pass; fi

CASE="refuses when the sandbox endpoint is the default under the real XDG_RUNTIME_DIR (FLEET_ENDPOINT unset)"
make_fake 0 stops
OUT="$WORK/out.log"
mkdir -p "$WORK/real-runtime/fleet"
: >"$WORK/real-runtime/fleet/embedded.sock"
env -u FLEET_ENDPOINT HOME="$WORK/real-home" XDG_RUNTIME_DIR="$WORK/real-runtime" TMPDIR="$WORK" \
    SANDBOX_FLEET_ENDPOINT="$WORK/real-runtime/fleet/embedded.sock" bash "$SCRIPT" "$FAKE" >"$OUT" 2>&1
RC=$?
if [ "$RC" = 0 ]; then fail 'exit 0'
elif [ -s "$LOG" ]; then fail 'the binary was run'
else pass; fi

CASE="doctor exit 1 (problems reported) is reported, not fatal"
make_fake 1 stops
run_smoke
if [ "$RC" != 0 ]; then fail "exit $RC"
elif ! has 'reported'; then fail 'no mention that doctor reported problems'
else pass; fi

CASE="doctor exit 2 (crash) fails the script and the daemon is still stopped"
make_fake 2 stops
run_smoke
sleep 0.3
if [ "$RC" = 0 ]; then fail 'exit 0'
elif daemon_alive; then kill "$(cat "$LOG.daemon")" 2>/dev/null; fail 'daemon left running'
elif leftover_dirs; then fail 'temp dir left behind'
else pass; fi

CASE="a daemon that ignores 'daemon stop' fails the script but is still killed"
make_fake 0 ignores
run_smoke
sleep 0.3
if [ "$RC" = 0 ]; then fail 'exit 0'
elif daemon_alive; then kill "$(cat "$LOG.daemon")" 2>/dev/null; fail 'daemon left running'
elif leftover_dirs; then fail 'temp dir left behind'
else pass; fi

CASE="no argument prints usage and exits non-zero"
OUT="$WORK/out.log"
bash "$SCRIPT" >"$OUT" 2>&1
RC=$?
if [ "$RC" = 0 ]; then fail 'exit 0'
elif ! has 'usage'; then fail 'no usage text'
else pass; fi

CASE="a missing binary fails with a clear message"
OUT="$WORK/out.log"
bash "$SCRIPT" "$WORK/nope" >"$OUT" 2>&1
RC=$?
if [ "$RC" = 0 ]; then fail 'exit 0'
elif ! has 'not an executable'; then fail 'no message'
else pass; fi

printf '\n%s passed, %s failed\n' "$PASS" "$FAIL"
[ "$FAIL" = 0 ]
