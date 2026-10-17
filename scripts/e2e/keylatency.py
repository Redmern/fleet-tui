#!/usr/bin/env python3
"""Key -> frame latency harness for fleet on Linux.

    python3 scripts/e2e/keylatency.py run  <fleet binary> [options]   # measure, print a report
    python3 scripts/e2e/keylatency.py log  <fleet.log> [--panes p3,p4] # summarise FLEET_KEY_TIMING lines

`run` starts an isolated fleetd (its own config, socket and stand-in claude/nvim under --root),
drives `fleet` in a pseudo terminal it owns, and for each screen (menu, picker, dashboard) presses
keys one at a time: it waits for the screen to be quiet, writes the key, and timestamps the first
output byte that follows (key -> frame, end to end) and the last byte before quiet again (frame
complete). fleetd runs with FLEET_KEY_TIMING=1, so its log splits the same keys into the hop inside
the app (key reaches the pane -> the pane answers) and the hop inside fleetd (pane output -> frame).
`--direct` also runs `fleet menu` straight in the harness pty, outside fleetd, once answering the
terminal queries it asks and once answering none of them. `--busy N` adds N hidden panes printing
~100 lines/s. Unit tests: python3 scripts/e2e/keylatency_test.py
"""
import argparse
import json
import random
import os
import re
import shutil
import signal
import socket
import struct
import subprocess
import sys
import threading
import time
from collections import namedtuple

KeyTiming = namedtuple('KeyTiming', 'client pane output_ms frame_ms')
MenuTiming = namedtuple('MenuTiming', 'client warm first_frame_ms settle_ms')

KEY_LINE = re.compile(r'key timing (\S+) (\S+): output ([\d.]+) ms, frame ([\d.]+) ms$')
MENU_LINE = re.compile(r'menu timing (\S+): (warm|cold), first frame ([\d.]+) ms, settle ([\d.]+) ms')

QUERIES = [
    ('DA1', re.compile(rb'\x1b\[0?c')),
    ('DA2', re.compile(rb'\x1b\[>0?c')),
    ('DA3', re.compile(rb'\x1b\[=0?c')),
    ('CPR', re.compile(rb'\x1b\[\??6n')),
    ('size-18t', re.compile(rb'\x1b\[18t')),
    ('size-14t', re.compile(rb'\x1b\[14t')),
    ('size-16t', re.compile(rb'\x1b\[16t')),
    ('kitty-keys', re.compile(rb'\x1b\[\?u')),
    ('XTVERSION', re.compile(rb'\x1b\[>0?q')),
    ('DECRQM', re.compile(rb'\x1b\[\?\d+\$p')),
    ('OSC10', re.compile(rb'\x1b\]10;\?')),
    ('OSC11', re.compile(rb'\x1b\]11;\?')),
    ('OSC4', re.compile(rb'\x1b\]4;\d+;\?')),
    ('XTGETTCAP', re.compile(rb'\x1bP\+q')),
]


HEARTBEAT = re.compile(rb'(?:\x1b\[\?25[lh]|\x1b\[\d*(?:;\d*)?H)+')


def is_heartbeat(data):
    return bool(data) and HEARTBEAT.fullmatch(data) is not None


def percentile(values, p):
    if not values:
        return None
    ordered = sorted(values)
    rank = (len(ordered) - 1) * p / 100
    low = int(rank)
    high = min(low + 1, len(ordered) - 1)
    return ordered[low] + (ordered[high] - ordered[low]) * (rank - low)


def summarize(values):
    return {
        'n': len(values),
        'p50': percentile(values, 50),
        'p95': percentile(values, 95),
        'max': max(values) if values else None,
    }


def parse_key_timings(lines):
    found = []
    for line in lines:
        m = KEY_LINE.search(line.rstrip())
        if m:
            found.append(KeyTiming(m[1], m[2], float(m[3]), float(m[4])))
    return found


def parse_menu_timings(lines):
    found = []
    for line in lines:
        m = MENU_LINE.search(line)
        if m:
            found.append(MenuTiming(m[1], m[2] == 'warm', float(m[3]), float(m[4])))
    return found


