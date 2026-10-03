import os
import sys
import unittest

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import l1011_rpn as rpn

BATTERY_CLICK = ("(L:TOGGLE_Battery) ! (>L:TOGGLE_Battery) (>H:ELECTRICAL) "
                 "(L:TOGGLE_Battery) 0 == if{ (>H:ELECTRICAL_0) } (L:TOGGLE_Battery) 1 == if{ (>H:ELECTRICAL_1) }")
INSERT_KEY = ("(M:Event) 'Lock' scmi 0 == if{ 1 (>O:_ButtonAnimVar_IsDown) 1 (>L:INS_1_KEY_INSERT_PUSH) "
              "(>H:INS_1_KEY_INSERT) } els{ (M:Event) 'Unlock' scmi 0 == if{ 0 (>O:_ButtonAnimVar_IsDown) "
              "0 (>L:INS_1_KEY_INSERT_PUSH) } }")
TCAS_SET = ("p0 5 min 0 max (>O:AIRLINER_ROTARY_TCAS_MODE_Position) (O:AIRLINER_ROTARY_TCAS_MODE_Position) s0 "
            "l0 0 == if{ 0 (>L:ROTARY_TCAS_MODE) 1 (>H:ROTARY_TCAS_MODE_0) g1 } "
            "l0 1 == if{ 1 (>L:ROTARY_TCAS_MODE) 1 (>H:ROTARY_TCAS_MODE_1) g1 } :1")


class TokenTests(unittest.TestCase):
    def test_variables_with_spaces_and_quotes_are_one_token(self):
        t = rpn.tokens("(A:ELECTRICAL BUS VOLTAGE:'dc_stby_bus'_n, volts) 20 > if{ 1 (>L:X, boolean) }")
        self.assertEqual("(A:ELECTRICAL BUS VOLTAGE:'dc_stby_bus'_n, volts)", t[0])
        self.assertEqual('(>L:X, boolean)', t[-2])

    def test_parse_var_splits_unit(self):
        v = rpn.parse_var('(>L:SWITCH_APU_START, boolean)')
        self.assertEqual((True, 'L', 'SWITCH_APU_START', 'boolean'), tuple(v))
        self.assertIsNone(rpn.parse_var('if{'))


class EffectsTests(unittest.TestCase):
    def test_toggle_click_code(self):
        e = rpn.effects(BATTERY_CLICK)
        self.assertEqual(('L', 'TOGGLE_Battery', 'toggle'), tuple(e[0])[:3])
        self.assertEqual(['ELECTRICAL', 'ELECTRICAL_0', 'ELECTRICAL_1'], [x.name for x in e if x.kind == 'H'])

    def test_literal_writes_and_k_params(self):
        e = rpn.effects("6 (>K:FUELSYSTEM_VALVE_OPEN) 0 1 (>K:2:COVER_SET) 1 (>L:SWITCH_APU_START, boolean)")
        self.assertEqual(('K', 'FUELSYSTEM_VALVE_OPEN', '6'), tuple(e[0])[:3])
        self.assertEqual(('K', '2:COVER_SET', '0,1'), tuple(e[1])[:3])
        self.assertEqual(('L', 'SWITCH_APU_START', '1', 'boolean'), tuple(e[2]))

    def test_expression_write_is_marked(self):
        e = rpn.effects("(L:INI_VS_PID_SETPOINT_2) 100 + (>L:INI_VS_PID_SETPOINT_2)")
        self.assertEqual('expr', e[0].value)

    def test_reads(self):
        r = rpn.reads("(L:A) (A:FUELSYSTEM VALVE SWITCH:2, Bool) (>L:B) (M:Event)")
        self.assertEqual([('L', 'A'), ('A', 'FUELSYSTEM VALVE SWITCH:2')], [(v.kind, v.name) for v in r])


