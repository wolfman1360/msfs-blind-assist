import os
import sys
import unittest

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import convert_checklist as cc

XML = b"""<?xml version="1.0" encoding="Windows-1252"?>
<SimBase.Document Type="Checklist" version="1,0"><Checklist.Checklist>
<Step ChecklistStepId="PREFLIGHT_GATE">
  <Page SubjectTT="TT:TRISTAR.CHECKLIST_FE_SAFETY_INSPECTION">
    <Checkpoint Id="A"><CheckpointDesc SubjectTT="TT:TRISTAR.SAFETY_INSP_CBS_IN" ExpectationTT="TT:TRISTAR.SAFETY_INSP_CBS_IN.ACTION"/></Checkpoint>
    <Checkpoint Id="B"><Instrument Id="SWITCH_CONT_IGNITION"/><CheckpointDesc SubjectTT="TT:TRISTAR.SAFETY_INSP.CONT_IGN" ExpectationTT="TT:TRISTAR.SAFETY_INSP.CONT_IGN.ACTION"/></Checkpoint>
    <Checkpoint Id="C"><CheckpointDesc SubjectTT="TT:TRISTAR.APU" ExpectationTT="TT:MISSING"/></Checkpoint>
    <Block SubjectTT="TT:TRISTAR.STBY_CHECK">
      <Checkpoint Id="E"><CheckpointDesc SubjectTT="TT:TRISTAR.BATT" ExpectationTT="TT:TRISTAR.BATT.ACTION"/></Checkpoint>
    </Block>
  </Page>
</Step>
<Step ChecklistStepId="LANDING_GATE"><Page SubjectTT="TT:TRISTAR.PARKING"><Checkpoint Id="D"><CheckpointDesc SubjectTT="TT:TRISTAR.INS" ExpectationTT="TT:TRISTAR.INS.ACTION"/></Checkpoint></Page></Step>
</Checklist.Checklist></SimBase.Document>"""

LOC = {
    'TRISTAR.CHECKLIST_FE_SAFETY_INSPECTION': 'COCKPIT SAFETY INSPECTION',
    'TRISTAR.SAFETY_INSP_CBS_IN': 'CIRCUIT BREAKERS',
    'TRISTAR.SAFETY_INSP_CBS_IN.ACTION': 'CHECKED',
    'TRISTAR.SAFETY_INSP.CONT_IGN': 'CONTINUOUS IGNITION',
    'TRISTAR.SAFETY_INSP.CONT_IGN.ACTION': 'UNLATCHED',
    'TRISTAR.APU': 'APU MASTER',
    'TRISTAR.PARKING': 'PARKING',
    'TRISTAR.INS': 'INS MODE SELECTORS',
    'TRISTAR.INS.ACTION': 'OFF',
    'TRISTAR.STBY_CHECK': 'STANDBY POWER CHECK',
    'TRISTAR.BATT': 'BATTERY',
    'TRISTAR.BATT.ACTION': 'OFF',
}


class ConvertChecklistTests(unittest.TestCase):
    def test_format(self):
        self.assertEqual(
            '[Preflight - Cockpit safety inspection]\n'
            'Circuit breakers: checked\n'
            'Continuous ignition: unlatched\n'
            'APU master\n'
            'Standby power check - Battery: off\n'
            '\n'
            '[Parking - Parking]\n'
            'INS mode selectors: off\n',
            cc.convert(XML, LOC))

    def test_sentence_case_keeps_abbreviations(self):
        self.assertEqual('APU bleed and DC bus', cc.sentence_case('APU BLEED AND DC BUS'))
        self.assertEqual('Fuel & ignition switches', cc.sentence_case('FUEL & IGNITION SWITCHES'))

    def test_expectation_starting_with_an_abbreviation_keeps_it(self):
        self.assertEqual('Ignition: APU bleed', cc.item_text('Ignition', 'APU bleed'))


if __name__ == '__main__':
    unittest.main()