def key_hops(timings, panes=None):
    chosen = [t for t in timings if panes is None or t.pane in panes]
    return {
        'app': [t.output_ms for t in chosen],
        'render': [round(t.frame_ms - t.output_ms, 3) for t in chosen],
        'daemon': [t.frame_ms for t in chosen],
    }


def terminal_queries(data):
    found = []
    for name, pattern in QUERIES:
        if pattern.search(data):
            found.append((data.index(pattern.search(data)[0]), name))
    return [name for _, name in sorted(found)]


def answer(name, cols, rows):
    return {
        'DA1': b'\x1b[?62;22c',
        'DA2': b'\x1b[>1;10;0c',
        'CPR': b'\x1b[1;1R',
        'size-18t': b'\x1b[8;%d;%dt' % (rows, cols),
        'size-14t': b'\x1b[4;%d;%dt' % (rows * 16, cols * 8),
        'size-16t': b'\x1b[6;16;8t',
        'kitty-keys': b'\x1b[?0u',
        'XTVERSION': b'\x1bP>|keylatency\x1b\\',
        'OSC10': b'\x1b]10;rgb:ffff/ffff/ffff\x1b\\',
        'OSC11': b'\x1b]11;rgb:0000/0000/0000\x1b\\',
    }.get(name, b'')


def fmt(value):
    return '-' if value is None else f'{value:.1f}'


def row(label, values):
    s = summarize(values)
    return f'| {label} | {s["n"]} | {fmt(s["p50"])} | {fmt(s["p95"])} | {fmt(s["max"])} |'


class Terminal:
    """A pseudo terminal around one process, recording when every output chunk arrived."""

    def __init__(self, argv, env, cols=120, rows=35, answers='all', cwd=None):
        import pty
        import fcntl
        import termios
        self.cols, self.rows, self.answers = cols, rows, answers
        self.chunks = []
        self.queries = []
        self.beats = []
        self.lock = threading.Lock()
        self.pid, self.fd = pty.fork()
        if self.pid == 0:
            fcntl.ioctl(0, termios.TIOCSWINSZ, struct.pack('HHHH', rows, cols, 0, 0))
            if cwd:
                os.chdir(cwd)
            os.execvpe(argv[0], argv, env)
        fcntl.ioctl(self.fd, termios.TIOCSWINSZ, struct.pack('HHHH', rows, cols, 0, 0))
        self.reader = threading.Thread(target=self._read, daemon=True)
        self.reader.start()

    def _read(self):
        while True:
            try:
                data = os.read(self.fd, 65536)
            except OSError:
                return
            if not data:
                return
            now = time.monotonic()
            with self.lock:
                if is_heartbeat(data):
                    self.beats.append(now)
                else:
                    self.chunks.append((now, data))
            for name in terminal_queries(data):
                self.queries.append((now, name))
                if self.answers == 'all' or (self.answers == 'fleetd' and name == 'size-18t'):
                    reply = answer(name, self.cols, self.rows)
                    if reply:
                        self.write(reply)

    def write(self, data):
        os.write(self.fd, data)

    def last_output(self):
        with self.lock:
            return self.chunks[-1][0] if self.chunks else 0.0

    def text_since(self, t):
        with self.lock:
            return b''.join(d for at, d in self.chunks if at >= t)

    def wait_quiet(self, quiet=0.25, timeout=5.0):
        deadline = time.monotonic() + timeout
        while time.monotonic() < deadline:
            if time.monotonic() - self.last_output() >= quiet:
                return True
            time.sleep(0.005)
        return False

    def wait_for(self, needle, since, timeout=15.0):
        deadline = time.monotonic() + timeout
        while time.monotonic() < deadline:
            if needle in self.text_since(since):
                return True
            time.sleep(0.02)
        return False

    def press(self, key, timeout=2.0, settle=0.1):
        """Write one key; return (first output, last output) in ms after it, or None if nothing came."""
        self.wait_quiet()
        time.sleep(random.uniform(0, 0.06))
        start = time.monotonic()
        self.write(key)
        deadline = start + timeout
        first = None
        while time.monotonic() < deadline:
            with self.lock:
                after = [at for at, _ in self.chunks if at >= start]
            if after:
                first = after[0]
                break
            time.sleep(0.0005)
        if first is None:
            return None
        self.wait_quiet(settle, timeout)
        return (first - start) * 1000, (self.last_output() - start) * 1000

    def hold(self, key, count=25, interval=0.033, timeout=5.0):
        """Write a key `count` times at a key-repeat rate; return ms from the last press until quiet."""
        self.wait_quiet()
        for _ in range(count):
            self.write(key)
            time.sleep(interval)
        last = time.monotonic() - interval
        self.wait_quiet(0.15, timeout)
        return max(0.0, (self.last_output() - last) * 1000)

    def close(self):
        try:
            os.kill(self.pid, signal.SIGTERM)
        except ProcessLookupError:
            pass
        try:
            os.waitpid(self.pid, 0)
        except ChildProcessError:
            pass