class BranchTests(unittest.TestCase):
    def test_mouse_branches(self):
        b = rpn.mouse_branches(INSERT_KEY)
        self.assertEqual(['Lock', 'Unlock'], list(b))
        self.assertIn('(>H:INS_1_KEY_INSERT)', b['Lock'])
        self.assertNotIn('Unlock', b['Lock'])
        self.assertIn('0 (>L:INS_1_KEY_INSERT_PUSH)', b['Unlock'])

    def test_no_branches_in_plain_click_code(self):
        self.assertEqual({}, dict(rpn.mouse_branches(BATTERY_CLICK)))

    def test_set_branches_drop_gotos(self):
        b = rpn.set_branches(TCAS_SET)
        self.assertEqual(['0', '1'], list(b))
        self.assertEqual('1 (>L:ROTARY_TCAS_MODE) 1 (>H:ROTARY_TCAS_MODE_1)', b['1'])

    def test_set_range_both_orders(self):
        self.assertEqual((0.0, 1.0), rpn.set_range('p0 1 min 0 max (>O:X)'))
        self.assertEqual((0.0, 100.0), rpn.set_range('p0 0 max 100 min s0 (>O:X)'))
        self.assertEqual((0.0, 5.0), rpn.set_range(TCAS_SET))
        self.assertIsNone(rpn.set_range('p0 p0 if{ 1 } els{ 0 } 1 (>O:_ButtonAnimVar)'))

    def test_push_template(self):
        self.assertTrue(rpn.is_push_template(
            'p0 p0 if{ (L:INI_PULL_UP_CALLOUT_COMMAND, Bool) } els{ (L:INI_PULL_UP_CALLOUT_COMMAND, Bool) } 1 (>O:_ButtonAnimVar)'))
        self.assertFalse(rpn.is_push_template(TCAS_SET))


class PositionTests(unittest.TestCase):
    def test_families_need_two_suffixed_members(self):
        self.assertEqual({'ELECTRICAL'}, rpn.positional_families(['ELECTRICAL', 'ELECTRICAL_0', 'ELECTRICAL_1']))
        self.assertEqual(set(), rpn.positional_families(['ELECTRICAL', 'ELECTRICAL_1']))

    def test_breaker_numbers_are_not_mistaken_for_positions(self):
        names = ['V_C_Breaker_004', 'V_C_Breaker_004_0', 'V_C_Breaker_004_1']
        self.assertEqual({'V_C_Breaker_004'}, rpn.positional_families(names))

    def test_filter_keeps_the_target_member_and_unsuffixed_events(self):
        kept = rpn.filter_for_position(rpn.effects(BATTERY_CLICK), '1')
        self.assertEqual(['ELECTRICAL', 'ELECTRICAL_1'], [e.name for e in kept if e.kind == 'H'])


class TextTests(unittest.TestCase):
    def test_effect_text(self):
        e = rpn.effects("1 (>L:SWITCH_APU_START, boolean) (>H:APU) 2 (>K:FUELSYSTEM_VALVE_TOGGLE)")
        self.assertEqual('L:SWITCH_APU_START, boolean=1', rpn.effect_text(e[0]))
        self.assertEqual('H:APU', rpn.effect_text(e[1]))
        self.assertEqual('K:FUELSYSTEM_VALVE_TOGGLE=2', rpn.effect_text(e[2]))
        self.assertEqual('L:SWITCH_APU_START, boolean=0', rpn.effect_text(e[0], '0'))

    def test_num(self):
        self.assertEqual('1', rpn.num('1.0'))
        self.assertEqual('0.5', rpn.num('0.5'))



class EventParameterTests(unittest.TestCase):
    def test_component_reference_is_one_token_and_a_parameter(self):
        e = rpn.effects("1 'Slats'_n (>K:2:HYDRAULIC_ACTUATOR_ACTIVE_SET)")
        self.assertEqual("1,'Slats'_n", e[0].value)

    def test_computed_parameter_is_expr(self):
        e = rpn.effects("(L:SWITCH_FE_SLAT_LOCK, bool) 'Slats'_n (>K:2:HYDRAULIC_ACTUATOR_ACTIVE_SET)")
        self.assertEqual('expr', e[0].value)

    def test_parameterless_event_is_empty(self):
        e = rpn.effects("(>K:CABIN_SEATBELTS_ALERT_SWITCH_TOGGLE) (L:S) ! (>L:S)")
        self.assertEqual('', e[0].value)


