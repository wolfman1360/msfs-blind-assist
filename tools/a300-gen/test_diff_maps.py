import os
import sys
import unittest

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import diff_maps


def control(cid, kind='toggle', state='L:X'):
    return {'id': cid, 'key': 'A300_' + cid, 'kind': kind, 'state_var': state, 'positions': {}}


class DiffMapsTests(unittest.TestCase):
    def test_identical_maps(self):
        m = {'controls': [control('A')]}
        self.assertEqual([], diff_maps.diff(m, m))

    def test_added_removed_and_changed(self):
        old = {'controls': [control('A'), control('B')]}
        new = {'controls': [control('A', state='L:Y'), control('C', 'button')]}
        lines = diff_maps.diff(old, new)
        self.assertIn('added   C (button)', lines)
        self.assertIn('removed B (toggle)', lines)
        self.assertIn('changed A state_var: "L:X" -> "L:Y"', lines)

    def test_a_changed_action_is_reported(self):
        old = {'controls': [dict(control('A'), action='AIRCRAFT HEADING')]}
        new = {'controls': [dict(control('A'), action='HEADING')]}
        self.assertIn('changed A action: "AIRCRAFT HEADING" -> "HEADING"', diff_maps.diff(old, new))


if __name__ == '__main__':
    unittest.main()