DOWN, UP, PREFIX, ESC = b'\x1b[B', b'\x1b[A', b'\x13', b'\x1b'


class Fleet:
    def __init__(self, binary, root, busy, driver=None):
        self.root = root
        shutil.rmtree(root, ignore_errors=True)
        for d in ['bin', 'cfg/projects', 'demo1', 'demo2', 'stand']:
            os.makedirs(os.path.join(root, d), exist_ok=True)
        shutil.copy(binary, os.path.join(root, 'bin/fleet'))
        os.chmod(os.path.join(root, 'bin/fleet'), 0o755)
        for p in ['demo1', 'demo2']:
            subprocess.run(['git', '-C', os.path.join(root, p), 'init', '-q'], check=True)
            with open(os.path.join(root, 'cfg/projects', p + '.json'), 'w') as f:
                json.dump({'version': 1, 'name': p, 'root': os.path.join(root, p)}, f)
        for s in ['claude', 'nvim']:
            path = os.path.join(root, 'stand', s)
            with open(path, 'w') as f:
                f.write('#!/bin/sh\necho "%s-standin in $(basename "$PWD")"\nexec sh\n' % s)
            os.chmod(path, 0o755)
        self.endpoint = os.path.join(root, 'fleetd.sock')
        self.log = os.path.join(root, 'cfg', 'fleet.log')
        self.env = dict(os.environ)
        self.env.update({
            'FLEET_CONFIG_HOME': os.path.join(root, 'cfg'),
            'FLEET_MUX': 'embedded',
            'FLEET_ENDPOINT': self.endpoint,
            'PATH': f'{root}/stand:{root}/bin:' + os.environ['PATH'],
            'TERM': 'xterm-256color',
            'FLEET_KEY_TIMING': '1',
            'FLEET_MENU_TIMING': '1',
        })
        if driver:
            self.env['FLEET_TUI_DRIVER'] = driver
        for v in ['TMUX', 'FLEET_PANE', 'FLEET_FLOAT', 'FLEET_FRAMED']:
            self.env.pop(v, None)
        self.busy = busy

    def control(self, op, **fields):
        sock = socket.socket(socket.AF_UNIX, socket.SOCK_STREAM)
        sock.connect(self.endpoint)

        def send(kind, obj):
            body = json.dumps(obj).encode()
            sock.sendall(struct.pack('<I', len(body) + 1) + bytes([kind]) + body)

        def receive():
            head = b''
            while len(head) < 5:
                head += sock.recv(5 - len(head))
            length = struct.unpack('<I', head[:4])[0] - 1
            body = b''
            while len(body) < length:
                body += sock.recv(length - len(body))
            return json.loads(body)

        send(1, {'version': 1, 'role': 'control', 'os': 'test'})
        receive()
        send(30, dict({'id': 1, 'op': op}, **fields))
        reply = receive()
        sock.close()
        return reply

    def panes(self):
        return self.control('list-panes').get('panes') or []

    def log_lines(self):
        try:
            with open(self.log, errors='replace') as f:
                return f.readlines()
        except FileNotFoundError:
            return []

    def stop(self):
        subprocess.run(['pkill', '-f', f'^{self.root}/bin/fleet'], check=False)


def float_pane(fleet):
    floats = [p for p in fleet.panes() if p.get('tab') == 'float' and not p.get('parked')]
    return {p['id'] for p in floats}


def measure(term, keys, presses):
    firsts, lasts, misses = [], [], 0
    for i in range(presses):
        got = term.press(keys[i % len(keys)])
        if got is None:
            misses += 1
        else:
            firsts.append(got[0])
            lasts.append(got[1])
    return {'first': firsts, 'last': lasts, 'misses': misses}


