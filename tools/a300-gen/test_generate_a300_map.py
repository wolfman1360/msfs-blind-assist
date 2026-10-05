import json
import os
import sys
import tempfile
import unittest

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import generate_a300_map as gen
from a300_fixture import FixtureBuilder
from behavior_xml import Behavior

LOC = {
    'INI.TOOLTIPS.StormLight.TITLE': 'STORM LIGHT',
    'INI.TOOLTIPS.IRS_MODE_SELECT.TITLE': 'IRS DYSP SELECTOR',
    'INI.TOOLTIPS.IRS_MODE_SELECT.0': 'TEST',
    'INI.TOOLTIPS.IRS_MODE_SELECT.1': 'TK/GS',
    'INI.TOOLTIPS.IRS_MODE_SELECT.2': 'PPOS',
    'INI.TOOLTIPS.NOSE_TRIM.TITLE': 'RUDDER TRIM',
    'INI.TOOLTIPS.NOSE_TRIM.0': 'RIGHT',
    'INI.TOOLTIPS.NOSE_TRIM.1': 'NEUTRAL',
    'INI.TOOLTIPS.NOSE_TRIM.2': 'LEFT',
    'INI.TOOLTIPS.TCAS_ABV.TITLE': 'TCAS MODE',
    'INI.TOOLTIPS.TCAS_ABV.0': 'ABOVE',
    'INI.TOOLTIPS.TCAS_ABV.1': 'NEUTRAL',
    'INI.TOOLTIPS.TCAS_ABV.2': 'BELOW',
    'INI.TOOLTIPS.AUTO_PRESS_SELECT.TITLE': 'AUTO PRESS LIMIT',
    'INI.TOOLTIPS.ENG1_CUTOFF.TITLE': 'ENG1 MIXTURE',
    'INI.TOOLTIPS.CPT_VHF_TFR.TITLE': 'COM1 FREQUENCY',
    'INI.TOOLTIPS.ECAM_ENG.TITLE': 'ECAM ENG PAGE',
    'INI.TOOLTIPS.HEADING_KNOB.TITLE': 'HEADING KNOB',
    'INI.TOOLTIPS.HEADING_KNOB.PUSH': 'AIRCRAFT HEADING',
    'INI.TOOLTIPS.HEADING_KNOB.PULL': 'HEADING MODE',
    'INI.TOOLTIPS.VOR_CRS_CAPT.TITLE': 'VOR1 CRS',
    'INI.TOOLTIPS.RAM_AIR_COVER.TITLE': 'RAM AIR GUARD',
    'INI.TOOLTIPS.GEAR_LEVER.TITLE': 'GEAR LEVER',
    'INI.TOOLTIPS.AT_DISCO1.TITLE': 'AUTO THROTTLE DISCONNECT',
    'INI.TOOLTIPS.APU_START.TITLE': 'APU START',
    'INI.TOOLTIPS.FLAPS.TITLE': 'FLAPS LEVER',
}

CASE3 = ("p0 2 min 0 max (>O:{o}) (O:{o}) s0 l0 0 == if{{ 0 sp0 l0 (>L:{v}) g1 }} "
         "l0 1 == if{{ 1 sp0 l0 (>L:{v}) g1 }} l0 2 == if{{ 2 sp0 l0 (>L:{v}) g1 }} :1")


def tooltip_cases(name, n):
    return ' '.join("l0 %d == if{ (R:1:INI.TOOLTIPS.%s.%d) quit }" % (i, name, i) for i in range(n))


