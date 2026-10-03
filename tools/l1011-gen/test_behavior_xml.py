import os
import sys
import unittest

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from behavior_xml import Behavior
from l1011_fixture import FixtureBuilder


def _battery_fixture():
    f = FixtureBuilder()
    root = f.component('L1011_SYSTEMS_BEHAVIORS')
    area = f.component('ENGINEER_PANEL', root)
    panel = f.component('ELECTRICAL_PANEL', area)
    node = f.component('TOGGLE_BATTERY', panel)
    f.mouserect(node, 'TOGGLE_BATTERY',
                "(L:TOGGLE_Battery) ! (>L:TOGGLE_Battery) (>H:ELECTRICAL)",
                title='L1011.TOOLTIPS.TOGGLE_Battery.TITLE',
                entries=['INSTRUMENT_TOGGLE_Battery_IE_ID'])
    f.inputevent(node, 'INSTRUMENT_TOGGLE_BATTERY_IE_ID',
                 set_code="p0 1 min 0 max (>O:X_Position) l0 0 == if{ 0 (>L:TOGGLE_Battery) g1 } :1",
                 tt_value="(L:TOGGLE_Battery) 1 == if{ (R:1:L1011.TOOLTIPS.ACTION.ON) } els{ (R:1:L1011.TOOLTIPS.ACTION.OFF) }",
                 units='Boolean', tt_desc='L1011.TOOLTIPS.TOGGLE_Battery.ACTION')
    f.material("(L:AC_ESS_FAIL_LIGHT) 1 ==")
    return f.build()


class BehaviorXmlTests(unittest.TestCase):
    def test_components_and_paths(self):
        b = Behavior(_battery_fixture())
        self.assertEqual(['L1011_SYSTEMS_BEHAVIORS', 'ENGINEER_PANEL', 'ELECTRICAL_PANEL', 'TOGGLE_BATTERY'],
                         b.path(3))

    def test_mouserect_code_title_and_entry(self):
        b = Behavior(_battery_fixture())
        mr = b.mouserects[0]
        self.assertEqual(3, mr['owner'])
        self.assertEqual('TOGGLE_BATTERY', mr['highlight'])
        self.assertEqual("(L:TOGGLE_Battery) ! (>L:TOGGLE_Battery) (>H:ELECTRICAL)", mr['codes']['IMDefault'])
        self.assertEqual(['L1011.TOOLTIPS.TOGGLE_Battery.TITLE'], mr['titles'])
        self.assertEqual(['INSTRUMENT_TOGGLE_Battery_IE_ID'], mr['entries'])

    def test_inputevent_codes_are_unescaped(self):
        b = Behavior(_battery_fixture())
        ie = b.inputevents[0]
        self.assertEqual('INSTRUMENT_TOGGLE_BATTERY_IE_ID', ie['id'])
        self.assertEqual('Boolean', ie['units'])
        self.assertIn('(>L:TOGGLE_Battery)', ie['set_code'])
        self.assertIn('(R:1:L1011.TOOLTIPS.ACTION.ON)', ie['tt_value'])
        self.assertEqual('L1011.TOOLTIPS.TOGGLE_Battery.ACTION', ie['tt_desc'])

    def test_material_codes(self):
        b = Behavior(_battery_fixture())
        self.assertEqual(["(L:AC_ESS_FAIL_LIGHT) 1 =="], b.material_codes)

    def test_input_event_lookup_ignores_case(self):
        b = Behavior(_battery_fixture())
        self.assertEqual('INSTRUMENT_TOGGLE_BATTERY_IE_ID', b.inputevent('instrument_toggle_battery_ie_id')['id'])
        self.assertIsNone(b.inputevent('NOPE'))


if __name__ == '__main__':
    unittest.main()
