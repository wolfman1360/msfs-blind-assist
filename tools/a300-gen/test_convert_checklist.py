import os
import sys
import unittest

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import convert_checklist as cc

XML = b"""<?xml version="1.0" encoding="Windows-1252"?>
<SimBase.Document Type="Checklist" version="1,0"><Checklist.Checklist>
<Step ChecklistStepId="PREFLIGHT_GATE">
  <Page SubjectTT="TT:A300.CHECKLISTS.PRELIM">
    <Checkpoint Id="A300.CHECKLISTS.PRELIM.BATT">
      <CheckpointDesc SubjectTT="TT:A300.CHECKLISTS.PRELIM.BATT" ExpectationTT="TT:A300.CHECKLISTS.ACTION.AUTO"/>
    </Checkpoint>
    <Checkpoint Id="B"><CheckpointDesc SubjectTT="TT:A300.CHECKLISTS.PRELIM.IRS" ExpectationTT="TT:A300.CHECKLISTS.ACTION.NAV"/></Checkpoint>
    <Block SubjectTT="TT:A300.CHECKLISTS.PRELIM.APUA.BLOCK">
      <Checkpoint Id="C"><CheckpointDesc SubjectTT="TT:A300.CHECKLISTS.PRELIM.APUM" ExpectationTT="TT:A300.CHECKLISTS.ACTION.ON"/></Checkpoint>
    </Block>
  </Page>
</Step>
<Step ChecklistStepId="LANDING_GROUNDROLL"><Page SubjectTT="TT:A300.CHECKLISTS.AFTERLAND"><Checkpoint Id="D"><CheckpointDesc SubjectTT="TT:A300.CHECKLISTS.AFTERLAND.FMC" ExpectationTT="TT:MISSING"/></Checkpoint></Page></Step>
</Checklist.Checklist></SimBase.Document>"""

LOC = {
    'A300.CHECKLISTS.PRELIM': 'Preliminary Cockpit Preparation',
    'A300.CHECKLISTS.PRELIM.BATT': 'BATTERIES',
    'A300.CHECKLISTS.PRELIM.IRS': 'IRS MODE SELECTORS',
    'A300.CHECKLISTS.PRELIM.APUA.BLOCK': 'APU (As required)',
    'A300.CHECKLISTS.PRELIM.APUM': 'APU Master Switch',
    'A300.CHECKLISTS.ACTION.AUTO': 'AUTO',
    'A300.CHECKLISTS.ACTION.NAV': 'NAV',
    'A300.CHECKLISTS.ACTION.ON': 'ON',
    'A300.CHECKLISTS.AFTERLAND': 'After Landing Procedure',
    'A300.CHECKLISTS.AFTERLAND.FMC': 'FMC MCDU',
}


class ConvertChecklistTests(unittest.TestCase):
    def test_format(self):
        self.assertEqual(
            '[Preflight - Preliminary cockpit preparation]\n'
            'Batteries: auto\n'
            'IRS mode selectors: nav\n'
            'APU (as required) - APU master switch: on\n'
            '\n'
            '[Landing roll - After landing procedure]\n'
            'FMC MCDU\n',
            cc.convert(XML, LOC))

    def test_sentence_case_keeps_a300_abbreviations(self):
        self.assertEqual('FMC (initialise using MCDU)', cc.sentence_case('FMC (INITIALISE USING MCDU)'))
        self.assertEqual('EFIS control panel - PFD and ND brightness', cc.sentence_case('EFIS CONTROL PANEL - PFD AND ND BRIGHTNESS'))


if __name__ == '__main__':
    unittest.main()
