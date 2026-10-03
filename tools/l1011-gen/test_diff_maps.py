import os
import sys
import unittest

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import diff_maps


def control(cid, kind='switch', state='L:X'):
    return {'id': cid, 'kind': kind, 'state_var': state, 'positions': {}, 'transitions': {}}


class DiffMapsTests(unittest.TestCase):
    def test_identical_maps(self):
        m = {'controls': [control('A')], 'breakers': [], 'lamps': ['L1']}
        self.assertEqual([], diff_maps.diff(m, m))

    def test_added_removed_and_changed(self):
        old = {'controls': [control('A'), control('B')], 'breakers': [], 'lamps': ['L1']}
        new = {'controls': [control('A', state='L:Y'), control('C', 'button')], 'breakers': [], 'lamps': ['L2']}
        lines = diff_maps.diff(old, new)
        self.assertIn('added   C (button)', lines)
        self.assertIn('removed B (switch)', lines)
        self.assertIn('changed A state_var: "L:X" -> "L:Y"', lines)
        self.assertIn('lamps added: L2', lines)
        self.assertIn('lamps removed: L1', lines)


if __name__ == '__main__':
    unittest.main()
