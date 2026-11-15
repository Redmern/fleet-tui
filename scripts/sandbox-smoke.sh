#!/usr/bin/env bash
# Smoke-test a fleet binary inside a sandbox, so it can never stop the real daemon or
# touch real config.
#
#   scripts/sandbox-smoke.sh <path to a fleet binary>
#
# Linux with GNU coreutils (realpath, timeout, mktemp); not for macOS or the BSDs.
#
# The binary runs under 'env -i': nothing from the caller's environment reaches it. It gets
# a throwaway HOME, XDG_CONFIG_HOME, XDG_DATA_HOME, XDG_STATE_HOME, XDG_CACHE_HOME,
# XDG_RUNTIME_DIR, FLEET_CONFIG_HOME, CLAUDE_CONFIG_DIR, CODEX_HOME, PI_CODING_AGENT_DIR and
# FLEET_ENDPOINT (a socket in the sandbox), a short
# PATH (/usr/local/bin:/usr/bin:/bin plus the folders of git and nvim when they live
# elsewhere) and TERM. Fleet panes export the real FLEET_ENDPOINT; the sandbox never uses it.
#
# Steps, all inside the sandbox:
#   1. fleet --help             must exit 0
#   2. fleet daemon             started in the background; the endpoint must appear
#   3. fleet doctor             must run to the end (exit 0 or 1) and report this fleetd's pid
#   4. fleet daemon stop        must exit 0, the endpoint must go and the process must exit
#
# Doctor runs while the daemon is up because it starts a daemon of its own (detached, not a
# child of this script) when none answers. Doctor exits 1 when it reports problems such as a
# missing nvim or harness or a keybind file that is not written yet; that says something
# about the machine, not the binary, so it is reported and does not fail the run. Any other
# exit code (a crash, a signal, a hang past 60 s) fails it.
#
# Refuses (exit 2, nothing run) when the sandbox endpoint equals the caller's
# FLEET_ENDPOINT or the default endpoint of the caller's environment, or when the sandbox
# runtime dir or HOME equals the caller's. The sandbox values are always fresh temp paths;
# SANDBOX_FLEET_ENDPOINT, SANDBOX_XDG_RUNTIME_DIR and SANDBOX_HOME override them and exist
# so that the refusal can be tested. Values are compared after trimming whitespace (the binary
# trims FLEET_ENDPOINT) and resolving symlinks.
#
# The sandbox is removed on exit. First 'fleet daemon stop' runs in it, which also stops a
# daemon that doctor started; a daemon this script started that survives that is killed by pid.
#
# Exit codes: 0 all steps passed, 1 a step failed, 2 refused or bad arguments or setup failed,
# 130 INT, 143 TERM, 129 HUP.

set -u

usage() {
    sed -n '2,/^$/{/^#/s/^# \{0,1\}//p}' "$0"
}

if [ "$#" -ne 1 ] || [ "$1" = -h ] || [ "$1" = --help ]; then
    echo "usage: $0 <path to a fleet binary>" >&2
    usage >&2
    exit 2
fi

BIN="$1"
if [ ! -f "$BIN" ] || [ ! -x "$BIN" ]; then
    echo "sandbox-smoke: $BIN is not an executable file" >&2
    exit 2
fi
BIN_DIR="$(CDPATH= cd -- "$(dirname -- "$BIN")" && pwd -P)" || { echo "sandbox-smoke: cannot resolve the folder of $BIN" >&2; exit 2; }
BIN="$BIN_DIR/$(basename -- "$BIN")"

