import json
import os
import sys
import unittest

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from behavior_xml import Behavior
from l1011_fixture import FixtureBuilder
import generate_l1011_map as gen

LOC = {
    'L1011.TOOLTIPS.TOGGLE_Battery.TITLE': 'BATTERY SWITCH',
    'L1011.TOOLTIPS.ACTION.ON': 'ON',
    'L1011.TOOLTIPS.ACTION.OFF': 'OFF',
    'L1011.TOOLTIPS.SWITCH_APU_START.TITLE': 'APU START BUTTON',
    'L1011.TOOLTIPS.ROTARY_TCAS_MODE.TITLE': 'TRANSPONDER MODE SELECTOR',
    'L1011.TOOLTIPS.ROTARY_TCAS_MODE.0': 'TEST',
    'L1011.TOOLTIPS.ROTARY_TCAS_MODE.1': 'STANDBY',
    'L1011.TOOLTIPS.V_C_Breaker_004.TITLE': 'HYD IND QTY',
    'L1011.TOOLTIPS.CB.PULLED': 'PULLED',
    'L1011.TOOLTIPS.CB.PUSHED': 'PUSHED',
}

BATTERY_CLICK = ("(L:TOGGLE_Battery) ! (>L:TOGGLE_Battery) (>H:ELECTRICAL) "
                 "(L:TOGGLE_Battery) 0 == if{ (>H:ELECTRICAL_0) } (L:TOGGLE_Battery) 1 == if{ (>H:ELECTRICAL_1) }")
BATTERY_SET = ("p0 1 min 0 max (>O:INSTRUMENT_TOGGLE_Battery_IE_ID_Position) "
               "(O:INSTRUMENT_TOGGLE_Battery_IE_ID_Position) s0 l0 0 == if{ 0 (>L:TOGGLE_Battery) g1 } "
               "l0 1 == if{ 1 (>L:TOGGLE_Battery) g1 } :1 1 (>O:_ButtonAnimVar)")
ON_OFF_TOOLTIP = ("(L:TOGGLE_Battery) 1 == if{ (R:1:L1011.TOOLTIPS.ACTION.ON) } "
                  "els{ (R:1:L1011.TOOLTIPS.ACTION.OFF) }")
PUSH_SET = "p0 p0 if{ (L:INI_PULL_UP_CALLOUT_COMMAND, Bool) } els{ (L:INI_PULL_UP_CALLOUT_COMMAND, Bool) } 1 (>O:_ButtonAnimVar)"
CARGO_CLICK = ("(L:SWITCH_FWD_CARGO_EXT_MAIN) ! (>L:SWITCH_FWD_CARGO_EXT_MAIN) (>H:SWITCH_FWD_CARGO_EXT_MAIN) "
               "(L:FWD_CARGO_EXT_MAIN_FIRED, bool) ! if{ 1 (>L:FWD_CARGO_EXT_MAIN_FIRED, bool) }")
# The stock ignition switch is set to the switch's new position by reading it BEFORE the toggle.
IGNITION_CLICK = ("(A:TURB ENG IGNITION SWITCH:1, bool) ! if{ 1 (>K:TURBINE_IGNITION_SWITCH_SET1) } "
                  "els{ 0 (>K:TURBINE_IGNITION_SWITCH_SET1) } (L:{v}) ! (>L:{v}) (>H:{v})")


def two_position_switch(f, panel, var):
    """A two-position switch with an input event whose Set code writes the L:var, and ON/OFF words."""
    ie = 'INSTRUMENT_%s_IE_ID' % var
    n = f.component(var, panel)
    return n, ie, ("p0 1 min 0 max (>O:P) (O:P) s0 l0 0 == if{ 0 (>L:%s) g1 } l0 1 == if{ 1 (>L:%s) g1 } :1" % (var, var),
                   "(L:%s) 1 == if{ (R:1:L1011.TOOLTIPS.ACTION.ON) } els{ (R:1:L1011.TOOLTIPS.ACTION.OFF) }" % var)