def run(args):
    root = args.root
    fleet = Fleet(args.binary, root, args.busy, args.driver)
    results = []
    notes = []
    log_mark = 0
    watcher = None
    if shutil.which('inotifywait'):
        watcher = subprocess.Popen(
            ['inotifywait', '-m', '-q', '-r', '--format', '%T %w%f %e', '--timefmt', '%s',
             os.path.join(root, 'cfg')],
            stdout=subprocess.PIPE, stderr=subprocess.DEVNULL, text=True)
    term = Terminal([os.path.join(root, 'bin/fleet')], fleet.env, args.cols, args.rows, answers='all')
    try:
        t = time.monotonic()
        if not term.wait_for(b'demo1', t, 20):
            sys.exit('fleet did not show the project picker')
        time.sleep(0.5)
        t = time.monotonic()
        term.write(b'\r')
        if not term.wait_for(b'Agents', t, 30):
            notes.append('dashboard text not seen within 30 s')
        term.wait_quiet(1.0, 20)

        for i in range(args.busy):
            fleet.control('spawn', session='demo1', newWindow=True, cwd=root,
                          args=['sh', '-c', 'while :; do date; sleep 0.01; done'])
        for i in range(args.dashes):
            fleet.control('spawn', session='demo2', newWindow=True, cwd=os.path.join(root, 'demo2'),
                          args=[os.path.join(root, 'bin/fleet'), 'dash', '--project', 'demo2'])
        if args.busy or args.dashes:
            time.sleep(3)

        def screen(name, opener, keys, closer):
            nonlocal log_mark
            log_mark = len(fleet.log_lines())
            opened = time.monotonic()
            for k in opener:
                term.write(k)
                time.sleep(args.step)
            term.wait_quiet(0.5, 10)
            panes = float_pane(fleet) if name != 'dashboard' else None
            m = measure(term, keys, args.presses)
            shown = term.text_since(opened)
            with open(os.path.join(root, f'screen-{name}.txt'), 'w') as f:
                f.write(re.sub(r'\x1b\[[0-9;?<>=]*[ -/]*[@-~]|\x1b[\]P][^\x07\x1b]*(\x07|\x1b\\)?|\x1b.', ' ',
                               shown.decode('utf-8', 'replace')))
            held = [term.hold(keys[0], args.hold) for _ in range(3)]
            lines = fleet.log_lines()[log_mark:]
            timings = parse_key_timings(lines)
            if panes is None:
                focused = [p for p in fleet.panes() if p.get('active')]
                panes = {p['id'] for p in focused} or None
            hops = key_hops(timings, panes)
            results.append((name, m, held, hops, time.monotonic() - opened))
            for k in closer:
                term.write(k)
                time.sleep(args.step)
            term.wait_quiet(0.5, 10)

        screen('menu', [PREFIX, b' '], [DOWN, UP], [ESC])
        screen('picker', [PREFIX, b' ', b'p', b'l'], [DOWN, UP], [ESC, ESC, ESC])
        screen('dashboard', [PREFIX, b' ', b'd'], [b'l', b'h'], [])

        opens = parse_menu_timings(fleet.log_lines())
    finally:
        term.close()

    direct = []
    if args.direct:
        for answers in ['all', 'fleetd']:
            menu = Terminal([os.path.join(root, 'bin/fleet'), 'menu'], fleet.env, 80, 24, answers=answers)
            try:
                menu.wait_for(b'fleet', 0, 20)
                menu.wait_quiet(1.0, 20)
                beats_from = len(menu.beats)
                time.sleep(2)
                beat_rate = (len(menu.beats) - beats_from) / 2
                m = measure(menu, [DOWN, UP], args.presses)
                held = [menu.hold(DOWN, args.hold) for _ in range(3)]
                asked = sorted({n for _, n in menu.queries})
                direct.append((answers, m, held, asked, beat_rate))
            finally:
                menu.close()
    fleet.stop()

    events = []
    if watcher is not None:
        watcher.terminate()
        events = [line for line in watcher.stdout.read().splitlines()]

    print(report(args, results, opens, direct, events, notes))