trim() {
    local v="$1"
    v="${v#"${v%%[![:space:]]*}"}"
    v="${v%"${v##*[![:space:]]}"}"
    printf '%s' "$v"
}

norm() {
    local v
    v="$(trim "$1")"
    [ -n "$v" ] || return 0
    realpath -m -- "$v" 2>/dev/null || printf '%s' "${v%/}"
}

refuse() {
    echo "sandbox-smoke: refusing to run: $1" >&2
    exit 2
}

real_endpoints() {
    if [ -n "${FLEET_ENDPOINT:-}" ]; then
        norm "$FLEET_ENDPOINT"
        echo
    fi
    if [ -n "${XDG_RUNTIME_DIR:-}" ]; then
        norm "$XDG_RUNTIME_DIR/fleet/embedded.sock"
    else
        norm "${TMPDIR:-/tmp}/fleet-$(id -un)/embedded.sock"
    fi
    echo
}

SANDBOX=""
DAEMON_PID=""
ARMED=0
FAILS=0
ENV_ARGS=()

# shellcheck disable=SC2329
cleanup() {
    local status=$?
    if [ "$ARMED" = 1 ] && [ -e "$S_ENDPOINT" ]; then
        timeout --foreground 20 env -i "${ENV_ARGS[@]}" "$BIN" daemon stop >/dev/null 2>&1 </dev/null
    fi
    if [ -n "$DAEMON_PID" ] && kill -0 "$DAEMON_PID" 2>/dev/null; then
        sleep 0.3
        kill "$DAEMON_PID" 2>/dev/null
        sleep 0.5
        kill -9 "$DAEMON_PID" 2>/dev/null
    fi
    [ -n "$SANDBOX" ] && rm -rf -- "$SANDBOX"
    exit "$status"
}
trap cleanup EXIT
trap 'exit 130' INT
trap 'exit 143' TERM
trap 'exit 129' HUP

made="$(mktemp -d "${TMPDIR:-/tmp}/fleet-smoke.XXXXXX")" || { echo "sandbox-smoke: cannot create a temp dir" >&2; exit 2; }
[ -n "$made" ] && [ -d "$made" ] || { echo "sandbox-smoke: cannot create a temp dir" >&2; exit 2; }
SANDBOX="$made"
resolved="$(CDPATH= cd -- "$SANDBOX" && pwd -P)" || { echo "sandbox-smoke: cannot enter $SANDBOX" >&2; exit 2; }
[ -n "$resolved" ] && [ "$resolved" != / ] || { echo "sandbox-smoke: bad sandbox dir '$resolved'" >&2; exit 2; }
SANDBOX="$resolved"

S_HOME="${SANDBOX_HOME:-$SANDBOX/home}"
S_RUNTIME="${SANDBOX_XDG_RUNTIME_DIR:-$SANDBOX/run}"
S_ENDPOINT="$(trim "${SANDBOX_FLEET_ENDPOINT:-$S_RUNTIME/fleet.sock}")"

while IFS= read -r real; do
    [ -n "$real" ] && [ "$(norm "$S_ENDPOINT")" = "$real" ] && refuse "the sandbox endpoint $S_ENDPOINT is the real daemon's"
done < <(real_endpoints)
[ -n "${XDG_RUNTIME_DIR:-}" ] && [ "$(norm "$S_RUNTIME")" = "$(norm "$XDG_RUNTIME_DIR")" ] && refuse "the sandbox runtime dir $S_RUNTIME is the real XDG_RUNTIME_DIR"
[ -n "${HOME:-}" ] && [ "$(norm "$S_HOME")" = "$(norm "$HOME")" ] && refuse "the sandbox HOME $S_HOME is the real HOME"

mkdir -p "$S_HOME" "$S_RUNTIME" "$SANDBOX/config" "$SANDBOX/data" "$SANDBOX/state" "$SANDBOX/cache" "$SANDBOX/fleet-config" "$SANDBOX/tmp" "$SANDBOX/claude" "$SANDBOX/codex" "$SANDBOX/pi" \
    || { echo "sandbox-smoke: cannot create the sandbox folders" >&2; exit 2; }
chmod 700 "$SANDBOX" "$S_RUNTIME" || { echo "sandbox-smoke: cannot protect the sandbox folders" >&2; exit 2; }

S_PATH="/usr/local/bin:/usr/bin:/bin"
for tool in git nvim; do
    found="$(command -v "$tool" 2>/dev/null)" || continue
    dir="$(dirname -- "$found")"
    case ":$S_PATH:" in
    *":$dir:"*) ;;
    *) S_PATH="$S_PATH:$dir" ;;
    esac
done