def build_fixture():
    f = FixtureBuilder()
    root = f.component('L1011_SYSTEMS_BEHAVIORS')
    area = f.component('ENGINEER_PANEL', root)
    panel = f.component('ELECTRICAL_PANEL', area)

    n = f.component('TOGGLE_BATTERY', panel)
    f.mouserect(n, 'TOGGLE_BATTERY', BATTERY_CLICK, 'L1011.TOOLTIPS.TOGGLE_Battery.TITLE',
                ['INSTRUMENT_TOGGLE_Battery_IE_ID'])
    f.inputevent(n, 'INSTRUMENT_TOGGLE_BATTERY_IE_ID', BATTERY_SET, ON_OFF_TOOLTIP, 'Boolean')

    n = f.component('TOGGLE_BATTERY_SEQ1_NEW', panel)
    f.mouserect(n, 'TOGGLE_BATTERY_SEQ1_NEW', BATTERY_CLICK)

    n = f.component('SWITCH_APU_START', panel)
    f.mouserect(n, 'SWITCH_APU_START',
                "(M:Event) 'Lock' scmi 0 == if{ 1 (>O:_ButtonAnimVar_IsDown) 1 (>L:SWITCH_APU_START, boolean) } "
                "els{ (M:Event) 'Unlock' scmi 0 == if{ 0 (>L:SWITCH_APU_START, boolean) } }",
                'L1011.TOOLTIPS.SWITCH_APU_START.TITLE', ['INSTRUMENT_SWITCH_APU_START'])
    f.inputevent(n, 'INSTRUMENT_SWITCH_APU_START', PUSH_SET)

    n = f.component('ROTARY_FLT_STA', panel)
    f.mouserect(n, 'ROTARY_FLT_STA', "(M:Event) 'WheelUp' scmi 0 == if{ 1 (>B:AIRLINER_ROTARY_FLT_STA_Inc) }",
                None, ['AIRLINER_ROTARY_FLT_STA'])
    f.inputevent(n, 'AIRLINER_ROTARY_FLT_STA',
                 "p0 0 max 100 min s0 (>O:AIRLINER_ROTARY_FLT_STA_Position) l0 (>L:ROTARY_FLT_STA)",
                 "(L:ROTARY_FLT_STA) '%.1f%%' (F:Format)", 'percent')

    n = f.component('ROTARY_CPT_DH', panel)
    f.mouserect(n, 'ROTARY_CPT_DH',
                "(M:Event) 'WheelUp' scmi 0 == if{ 1 if{ (O:_KnobAnimVar) 10 + dnor (>O:_KnobAnimVar) "
                "(>H:ROTARY_CPT_DH_INC) } } els{ (M:Event) 'WheelDown' scmi 0 == if{ 1 if{ "
                "(>H:ROTARY_CPT_DH_DEC) } } }")

    n = f.component('ROTARY_TCAS_MODE', panel)
    f.mouserect(n, 'ROTARY_TCAS_MODE', "(O:SwitchState) (>O:PreviousPos) quit :1 g3",
                'L1011.TOOLTIPS.ROTARY_TCAS_MODE.TITLE', ['AIRLINER_ROTARY_TCAS_MODE'])
    f.inputevent(n, 'AIRLINER_ROTARY_TCAS_MODE',
                 "p0 1 min 0 max (>O:P) (O:P) s0 l0 0 == if{ 0 (>L:ROTARY_TCAS_MODE) 1 (>H:ROTARY_TCAS_MODE_0) g1 } "
                 "l0 1 == if{ 1 (>L:ROTARY_TCAS_MODE) 1 (>H:ROTARY_TCAS_MODE_1) g1 } :1",
                 "(B:AIRLINER_ROTARY_TCAS_MODE, Number) sp0 l0 0 == if{ (R:1:L1011.TOOLTIPS.ROTARY_TCAS_MODE.0) quit } "
                 "l0 1 == if{ (R:1:L1011.TOOLTIPS.ROTARY_TCAS_MODE.1) quit }", 'numbers')

    n = f.component('TOGGLE_CUTOFF_ENG_1', panel)
    f.mouserect(n, 'TOGGLE_CUTOFF_ENG_1',
                "(L:TOGGLE_CUTOFF_ENG_1) ! (>L:TOGGLE_CUTOFF_ENG_1) 2 (>K:FUELSYSTEM_VALVE_TOGGLE)",
                None, ['INSTRUMENT_TOGGLE_CUTOFF_ENG_1_IE_ID'])
    f.inputevent(n, 'INSTRUMENT_TOGGLE_CUTOFF_ENG_1_IE_ID',
                 "p0 1 min 0 max (>O:P) l0 0 == if{ 0 (>L:TOGGLE_CUTOFF_ENG_1) g1 } l0 1 == if{ 1 (>L:TOGGLE_CUTOFF_ENG_1) g1 } :1",
                 "(A:FUELSYSTEM VALVE SWITCH:2, Bool) if{ (R:1:L1011.TOOLTIPS.ACTION.ON) } els{ (R:1:L1011.TOOLTIPS.ACTION.OFF) }",
                 'Boolean')

    n = f.component('SWITCH_BRG_GEN_1', panel)
    f.mouserect(n, 'SWITCH_BRG_GEN_1',
                "(L:SWITCH_BRG_GEN_1) ! (>L:SWITCH_BRG_GEN_1) (>H:ELECTRICAL)", None,
                ['INSTRUMENT_SWITCH_BRG_GEN_1_IE_ID'])
    f.inputevent(n, 'INSTRUMENT_SWITCH_BRG_GEN_1_IE_ID',
                 "p0 1 min 0 max (>O:P) l0 0 == if{ 0 (>L:SWITCH_BRG_GEN_1) g1 } l0 1 == if{ 1 (>L:SWITCH_BRG_GEN_1) g1 } :1",
                 None, 'Boolean')

    n = f.component('SWITCH_ENG_1_DISCH', panel)
    f.mouserect(n, 'SWITCH_ENG_1_DISCH',
                "(M:Event) 'WheelUp' scmi 0 == if{ 1 (>O:GoToRelease) g1 } quit "
                ":1 (O:SwitchState) 2 == if{ 1 if{ 1 (>O:SwitchState) 1 (>L:SWITCH_ENG_1_DISCH, number) } } "
                ":2 (O:SwitchState) 0 == if{ 1 if{ 1 (>O:SwitchState) 1 (>L:SWITCH_ENG_1_DISCH, number) } } "
                "els{ (O:SwitchState) 1 == if{ 1 if{ 2 (>O:SwitchState) 2 (>L:SWITCH_ENG_1_DISCH, number) } } } "
                "(O:SwitchState) 1 == if{ 0 (>L:SWITCH_ENG_1_DISCH, number) }")

    n = f.component('TOGGLE_CAB_PRESS_FWD', panel)
    f.mouserect(n, 'TOGGLE_CAB_PRESS_FWD',
                "(M:Event) 'WheelUp' scmi 0 == if{ 1 (>O:GoToRelease) g1 } quit :1 "
                "(L:OUTFLOW_VALVE_FWD, number) 1 + 100 min 0 max (>L:OUTFLOW_VALVE_FWD, number)",
                None, ['INSTRUMENT_TOGGLE_CAB_PRESS_FWD_IE_ID'])
    f.inputevent(n, 'INSTRUMENT_TOGGLE_CAB_PRESS_FWD_IE_ID',
                 "p0 2 min 0 max (>O:P) (O:P) s0 l0 0 == if{ 0 (>) g1 } l0 1 == if{ 1 (>) g1 } l0 2 == if{ 2 (>) g1 } :1",
                 None, 'numbers')

    n, ie, (set_code, tooltip) = two_position_switch(f, panel, 'SWITCH_FWD_CARGO_EXT_MAIN')
    f.mouserect(n, 'SWITCH_FWD_CARGO_EXT_MAIN', CARGO_CLICK, None, [ie])
    f.inputevent(n, ie, set_code, tooltip, 'Boolean')

    for var in ('SWITCH_CONT_IGNITION', 'SWITCH_OTHER_IGNITION'):
        n, ie, (set_code, tooltip) = two_position_switch(f, panel, var)
        f.mouserect(n, var, IGNITION_CLICK.replace('{v}', var), None, [ie])
        f.inputevent(n, ie, set_code, tooltip, 'Boolean')

    extras = FixtureBuilder()
    eroot = extras.component('L1011_EXTRAS')
    ebox = extras.component('CIRCUIT_BREAKERS', eroot)
    b = extras.component('V_C_BREAKER_004', ebox)
    extras.mouserect(b, 'V_C_BREAKER_004',
                     "(L:V_C_Breaker_004) ! (>L:V_C_Breaker_004) (>H:V_C_Breaker_004) "
                     "(L:V_C_Breaker_004) 0 == if{ (>H:V_C_Breaker_004_0) } (L:V_C_Breaker_004) 1 == if{ (>H:V_C_Breaker_004_1) }",
                     'L1011.TOOLTIPS.V_C_Breaker_004.TITLE', ['INSTRUMENT_V_C_Breaker_004_IE_ID'])
    extras.inputevent(b, 'INSTRUMENT_V_C_BREAKER_004_IE_ID',
                      "p0 1 min 0 max (>O:P) l0 0 == if{ 0 (>L:V_C_Breaker_004) g1 } l0 1 == if{ 1 (>L:V_C_Breaker_004) g1 } :1",
                      "(L:V_C_Breaker_004) 1 == if{ (R:1:L1011.TOOLTIPS.CB.PULLED) } els{ (R:1:L1011.TOOLTIPS.CB.PUSHED) }",
                      'Boolean')
    f.material("(L:AC_ESS_FAIL_LIGHT) 1 == (L:ENG_FIRE_1) or")
    return [(Behavior(f.build()), 'COCKPIT'), (Behavior(extras.build()), 'COCKPIT_EXTRAS')]


