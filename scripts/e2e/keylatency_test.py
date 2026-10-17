#!/usr/bin/env python3
"""Unit tests for the pure parts of keylatency.py: python3 scripts/e2e/keylatency_test.py"""
import os
import sys
import unittest

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import keylatency as kl  # noqa: E402


class Percentiles(unittest.TestCase):
    def test_interpolates_between_ranks(self):
        self.assertEqual(kl.percentile([1, 2, 3, 4], 50), 2.5)
        self.assertEqual(kl.percentile([10, 20, 30, 40, 50], 50), 30)

    def test_p95_of_twenty_values_leans_to_the_top(self):
        values = list(range(1, 21))
        self.assertAlmostEqual(kl.percentile(values, 95), 19.05)

    def test_order_does_not_matter(self):
        self.assertEqual(kl.percentile([4, 1, 3, 2], 50), 2.5)

    def test_one_value_is_every_percentile(self):
        self.assertEqual(kl.percentile([7], 95), 7)

    def test_no_values_has_no_percentile(self):
        self.assertIsNone(kl.percentile([], 50))

    def test_summary_counts_and_ranks(self):
        s = kl.summarize([10, 20, 30, 40])
        self.assertEqual(s['n'], 4)
        self.assertEqual(s['p50'], 25)
        self.assertEqual(s['max'], 40)

    def test_summary_of_nothing_is_empty(self):
        self.assertEqual(kl.summarize([]), {'n': 0, 'p50': None, 'p95': None, 'max': None})


class LogParsing(unittest.TestCase):
    LOG = [
        '2026-10-17 10:00:00.000 started p3: fleet in /x at 80x20',
        '2026-10-17 10:00:01.000 key timing c1 p3: output 30.4 ms, frame 42 ms',
        '2026-10-17 10:00:01.100 key timing c1 p3: output 5 ms, frame 9.5 ms',
        '2026-10-17 10:00:02.000 menu timing c1: warm, first frame 12 ms, settle 0 ms',
        'key timing broken line',
    ]

    def test_reads_key_timings_and_skips_other_lines(self):
        self.assertEqual(kl.parse_key_timings(self.LOG), [
            kl.KeyTiming('c1', 'p3', 30.4, 42.0),
            kl.KeyTiming('c1', 'p3', 5.0, 9.5),
        ])

    def test_reads_menu_open_timings(self):
        self.assertEqual(kl.parse_menu_timings(self.LOG), [kl.MenuTiming('c1', True, 12.0, 0.0)])

    def test_splits_daemon_time_into_app_and_render_hops(self):
        hops = kl.key_hops([kl.KeyTiming('c1', 'p3', 30, 42), kl.KeyTiming('c1', 'p3', 10, 14)])
        self.assertEqual(hops['app'], [30, 10])
        self.assertEqual(hops['render'], [12, 4])
        self.assertEqual(hops['daemon'], [42, 14])

    def test_only_counts_the_panes_asked_for(self):
        timings = [kl.KeyTiming('c1', 'p3', 30, 42), kl.KeyTiming('c1', 'p9', 1, 2)]
        self.assertEqual(kl.key_hops(timings, panes={'p9'})['daemon'], [2])


class TerminalQueries(unittest.TestCase):
    def test_names_queries_an_app_writes(self):
        out = b'\x1b[?1049h\x1b[c\x1b[>c\x1b[6n\x1b[18t\x1b[?u\x1b]11;?\x1b\\'
        self.assertEqual(kl.terminal_queries(out), ['DA1', 'DA2', 'CPR', 'size-18t', 'kitty-keys', 'OSC11'])

    def test_plain_drawing_has_no_queries(self):
        self.assertEqual(kl.terminal_queries(b'\x1b[2J\x1b[1;1Hhello\x1b[0m'), [])

    def test_answers_each_query_it_knows(self):
        self.assertEqual(kl.answer('size-18t', 100, 30), b'\x1b[8;30;100t')
        self.assertEqual(kl.answer('CPR', 100, 30), b'\x1b[1;1R')
        self.assertTrue(kl.answer('DA1', 100, 30).startswith(b'\x1b[?'))


class Heartbeats(unittest.TestCase):
    def test_cursor_only_writes_are_a_heartbeat(self):
        self.assertTrue(kl.is_heartbeat(b'\x1b[?25l\x1b[1;1H'))
        self.assertTrue(kl.is_heartbeat(b'\x1b[1;1H'))
        self.assertTrue(kl.is_heartbeat(b'\x1b[?25l\x1b[12;4H\x1b[?25h'))

    def test_drawing_is_not_a_heartbeat(self):
        self.assertFalse(kl.is_heartbeat(b'\x1b[3;1H> demo2'))
        self.assertFalse(kl.is_heartbeat(b''))


if __name__ == '__main__':
    unittest.main()
