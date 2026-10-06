#!/usr/bin/env bash
# End-to-end check of the embedded multiplexer on Linux, with a real terminal.
#
#   scripts/e2e/linux.sh <path to a linux-x64 fleet binary>
#
# tmux (its own server, -L fe) is the terminal around `fleet` and `fleet attach`, so the
# Unix client, the PTYs and Terminal.Gui in panes all run for real. fleetd, its config and
# its socket are isolated under /tmp/fe; claude and nvim are stand-ins that print one line.
# Needs bash, tmux, git and python3. Exits with the number of failed checks.
#
# From Windows, with the binary CI built for a commit:
#   gh run download <run id> -n fleet-linux-x64 -D artifacts\e2e\linux
#   wsl -e bash scripts/e2e/linux.sh artifacts/e2e/linux/fleet
set -uo pipefail
here="$(cd "$(dirname "$0")" && pwd)"
root=/tmp/fe
rm -rf "$root" && mkdir -p "$root/bin" "$root/cfg/projects" "$root/demo1" "$root/demo2" "$root/stand"
cp "$1" "$root/bin/fleet" && chmod +x "$root/bin/fleet"
cp "$here/fleetctl.py" "$root/fleetctl.py"

for p in demo1 demo2; do
    git -C "$root/$p" init -q
    printf '{"version":1,"name":"%s","root":"%s/%s"}\n' "$p" "$root" "$p" > "$root/cfg/projects/$p.json"
done
for s in claude nvim; do
    printf '#!/bin/sh\ncase "$*" in *--continue*) r=resumed;; *) r=fresh;; esac\necho "%s-standin in $(basename "$PWD") $r"\nexec sh\n' "$s" > "$root/stand/$s"
    chmod +x "$root/stand/$s"
done

export FLEET_CONFIG_HOME=$root/cfg FLEET_MUX=embedded FLEET_ENDPOINT=$root/fleetd.sock
export PATH="$root/stand:$root/bin:$PATH" TERM=xterm-256color
T="tmux -L fe"
$T kill-server 2>/dev/null
trap 'kill "$(fleetd_pid)" 2>/dev/null; $T kill-server 2>/dev/null' EXIT
$T -f /dev/null new-session -d -s fe -x 120 -y 35 "env PS1='shell\$ ' bash --norc -i"
$T set -g -t fe status off
sleep 1

fails=0
screen() { $T capture-pane -p -t fe; }
keys() { $T send-keys -t fe "$@"; }
ctl() { python3 "$root/fleetctl.py" "$FLEET_ENDPOINT" "$@" 2>/dev/null; }
panes() {
    ctl list-panes | python3 -c '
import json,sys
for p in json.load(sys.stdin).get("panes") or []:
    print(p["id"], p["session"], p["tab"], "active" if p["active"] else "-", p.get("title") or "")'
}
dump() { screen | sed 's/^/     | /'; }
ok() { echo "ok   $1"; }
bad() { echo "FAIL $1"; fails=$((fails + 1)); [ "${2:-}" = noscreen ] || dump; }
sees() { if screen | grep -qF -- "$1"; then ok "$2"; else bad "$2"; fi; }
lacks() { if screen | grep -qF -- "$1"; then bad "$2"; else ok "$2"; fi; }
fleetd_pid() { pgrep -f "^$root/bin/fleet daemon" | head -1; }

echo "== 1. fleet: project picker, open demo1"
keys 'fleet' Enter; sleep 4
sees 'demo1' 'picker lists demo1'
sees 'demo2' 'picker lists demo2'
keys Enter; sleep 6
sees 'nvim-standin in demo1' 'orchestrator pane started in demo1'
sees 'Agents' 'dashboard drawn'
row0="$(screen | head -1)"
case "$row0" in *demo1*) ok "top bar shows demo1: ${row0:0:70}";; *) bad 'top bar on row 0';; esac
panes

echo "== 2. prefix shows which-key, esc closes it"
keys C-s; sleep 1
sees 'split right' 'which-key lists split right'
sees 'detach' 'which-key lists detach'
keys Escape; sleep 1
lacks 'split right' 'which-key closed'

echo "== 3. menu float (prefix space), esc closes"
keys C-s Space; sleep 3
sees 'fleet menu' 'menu float drawn'
sees 'List agents' 'menu items drawn'
keys Escape; sleep 2
lacks 'List agents' 'menu closed'

echo "== 4. split, new tab, zoom, tab switch"
before=$(panes | wc -l)
keys C-s %; sleep 2
after=$(panes | wc -l)
[ "$after" -eq $((before + 1)) ] && ok "split added a pane ($before -> $after)" || bad "split ($before -> $after)"
keys 'echo SPLIT-PANE' Enter; sleep 1
sees 'SPLIT-PANE' 'typing reaches the split pane'
keys C-s c; sleep 2
tabs=$(panes | awk '$2=="demo1" && $3!="float" {print $3}' | sort -u | wc -l)
[ "$tabs" -ge 2 ] && ok "new tab ($tabs tabs)" || bad "new tab ($tabs tabs)"
keys C-s 1; sleep 1
sees 'SPLIT-PANE' 'prefix 1 goes back to the first tab'
keys C-s z; sleep 1
lacks 'Agents' 'zoom hides the other panes'
keys C-s z; sleep 1
sees 'Agents' 'unzoom shows them again'