def _fixture():
    f = FixtureBuilder()
    root = f.component('A300_INTERIOR_COMPONENT')
    overhead = f.component('OVERHEAD', root)
    lights = f.component('OVERHEAD_LIGHTS', overhead)
    irs = f.component('IRS', overhead)
    pedestal = f.component('PEDESTAL', root)
    trim = f.component('TRIM', pedestal)

    def add(owner, ie_id, title, **ie):
        f.mouserect(owner, ie_id.replace('AIRLINER_', ''), '(>B:%s_Toggle)' % ie_id, title=title, entries=[ie_id])
        f.inputevent(owner, ie_id, **ie)

    add(lights, 'AIRLINER_StormLight', 'INI.TOOLTIPS.StormLight.TITLE', units='Boolean',
        set_code="p0 1 min 0 max (>O:AIRLINER_StormLight_Position) (L:INI_STORM_LIGHT_SWITCH) ! (>L:INI_STORM_LIGHT_SWITCH)",
        init="(L:INI_STORM_LIGHT_SWITCH) sp0 l0 (>O:AIRLINER_StormLight_Position)",
        tt_value="(L:INI_STORM_LIGHT_SWITCH) 0 == if{ 'OFF' } els{ 'ON' }",
        inc="(O:AIRLINER_StormLight_Position) p0 + (>B:AIRLINER_StormLight_Set)",
        dec="(O:AIRLINER_StormLight_Position) p0 - (>B:AIRLINER_StormLight_Set)")
    add(irs, 'AIRLINER_IRS_MODE_SELECT', 'INI.TOOLTIPS.IRS_MODE_SELECT.TITLE', units='numbers',
        set_code=("p0 2 min 0 max (>O:P) (O:P) s0 l0 0 == if{ 0 (>L:INI_IRS_MODE_KNOB) g1 } "
                  "l0 1 == if{ 1 (>L:INI_IRS_MODE_KNOB) g1 } l0 2 == if{ 2 (>L:INI_IRS_MODE_KNOB) g1 } :1"),
        init="(L:INI_IRS_MODE_KNOB) sp0 l0 (>O:P)",
        tt_value="(B:AIRLINER_IRS_MODE_SELECT, Number) sp0 " + tooltip_cases('IRS_MODE_SELECT', 3))
    add(trim, 'AIRLINER_NOSE_TRIM', 'INI.TOOLTIPS.NOSE_TRIM.TITLE', units='numbers',
        set_code=CASE3.format(o='O:N', v='XMLVAR_RudderTrim'),
        init="(L:XMLVAR_RudderTrim) sp0 l0 (>O:N)",
        tt_value="(B:AIRLINER_NOSE_TRIM, Number) sp0 " + tooltip_cases('NOSE_TRIM', 3))
    add(trim, 'AIRLINER_TCAS_ABV', 'INI.TOOLTIPS.TCAS_ABV.TITLE', units='numbers',
        set_code=CASE3.format(o='O:T', v='INI_tcas_mode'),
        init="(L:INI_tcas_mode) sp0 l0 (>O:T)",
        tt_value="(B:AIRLINER_TCAS_ABV, Number) sp0 " + tooltip_cases('TCAS_ABV', 3))
    add(overhead, 'AIRLINER_AUTO_PRESS_SELECT', 'INI.TOOLTIPS.AUTO_PRESS_SELECT.TITLE', units='percent',
        set_code="p0 0 max 100 min s0 (>O:A) l0 10 / (>L:INI_CABIN_AUTO_PRESS_ACTUAL)",
        init="(L:INI_CABIN_AUTO_PRESS_ACTUAL) 10 * (>O:A)")
    add(pedestal, 'AIRLINER_ENG1_CUTOFF', 'INI.TOOLTIPS.ENG1_CUTOFF.TITLE', units='Boolean',
        set_code="p0 1 min 0 max (>O:E) 1 (>L:INI_ENG1_MIXTURE_COMMAND) 1 (>O:_ButtonAnimVar)",
        tt_value="(L:INI_MIXTURE_RATIO1_HANDLE) 1 == 0 == if{ 'OFF' } els{ 'ON' }")
    add(pedestal, 'AIRLINER_CPT_VHF_TFR', 'INI.TOOLTIPS.CPT_VHF_TFR.TITLE', units='Boolean',
        set_code="p0 1 min 0 max (>O:C) 1 (>L:INI_CPT_VHF_TRANSFER_SWITCH_COMMAND) 1 (>O:_ButtonAnimVar)",
        tt_value="(L:INI_CPT_VHF_TRANSFER_SWITCH, bool) 0 == if{ 'OFF' } els{ 'ON' }")
    add(pedestal, 'AIRLINER_ECAM_ENG', 'INI.TOOLTIPS.ECAM_ENG.TITLE', units='Boolean',
        set_code="p0 1 min 0 max (>O:G) p0 2 == if{ 1 (>L:PUSH_ECAM_ENG) } els{ 0 (>L:PUSH_ECAM_ENG) }")
    add(pedestal, 'AIRLINER_HEADING_KNOB', 'INI.TOOLTIPS.HEADING_KNOB.TITLE', units='',
        set_code=("p0 p0 0 > if{ 1 (>L:INI_HEADING_DIAL_UP_COMMAND) } els{ 1 (>L:INI_HEADING_DIAL_DN_COMMAND) } "
                  "(O:_KnobAnimVar) 10 p0 * + dnor (>O:_KnobAnimVar)"),
        tt_value="(L:INI_HEADING_DIAL) flr '%ddeg' (F:Format)",
        inc="p0 (>B:AIRLINER_HEADING_KNOB_Set)", dec="p0 -1 * (>B:AIRLINER_HEADING_KNOB_Set)")
    for part, cmd in (('PUSH', 'INI_FCU_SYNC_HEADING_BUTTON'), ('PULL', 'INI_FCU_SELECTED_HEADING_BUTTON')):
        add(pedestal, 'AIRLINER_HEADING_KNOB_' + part, 'INI.TOOLTIPS.HEADING_KNOB.TITLE', units='',
            set_code=("p0 if{ 1 (>L:%s) } 1 (>O:IsPushed) (E:SIMULATION TIME, second) (>O:_LastPushTime) "
                      "(O:IsPulled) if{ 0 (>O:IsPulled) }" % cmd),
            tt_value='INI.TOOLTIPS.HEADING_KNOB.PUSH_VALUE',
            inc='1 (>B:AIRLINER_HEADING_KNOB_%s_Set)' % part.capitalize(),
            dec='0 (>B:AIRLINER_HEADING_KNOB_%s_Set)' % part.capitalize())
    add(pedestal, 'AIRLINER_VOR_CRS_CAPT', 'INI.TOOLTIPS.VOR_CRS_CAPT.TITLE', units='',
        set_code="p0 p0 0 > if{ (>K:VOR1_OBI_INC) } els{ (>K:VOR1_OBI_DEC) } (O:_KnobAnimVar) 10 p0 * + dnor (>O:_KnobAnimVar)",
        tt_value="(A:NAV OBS:1, Degrees) flr '%dDeg' (F:Format)",
        inc="p0 (>B:AIRLINER_VOR_CRS_CAPT_Set)", dec="p0 -1 * (>B:AIRLINER_VOR_CRS_CAPT_Set)")
    add(overhead, 'AIRLINER_RAM_AIR_COVER', 'INI.TOOLTIPS.RAM_AIR_COVER.TITLE', units='Boolean',
        set_code="p0 1 min 0 max (>O:AIRLINER_RAM_AIR_Cover_Position) (O:AIRLINER_RAM_AIR_Cover_Position) s0 l0 0 == if{ 0 (>O:AIRLINER_RAM_AIR_Cover_Position) g1 } :1")
    add(pedestal, 'AIRLINER_GEAR_LEVER', 'INI.TOOLTIPS.GEAR_LEVER.TITLE', units='Boolean',
        set_code="p0 1 min 0 max (>O:L) 1 (>K:GEAR_TOGGLE) 1 (>O:_ButtonAnimVar)", tt_value="'PRESS'")
    add(pedestal, 'AIRLINER_AT_DISCO1', 'INI.TOOLTIPS.AT_DISCO1.TITLE', units='Boolean',
        set_code="p0 1 min 0 max (>O:D) 1 (>K:AUTO_THROTTLE_DISCONNECT) 1 (>O:_ButtonAnimVar)")
    add(overhead, 'AIRLINER_APU_START', 'INI.TOOLTIPS.APU_START.TITLE', units='Boolean',
        set_code="p0 1 min 0 max (>O:S) (L:INI_apu_master_switch, Bool) if{ 1 (>L:INI_APU_START_BUTTON) } 1 (>O:_ButtonAnimVar)")
    add(pedestal, 'AIRLINER_FLAPS', 'INI.TOOLTIPS.FLAPS.TITLE', units='percent',
        set_code="p0 0 max 16384 min (>K:FLAPS_SET) (A:FLAPS HANDLE PERCENT, percent) (>O:F)")
    # A part-marker tag that begins with a digit, as the A300 file has.
    return f.build().replace('<Components Count=', '<0FAB/><Components Count=', 1)