def report(args, results, opens, direct, events, notes):
    out = [f'## key -> frame, {args.presses} presses per screen, {args.cols}x{args.rows}, busy panes: {args.busy}, '
           f'extra dashboards: {args.dashes}, driver: {args.driver or "default"}', '']
    out.append('End to end (harness pty: key written -> first byte / last byte of the redraw), ms')
    out.append('')
    out.append('| screen / measure | n | p50 | p95 | max |')
    out.append('|---|---|---|---|---|')
    for name, m, held, hops, _ in results:
        out.append(row(f'{name}: key -> first byte', m['first']))
        out.append(row(f'{name}: key -> frame done', m['last']))
        out.append(row(f'{name}: held key ({args.hold} @30 Hz), last press -> quiet', held))
        if m['misses']:
            out.append(f'| {name}: presses with no redraw within 2 s | {m["misses"]} | | | |')
    out.append('')
    out.append('Inside fleetd (FLEET_KEY_TIMING), ms: app = key reaches fleetd -> pane answers; '
               'render = pane answers -> frame composed')
    out.append('')
    out.append('| screen / hop | n | p50 | p95 | max |')
    out.append('|---|---|---|---|---|')
    for name, _, _, hops, _ in results:
        out.append(row(f'{name}: app (Terminal.Gui + UI thread + pty)', hops['app']))
        out.append(row(f'{name}: render (fleetd loop)', hops['render']))
        out.append(row(f'{name}: total in fleetd', hops['daemon']))
    if opens:
        out.append('')
        out.append('| menu opens | n | p50 | p95 | max |')
        out.append('|---|---|---|---|---|')
        out.append(row('first frame', [o.first_frame_ms for o in opens]))
    if direct:
        out.append('')
        out.append('`fleet menu` straight in the harness pty, no fleetd, ms')
        out.append('')
        out.append('| queries answered / measure | n | p50 | p95 | max |')
        out.append('|---|---|---|---|---|')
        for answers, m, held, asked, _ in direct:
            out.append(row(f'{answers}: key -> first byte', m['first']))
            out.append(row(f'{answers}: key -> frame done', m['last']))
            out.append(row(f'{answers}: held key, last press -> quiet', held))
        out.append('')
        out.append('terminal queries `fleet menu` wrote: ' + ', '.join(direct[0][3] or ['none']))
        out.append(f'idle cursor-only heartbeat writes from `fleet menu`: {direct[0][4]:.0f}/s')
    theme = [e for e in events if 'theme' in e.lower()]
    out.append('')
    out.append(f'inotify on the config dir during the run: {len(events)} events, {len(theme)} on theme files')
    for n in notes:
        out.append(f'note: {n}')
    return '\n'.join(out)


def log_command(args):
    with open(args.log, errors='replace') as f:
        lines = f.readlines()
    panes = set(args.panes.split(',')) if args.panes else None
    hops = key_hops(parse_key_timings(lines), panes)
    print('| hop | n | p50 | p95 | max |')
    print('|---|---|---|---|---|')
    for name in ['app', 'render', 'daemon']:
        print(row(name, hops[name]))


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    sub = parser.add_subparsers(dest='command', required=True)
    r = sub.add_parser('run')
    r.add_argument('binary')
    r.add_argument('--root', default='/tmp/fleet-keylatency')
    r.add_argument('--presses', type=int, default=40)
    r.add_argument('--hold', type=int, default=25)
    r.add_argument('--busy', type=int, default=0)
    r.add_argument('--direct', action='store_true')
    r.add_argument('--dashes', type=int, default=0, help='extra hidden `fleet dash` panes (idle Terminal.Gui apps)')
    r.add_argument('--driver', help='force a Terminal.Gui driver (ansi, dotnet) via FLEET_TUI_DRIVER')
    r.add_argument('--cols', type=int, default=120)
    r.add_argument('--rows', type=int, default=35)
    r.add_argument('--step', type=float, default=0.8, help='seconds between keys that open a screen')
    r.set_defaults(func=run)
    lg = sub.add_parser('log')
    lg.add_argument('log')
    lg.add_argument('--panes')
    lg.set_defaults(func=log_command)
    args = parser.parse_args()
    args.func(args)


if __name__ == '__main__':
    main()