echo "== 5. float: new, type, toggle"
keys C-s f; sleep 2
panes | grep -q ' float ' && ok 'float pane exists' || bad 'float pane exists' noscreen
keys 'echo IN-FLOAT' Enter; sleep 1
sees 'IN-FLOAT' 'typing reaches the float'
keys C-s t; sleep 1
lacks 'IN-FLOAT' 'toggle hides floats'
keys C-s t; sleep 1
sees 'IN-FLOAT' 'toggle shows them again'
keys C-s t; sleep 1

echo "== 6. resize the terminal (SIGWINCH)"
$T resize-window -t fe -x 100 -y 30; sleep 2
lines=$(screen | wc -l)
widest=$(screen | python3 -c 'import sys; print(max(len(l.rstrip("\n")) for l in sys.stdin))')
[ "$lines" -eq 30 ] && [ "$widest" -le 100 ] && ok "frame fits 100x30 (rows $lines, widest $widest)" || bad "frame after resize (rows $lines, widest $widest)"
sees 'Agents' 'dashboard redrawn after resize'

echo "== 7. mouse: click the second tab in the top bar"
bar="$(screen | head -1)"
col=$(python3 -c 'import sys; b=sys.argv[1]; i=b.find(" 2"); print(i+2 if i>=0 else -1)' "$bar")
if [ "$col" -gt 0 ]; then
    keys -l $'\e[<0;'"$col"$';1M\e[<0;'"$col"$';1m'; sleep 1
    active=$(panes | awk '$2=="demo1" && $4=="active" {print $3}' | head -1)
    first=$(panes | awk '$2=="demo1" && $3!="float" {print $3}' | head -1)
    [ -n "$active" ] && [ "$active" != "$first" ] && ok "click switched to tab $active" || bad "click on tab 2 (active: $active)"
else
    bad "no tab 2 in bar: $bar" noscreen
fi
keys C-s 1; sleep 1

echo "== 8. copy mode enters and leaves"
keys C-s '['; sleep 1
if screen | grep -qiE 'copy'; then ok 'copy mode shown'; else bad 'copy mode shown'; fi
keys q; sleep 1

echo "== 9. switch project from the menu"
keys C-s Space; sleep 3
keys s; sleep 3
sees 'demo2' 'switch picker lists demo2'
keys Enter; sleep 6
row0="$(screen | head -1)"
case "$row0" in *demo2*) ok 'bar shows demo2';; *) bad 'bar shows demo2';; esac
sees 'nvim-standin in demo2' 'demo2 orchestrator started'

echo "== 10. detach restores the terminal, fleetd keeps running"
keys C-s d; sleep 2
sees 'shell$' 'back at the shell prompt'
keys 'stty -a | grep -oE -- "(-)?icanon|(-)?echo " | tr "\n" " "; echo' Enter; sleep 1
if screen | grep -qxE 'icanon echo ?'; then ok 'tty is canonical with echo again'; else bad 'tty modes after detach'; fi
[ -n "$(fleetd_pid)" ] && ok "fleetd still runs (pid $(fleetd_pid))" || bad 'fleetd still runs' noscreen

echo "== 11. fleet attach shows the session picker"
keys clear Enter; keys 'fleet attach' Enter; sleep 3
sees 'Attach to' 'session picker shown'
keys Enter; sleep 2
row0="$(screen | head -1)"
case "$row0" in *demo*) ok "attached: ${row0:0:60}";; *) bad 'attached after picker';; esac
keys C-s d; sleep 2

echo "== 12. restore after fleetd is killed"
sleep 2
saved=$(ls "$root/cfg"/embedded-session*.json 2>/dev/null | head -1)
[ -n "$saved" ] && ok "snapshot saved: $(basename "$saved")" || bad 'snapshot saved' noscreen
panes | awk '{print $2, $3}' | sort > "$root/before.txt"
kill -9 "$(fleetd_pid)"; sleep 1
[ -z "$(fleetd_pid)" ] && ok 'fleetd killed' || bad 'fleetd killed' noscreen
keys clear Enter; keys 'fleet attach --project demo1' Enter; sleep 5
[ -n "$(fleetd_pid)" ] && ok 'attach started a new fleetd' || bad 'new fleetd' noscreen
panes | awk '{print $2, $3}' | sort > "$root/after-raw.txt"
b=$(awk '{print $1}' "$root/before.txt" | sort | uniq -c | tr -s ' '); a=$(awk '{print $1}' "$root/after-raw.txt" | sort | uniq -c | tr -s ' ')
[ "$b" = "$a" ] && ok "same panes per workspace after restore: $(echo $a)" || bad "panes per workspace: before [$(echo $b)] after [$(echo $a)]" noscreen
sees 'Agents' 'restored dashboard drawn'
sees 'nvim-standin in demo1 resumed' 'orchestrator relaunched with resume'
grep -h 'restored' "$root/cfg/fleet.log" | tail -1
keys C-s d; sleep 1

echo "== summary: $fails failure(s)"
exit $fails