ENV_ARGS=(
    "HOME=$S_HOME"
    "XDG_CONFIG_HOME=$SANDBOX/config"
    "XDG_DATA_HOME=$SANDBOX/data"
    "XDG_STATE_HOME=$SANDBOX/state"
    "XDG_CACHE_HOME=$SANDBOX/cache"
    "XDG_RUNTIME_DIR=$S_RUNTIME"
    "FLEET_CONFIG_HOME=$SANDBOX/fleet-config"
    "CLAUDE_CONFIG_DIR=$SANDBOX/claude"
    "CODEX_HOME=$SANDBOX/codex"
    "PI_CODING_AGENT_DIR=$SANDBOX/pi"
    "FLEET_ENDPOINT=$S_ENDPOINT"
    "TMPDIR=$SANDBOX/tmp"
    "PATH=$S_PATH"
    "TERM=${TERM:-xterm-256color}"
    "LANG=C.UTF-8"
)
[ -n "${DOTNET_ROOT:-}" ] && ENV_ARGS+=("DOTNET_ROOT=$DOTNET_ROOT")
ARMED=1

sandboxed() {
    env -i "${ENV_ARGS[@]}" "$BIN" "$@"
}

ok() { printf 'ok   %s\n' "$1"; }
bad() { printf 'FAIL %s\n' "$1"; FAILS=$((FAILS + 1)); }
indent() { sed 's/^/     | /' "$1"; }

echo "sandbox $SANDBOX"
echo "endpoint $S_ENDPOINT"

OUT="$SANDBOX/out.txt"

sandboxed --help >"$OUT" 2>&1 </dev/null
rc=$?
if [ "$rc" = 0 ]; then ok "fleet --help"; else bad "fleet --help exited $rc"; indent "$OUT"; fi

env -i "${ENV_ARGS[@]}" "$BIN" daemon >"$SANDBOX/daemon.log" 2>&1 </dev/null &
DAEMON_PID=$!
waited=0
while [ ! -e "$S_ENDPOINT" ] && kill -0 "$DAEMON_PID" 2>/dev/null && [ "$waited" -lt 100 ]; do
    sleep 0.1
    waited=$((waited + 1))
done
if [ -e "$S_ENDPOINT" ]; then
    ok "fleet daemon: started (pid $DAEMON_PID)"
else
    bad "fleet daemon did not create $S_ENDPOINT"
    indent "$SANDBOX/daemon.log"
    exit 1
fi

timeout --foreground 60 env -i "${ENV_ARGS[@]}" "$BIN" doctor >"$OUT" 2>&1 </dev/null
rc=$?
if [ "$rc" -gt 1 ]; then
    bad "fleet doctor exited $rc"
    indent "$OUT"
elif ! grep -qF "$SANDBOX/fleet-config" "$OUT"; then
    bad "fleet doctor did not report the sandbox config dir $SANDBOX/fleet-config"
    indent "$OUT"
elif ! grep -qE "fleetd +pid $DAEMON_PID([^0-9]|\$)" "$OUT"; then
    bad "fleet doctor does not show fleetd pid $DAEMON_PID"
    indent "$OUT"
elif [ "$rc" = 0 ]; then
    ok "fleet doctor: healthy, sees the sandbox fleetd (pid $DAEMON_PID)"
else
    ok "fleet doctor: sees the sandbox fleetd (pid $DAEMON_PID); exit 1 means it reported problems (below), not a failure"
    indent "$OUT"
fi

timeout --foreground 60 env -i "${ENV_ARGS[@]}" "$BIN" daemon stop >"$OUT" 2>&1 </dev/null
rc=$?
if [ "$rc" = 0 ]; then ok "fleet daemon stop"; else bad "fleet daemon stop exited $rc"; indent "$OUT"; fi

waited=0
while kill -0 "$DAEMON_PID" 2>/dev/null && [ "$waited" -lt 100 ]; do
    sleep 0.1
    waited=$((waited + 1))
done
if kill -0 "$DAEMON_PID" 2>/dev/null; then
    bad "daemon pid $DAEMON_PID still running after daemon stop"
else
    wait "$DAEMON_PID" 2>/dev/null
    DAEMON_PID=""
    ok "daemon process exited"
fi
if [ -e "$S_ENDPOINT" ]; then bad "endpoint $S_ENDPOINT still exists after daemon stop"; else ok "endpoint removed"; fi

if [ "$FAILS" = 0 ]; then
    echo "sandbox-smoke: all steps passed"
    exit 0
fi
echo "sandbox-smoke: $FAILS step(s) failed"
exit 1