def _map():
    path = os.path.join(tempfile.mkdtemp(), 'A300_Interior.behavior.xml')
    with open(path, 'w', encoding='utf-8') as fh:
        fh.write(_fixture())
    return gen.build_map(gen.read_behavior(path), LOC, '1.0.11')


def _by_id():
    return {c['id']: c for c in _map()['controls']}


class GenerateA300MapTests(unittest.TestCase):
    def test_reads_a_file_with_digit_tags(self):
        self.assertEqual(17, len(_map()['controls']))

    def test_a_knob_push_and_pull_carry_the_knobs_own_words(self):
        self.assertEqual('AIRCRAFT HEADING', _by_id()['AIRLINER_HEADING_KNOB_PUSH']['action'])
        self.assertEqual('HEADING MODE', _by_id()['AIRLINER_HEADING_KNOB_PULL']['action'])

    def test_other_controls_carry_no_action(self):
        self.assertNotIn('action', _by_id()['AIRLINER_StormLight'])
        self.assertNotIn('action', _by_id()['AIRLINER_HEADING_KNOB'])

    def test_a_push_with_no_tooltip_words_has_no_action(self):
        self.assertIsNone(gen.knob_action(LOC, 'INI.TOOLTIPS.CWS_PUSH.TITLE', 'AIRLINER_CWS_PUSH'))
        self.assertIsNone(gen.knob_action(LOC, '', 'AIRLINER_HEADING_KNOB_PUSH'))

    def test_toggle(self):
        c = _by_id()['AIRLINER_StormLight']
        self.assertEqual('toggle', c['kind'])
        self.assertEqual('A300_STORMLIGHT', c['key'])
        self.assertEqual(('OVERHEAD', 'OVERHEAD_LIGHTS'), (c['area'], c['panel']))
        self.assertEqual('STORM LIGHT', c['title'])
        self.assertEqual('AIRLINER_StormLight_Set', c['event'])
        self.assertEqual('L:INI_STORM_LIGHT_SWITCH', c['state_var'])
        self.assertEqual({'0': 'OFF', '1': 'ON'}, c['positions'])

    def test_selector_takes_words_from_the_tooltip_file(self):
        c = _by_id()['AIRLINER_IRS_MODE_SELECT']
        self.assertEqual('selector', c['kind'])
        self.assertEqual('L:INI_IRS_MODE_KNOB', c['state_var'])
        self.assertEqual({'0': 'TEST', '1': 'TK/GS', '2': 'PPOS'}, c['positions'])
        self.assertIsNone(c['values'])
        self.assertEqual('AIRLINER_IRS_MODE_SELECT_Set', c['event'])

    def test_a_neutral_middle_is_a_spring_unless_listed(self):
        trim = _by_id()['AIRLINER_NOSE_TRIM']
        self.assertEqual('spring', trim['kind'])
        self.assertEqual(1, trim['rest'])
        self.assertEqual('selector', _by_id()['AIRLINER_TCAS_ABV']['kind'])

    def test_knob_records_its_scale(self):
        c = _by_id()['AIRLINER_AUTO_PRESS_SELECT']
        self.assertEqual('knob', c['kind'])
        self.assertEqual('L:INI_CABIN_AUTO_PRESS_ACTUAL', c['state_var'])
        self.assertAlmostEqual(0.1, c['scale'])

    def test_command_reads_the_variable_the_tooltip_shows(self):
        c = _by_id()['AIRLINER_ENG1_CUTOFF']
        self.assertEqual('command', c['kind'])
        self.assertEqual('L:INI_MIXTURE_RATIO1_HANDLE', c['state_var'])
        self.assertEqual({'0': 'OFF', '1': 'ON'}, c['positions'])

    def test_a_command_whose_state_is_its_own_animation_is_a_button(self):
        c = _by_id()['AIRLINER_CPT_VHF_TFR']
        self.assertEqual('button', c['kind'])
        self.assertEqual(1, c['press'])

    def test_hold(self):
        c = _by_id()['AIRLINER_ECAM_ENG']
        self.assertEqual('hold', c['kind'])
        self.assertEqual(2, c['press'])

    def test_encoders_name_their_display(self):
        self.assertEqual(('encoder', 'L:INI_HEADING_DIAL'),
                         (_by_id()['AIRLINER_HEADING_KNOB']['kind'], _by_id()['AIRLINER_HEADING_KNOB']['state_var']))
        vor = _by_id()['AIRLINER_VOR_CRS_CAPT']
        self.assertEqual(('encoder', 'A:NAV OBS:1', 'Degrees'), (vor['kind'], vor['state_var'], vor['state_unit']))

    def test_cover(self):
        self.assertEqual('cover', _by_id()['AIRLINER_RAM_AIR_COVER']['kind'])

    def test_stock_toggle_commands_read_the_stock_variable(self):
        c = _by_id()['AIRLINER_GEAR_LEVER']
        self.assertEqual('command', c['kind'])
        self.assertEqual(('A:GEAR HANDLE POSITION', 'Bool'), (c['state_var'], c['state_unit']))
        self.assertEqual({'0': 'UP', '1': 'DOWN'}, c['positions'])

    def test_other_k_event_buttons(self):
        self.assertEqual('button', _by_id()['AIRLINER_AT_DISCO1']['kind'])
        self.assertEqual('button', _by_id()['AIRLINER_APU_START']['kind'])

    def test_a_k_event_lever_is_left_to_the_app(self):
        c = _by_id()['AIRLINER_FLAPS']
        self.assertEqual('none', c['kind'])
        self.assertTrue(c['note'])

    def test_output_is_deterministic(self):
        self.assertEqual(gen.to_json(_map()), gen.to_json(_map()))
        self.assertEqual('1.0.11', json.loads(gen.to_json(_map()))['package_version'])


if __name__ == '__main__':
    unittest.main()