class TooltipPositionTests(unittest.TestCase):
    def test_input_event_reading_tooltip_uses_stock_words(self):
        tt = "(B:INSTRUMENT_SWITCH_HYD_PUMP_A1_IE_ID, Bool) if{ (R:1:COCKPIT.TOOLTIPSV2.GT_STATE_ON) } els{ (R:1:COCKPIT.TOOLTIPSV2.GT_STATE_OFF) }"
        self.assertEqual({'0': 'OFF', '1': 'ON'}, dict(gen.positions_from_tooltip(tt, {})))

    def test_inverted_switch_keeps_its_own_order(self):
        tt = "(L:SWITCH_WHEEL_LIGHT) 1 == if{ (R:1:L1011.TOOLTIPS.ACTION.OFF) } els{ (R:1:L1011.TOOLTIPS.ACTION.ON) }"
        self.assertEqual({'0': 'ON', '1': 'OFF'}, dict(gen.positions_from_tooltip(tt, LOC)))

    def test_missing_words_are_empty(self):
        tt = "(B:AIRLINER_TOGGLE_STBY_POWER, Number) sp0 l0 0 == if{ (R:1:L1011.TOOLTIPS.TOGGLE_STBY_POWER.0) quit }"
        self.assertEqual({'0': ''}, dict(gen.positions_from_tooltip(tt, {})))