class ResolveConditionsTests(unittest.TestCase):
    IGNITION = ("(A:TURB ENG IGNITION SWITCH:1, bool) ! if{ 1 (>K:TURBINE_IGNITION_SWITCH_SET1) } "
                "els{ 0 (>K:TURBINE_IGNITION_SWITCH_SET1) } (L:SWITCH_CONT_IGNITION) ! (>L:SWITCH_CONT_IGNITION) "
                "(>H:SWITCH_CONT_IGNITION) (L:SWITCH_CONT_IGNITION) 0 == if{ (>H:SWITCH_CONT_IGNITION_0) } "
                "(L:SWITCH_CONT_IGNITION) 1 == if{ (>H:SWITCH_CONT_IGNITION_1) }")
    GUARD = ("(L:GUARD_STBY_POWER) 1 == if{ 1 (>L:TOGGLE_STBY_POWER) } (L:GUARD_STBY_POWER) ! "
             "(>L:GUARD_STBY_POWER) (>H:GUARD_STBY_POWER)")

    def _events(self, code, state, new, old):
        out = rpn.resolve_conditions(code, state, new, old, True)
        return [(e.kind, e.name, e.value) for e in rpn.effects(out) if e.kind in 'HKL']

    def test_ignition_on_sets_the_stock_switch_on(self):
        ev = self._events(self.IGNITION, 'L:SWITCH_CONT_IGNITION', '1', '0')
        self.assertIn(('K', 'TURBINE_IGNITION_SWITCH_SET1', '1'), ev)
        self.assertNotIn(('K', 'TURBINE_IGNITION_SWITCH_SET1', '0'), ev)
        self.assertIn(('H', 'SWITCH_CONT_IGNITION_1', ''), ev)
        self.assertNotIn(('H', 'SWITCH_CONT_IGNITION_0', ''), ev)

    def test_ignition_off_sets_the_stock_switch_off(self):
        ev = self._events(self.IGNITION, 'L:SWITCH_CONT_IGNITION', '0', '1')
        self.assertIn(('K', 'TURBINE_IGNITION_SWITCH_SET1', '0'), ev)
        self.assertNotIn(('K', 'TURBINE_IGNITION_SWITCH_SET1', '1'), ev)

    def test_condition_before_the_toggle_reads_the_old_position(self):
        closing = self._events(self.GUARD, 'L:GUARD_STBY_POWER', '0', '1')
        opening = self._events(self.GUARD, 'L:GUARD_STBY_POWER', '1', '0')
        self.assertIn(('L', 'TOGGLE_STBY_POWER', '1'), closing)
        self.assertNotIn(('L', 'TOGGLE_STBY_POWER', '1'), opening)

    def test_state_read_feeding_an_event_becomes_the_new_position(self):
        code = ("(L:SWITCH_FE_SLAT_LOCK) ! (>L:SWITCH_FE_SLAT_LOCK) (L:SWITCH_FE_SLAT_LOCK, bool) 'Slats'_n "
                "(>K:2:HYDRAULIC_ACTUATOR_ACTIVE_SET)")
        out = rpn.resolve_conditions(code, 'L:SWITCH_FE_SLAT_LOCK', '1', '0', True)
        self.assertEqual("1,'Slats'_n", [e for e in rpn.effects(out) if e.kind == 'K'][0].value)

    CARGO = ("(L:SWITCH_FWD_CARGO_EXT_MAIN) ! (>L:SWITCH_FWD_CARGO_EXT_MAIN) (>H:SWITCH_FWD_CARGO_EXT_MAIN) "
             "(L:FWD_CARGO_EXT_MAIN_FIRED, bool) ! if{ 1 (>L:FWD_CARGO_EXT_MAIN_FIRED, bool) }")

    def test_one_way_latch_writes_one_on_every_position(self):
        # The click latches FIRED to 1 the first time either way; it is never written back to 0.
        for follow in (False, True):
            for new, old in (('0', '1'), ('1', '0')):
                ev = self._events_with(self.CARGO, 'L:SWITCH_FWD_CARGO_EXT_MAIN', new, old, follow)
                self.assertIn(('L', 'FWD_CARGO_EXT_MAIN_FIRED', '1'), ev, (follow, new))
                self.assertIn(('H', 'SWITCH_FWD_CARGO_EXT_MAIN', ''), ev, (follow, new))

    def test_other_variables_are_unknown_unless_the_caller_says_they_follow(self):
        ev = self._events_with(self.IGNITION, 'L:SWITCH_CONT_IGNITION', '1', '0', False)
        self.assertFalse([e for e in ev if e[0] == 'K'])
        self.assertIn(('H', 'SWITCH_CONT_IGNITION_1', ''), ev)

    def _events_with(self, code, state, new, old, follow):
        out = rpn.resolve_conditions(code, state, new, old, follow)
        return [(e.kind, e.name, e.value) for e in rpn.effects(out) if e.kind in 'HKL']

    def test_unknown_condition_is_dropped_with_both_branches(self):
        out = rpn.resolve_conditions("(L:OTHER) 3 == if{ (>H:A) } els{ (>H:B) } (>H:C)", 'L:S', '1', '0', False)
        self.assertEqual(['C'], [e.name for e in rpn.effects(out)])


if __name__ == '__main__':
    unittest.main()