class GeneratorTests(unittest.TestCase):
    def setUp(self):
        self.map = gen.build_map(build_fixture(), LOC, '1.0.8')
        self.by_id = {c['id']: c for c in self.map['controls']}

    def test_switch_reads_its_own_words_and_replays_the_click_events(self):
        c = self.by_id['TOGGLE_BATTERY']
        self.assertEqual('switch', c['kind'])
        self.assertEqual('BATTERY SWITCH', c['title'])
        self.assertEqual(('ENGINEER_PANEL', 'ELECTRICAL_PANEL'), (c['area'], c['panel']))
        self.assertEqual('L:TOGGLE_Battery', c['state_var'])
        self.assertEqual({'0': 'OFF', '1': 'ON'}, dict(c['positions']))
        self.assertEqual(['L:TOGGLE_Battery=1', 'H:ELECTRICAL', 'H:ELECTRICAL_1'], c['transitions']['1'])
        self.assertEqual(['L:TOGGLE_Battery=0', 'H:ELECTRICAL', 'H:ELECTRICAL_0'], c['transitions']['0'])

    def test_second_binding_of_a_knob_is_not_listed(self):
        self.assertNotIn('TOGGLE_BATTERY_SEQ1_NEW', self.by_id)

    def test_push_button_press_and_release(self):
        c = self.by_id['SWITCH_APU_START']
        self.assertEqual('button', c['kind'])
        self.assertEqual(['L:SWITCH_APU_START, boolean=1'], c['press'])
        self.assertEqual(['L:SWITCH_APU_START, boolean=0'], c['release'])

    def test_percent_knob_writes_the_value(self):
        c = self.by_id['ROTARY_FLT_STA']
        self.assertEqual('knob', c['kind'])
        self.assertEqual('L:ROTARY_FLT_STA', c['state_var'])
        self.assertEqual(['L:ROTARY_FLT_STA={v}'], c['set_template'])
        self.assertEqual([0.0, 100.0], c['range'])

    def test_encoder_steps(self):
        c = self.by_id['ROTARY_CPT_DH']
        self.assertEqual('encoder', c['kind'])
        self.assertEqual(['H:ROTARY_CPT_DH_INC'], c['inc'])
        self.assertEqual(['H:ROTARY_CPT_DH_DEC'], c['dec'])

    def test_selector_positions_come_from_the_tooltip_cases(self):
        c = self.by_id['ROTARY_TCAS_MODE']
        self.assertEqual({'0': 'TEST', '1': 'STANDBY'}, dict(c['positions']))
        self.assertEqual(['L:ROTARY_TCAS_MODE=1', 'H:ROTARY_TCAS_MODE_1'], c['transitions']['1'])

    def test_stock_state_from_the_tooltip_and_toggle_event_kept(self):
        c = self.by_id['TOGGLE_CUTOFF_ENG_1']
        self.assertEqual('A:FUELSYSTEM VALVE SWITCH:2', c['state_var'])
        self.assertEqual('Bool', c['state_unit'])
        self.assertEqual(['L:TOGGLE_CUTOFF_ENG_1=1', 'K:FUELSYSTEM_VALVE_TOGGLE=2'], c['transitions']['1'])

    def test_latch_buttons_press_position_one(self):
        c = self.by_id['SWITCH_BRG_GEN_1']
        self.assertEqual('latch', c['kind'])
        self.assertEqual(['L:SWITCH_BRG_GEN_1=1', 'H:ELECTRICAL'], c['press'])

    def test_spring_switch_positions_and_rest(self):
        c = self.by_id['SWITCH_ENG_1_DISCH']
        self.assertEqual('spring', c['kind'])
        self.assertEqual(['0', '1', '2'], list(c['positions']))
        self.assertEqual(1.0, c['rest'])
        self.assertEqual(['L:SWITCH_ENG_1_DISCH=2'], c['transitions']['2'])

    def test_breakers_are_separate(self):
        self.assertNotIn('V_C_BREAKER_004', self.by_id)
        b = self.map['breakers'][0]
        self.assertEqual(4, b['index'])
        self.assertEqual('HYD IND QTY', b['title'])
        self.assertEqual('L:V_C_Breaker_004', b['state_var'])
        self.assertEqual(['L:V_C_Breaker_004=1', 'H:V_C_Breaker_004', 'H:V_C_Breaker_004_1'], b['transitions']['1'])

    def test_a_switch_whose_positions_write_nothing_is_not_offered(self):
        c = self.by_id['TOGGLE_CAB_PRESS_FWD']
        self.assertEqual('none', c['kind'])
        self.assertEqual('positions write nothing replayable', c['note'])

    def test_a_cargo_extinguisher_fires_on_every_click(self):
        # Every click flips the switch, fires the H: event and latches FIRED to 1 the first time.
        c = self.by_id['SWITCH_FWD_CARGO_EXT_MAIN']
        for v in ('0', '1'):
            self.assertEqual(['L:SWITCH_FWD_CARGO_EXT_MAIN=%s' % v, 'H:SWITCH_FWD_CARGO_EXT_MAIN',
                              'L:FWD_CARGO_EXT_MAIN_FIRED, bool=1'], c['transitions'][v])

    def test_continuous_ignition_sets_the_stock_switches_to_its_position(self):
        c = self.by_id['SWITCH_CONT_IGNITION']
        self.assertEqual(['L:SWITCH_CONT_IGNITION=1', 'K:TURBINE_IGNITION_SWITCH_SET1=1', 'H:SWITCH_CONT_IGNITION'],
                         c['transitions']['1'])
        self.assertEqual(['L:SWITCH_CONT_IGNITION=0', 'K:TURBINE_IGNITION_SWITCH_SET1=0', 'H:SWITCH_CONT_IGNITION'],
                         c['transitions']['0'])

    def test_only_the_allowlisted_switch_assumes_other_variables_follow_it(self):
        c = self.by_id['SWITCH_OTHER_IGNITION']
        self.assertEqual(['L:SWITCH_OTHER_IGNITION=1', 'H:SWITCH_OTHER_IGNITION'], c['transitions']['1'])
        self.assertEqual(['L:SWITCH_OTHER_IGNITION=0', 'H:SWITCH_OTHER_IGNITION'], c['transitions']['0'])

    def test_lamps_are_every_emissive_lvar(self):
        self.assertEqual(['AC_ESS_FAIL_LIGHT', 'ENG_FIRE_1'], self.map['lamps'])

    def test_output_is_deterministic(self):
        again = gen.build_map(build_fixture(), LOC, '1.0.8')
        self.assertEqual(gen.to_json(self.map), gen.to_json(again))
        self.assertEqual('1.0.8', json.loads(gen.to_json(self.map))['package_version'])


if __name__ == '__main__':
    unittest.main()
