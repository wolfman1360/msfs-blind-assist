"""Generates the iniBuilds L-1011 TriStar control map that MSFS Blind Assist embeds.

Reads the aircraft's compiled cockpit behavior XML and its en-US.locPak tooltip file, and writes
one JSON entry per clickable cockpit control: where it sits, what the aircraft calls it, which
variable holds its position, what each position is called, and the ordered list of writes and
events the cockpit's own click code performs to reach each position. Circuit breakers go in a
separate array; every variable an emissive (lamp) material reads is listed for the annunciator
curation in L1011Annunciators.cs.

Deterministic: the same package always produces byte-identical output.

    python tools/l1011-gen/generate_l1011_map.py --package "D:\\games\\MSFS2024\\Community2024\\inibuilds-aircraft-l1011"

writes MSFSBlindAssist/Resources/l1011_control_map.json (use --out to write elsewhere). Review a
regeneration with diff_maps.py before committing it.
"""
import argparse
import json
import os
import re
import sys
from collections import OrderedDict

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from behavior_xml import Behavior
import l1011_rpn as rpn

REPO_ROOT = os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', '..'))
DEFAULT_OUT = os.path.join(REPO_ROOT, 'MSFSBlindAssist', 'Resources', 'l1011_control_map.json')
BEHAVIOR_DIR = os.path.join('SimObjects', 'Airplanes', 'inibuilds-l1011', 'attachments', 'inibuilds')
COCKPIT_FILES = [
    (os.path.join('Part_L1011_Cockpit_Panel', 'model', 'L1011_COCKPIT.behavior.xml'), 'COCKPIT'),
    (os.path.join('Part_L1011_Cockpit', 'model', 'L1011_COCKPIT_EXTRAS.behavior.xml'), 'COCKPIT_EXTRAS'),
]
LOC_FILE = 'en-US.locPak'

# Words the stock (Asobo) tooltip keys stand for when the package's locPak has no entry.
STOCK_LABELS = {'GT_STATE_ON': 'ON', 'GT_STATE_OFF': 'OFF', 'GT_STATE_NA': ''}

BREAKER_RE = re.compile(r'^INSTRUMENT_V_C_BREAKER_(\d+)_IE_ID$', re.IGNORECASE)
POS_IF_ELSE_RE = re.compile(r"\((?:L|A|B):[^()]*\)\s*(-?\d+)\s*==\s*if\{\s*\(R:1:([^)]+)\)\s*\}\s*els\{\s*\(R:1:([^)]+)\)\s*\}")
POS_BOOL_RE = re.compile(r"\((?:L|A|B):[^()]*\)\s*if\{\s*\(R:1:([^)]+)\)\s*\}\s*els\{\s*\(R:1:([^)]+)\)\s*\}")
POS_CASE_RE = re.compile(r"l0\s+(-?\d+)\s*==\s*if\{\s*\(R:1:([^)]+)\)")

# ---- Curated behaviour. Labels and spoken names are NOT curated here: they live in
# ---- MSFSBlindAssist/Aircraft/L1011/L1011PanelLayout.cs and its row files.

# Generator and tie breaker CLOSE/TRIP buttons. The aircraft's own flight-engineer flows press
# them by writing 1 and firing H:ELECTRICAL, and the engineer-panel script latches the press
# and puts the L:var back to 0 (instruments-js.md section 10; efb-wasm-systems.md 2.10). Their
# tooltip words ("1=OFF; 0=CLOSE") describe that reset state, so read as a two-position switch
# they would show the wrong word: they are buttons that press position 1.
LATCH_BUTTONS = {
    'SWITCH_APU_BRG', 'SWITCH_APU_TRIP', 'SWITCH_GND_FLOW', 'SWITCH_GND_FLOW_OPEN',
    'SWITCH_BRG_GEN_1', 'SWITCH_BRG_GEN_2', 'SWITCH_BRG_GEN_3',
    'SWITCH_TRIP_GEN_1', 'SWITCH_TRIP_GEN_2', 'SWITCH_TRIP_GEN_3',
    'SWITCH_GEN_FLOW_1', 'SWITCH_GEN_FLOW_2', 'SWITCH_GEN_FLOW_3',
    'SWITCH_GEN_FLOW_OPEN_1', 'SWITCH_GEN_FLOW_OPEN_2', 'SWITCH_GEN_FLOW_OPEN_3',
    'SWITCH_TIE_FLOW_1', 'SWITCH_TIE_FLOW_2', 'SWITCH_TIE_FLOW_3',
    'SWITCH_BUS_1_BREAKER', 'SWITCH_BUS_2_BREAKER', 'SWITCH_BUS_3_BREAKER',
}

# Two-position switches whose click code sets OTHER variables to the switch's new position by
# reading them before the toggle ("(A:TURB ENG IGNITION SWITCH:1, bool) ! if{ 1 (>K:..SET1) }
# els{ 0 (>K:..SET1) }"), so a condition on them can be decided as if they followed the switch.
# Only where that was checked in the click code: elsewhere the same assumption reverses the
# cockpit (the forward cargo extinguishers' FIRED latch) or guesses an unrelated condition (the
# ADF ident's dependence on the ADF mode), so another variable's condition stays undecided.
FOLLOWS_STATE = {'SWITCH_CONT_IGNITION'}

# Fire-extinguisher discharge switches are momentary either side of a centre detent (position 1,
# the template's SwitchState 1, flagged XMLVAR_MomentarySwitch_IsHeld) and spring back to it.
SPRING_SWITCHES = {'SWITCH_ENG_1_DISCH': 1, 'SWITCH_ENG_2_DISCH': 1, 'SWITCH_ENG_3_DISCH': 1,
                   'SWITCH_APU_DISCH': 1}


def load_loc(package):
    with open(os.path.join(package, LOC_FILE), 'r', encoding='utf-8-sig') as f:
        return json.load(f)['LocalisationPackage']['Strings']


def loc_text(loc, key):
    if not key:
        return ''
    if key in loc:
        return loc[key].strip()
    return STOCK_LABELS.get(key.split('.')[-1], '')


def positions_from_tooltip(tt, loc):
    if not tt:
        return OrderedDict()
    m = POS_IF_ELSE_RE.search(tt)
    if m:
        v = rpn.num(m.group(1))
        other = '0' if v == '1' else '1'
        pairs = {v: loc_text(loc, m.group(2)), other: loc_text(loc, m.group(3))}
        return OrderedDict(sorted(pairs.items(), key=lambda kv: float(kv[0])))
    m = POS_BOOL_RE.search(tt)
    if m:
        return OrderedDict([('0', loc_text(loc, m.group(2))), ('1', loc_text(loc, m.group(1)))])
    items = POS_CASE_RE.findall(tt)
    return OrderedDict((rpn.num(v), loc_text(loc, k)) for v, k in items)


def area_and_panel(path, label):
    if label == 'COCKPIT':
        area = path[1] if len(path) > 1 else path[0]
        panel = path[2] if len(path) > 2 else area
        return area, panel
    top = path[1] if len(path) > 1 else path[0]
    return top, top


def var_text(v):
    return '%s:%s' % (v.kind, v.name)


def find_state_var(tt, set_br, click_effects, init):
    """The variable that holds the position: what the tooltip shows the pilot first, then
    what the Set branches write, then what the click code toggles, then what Init reads."""
    for v in rpn.reads(tt):
        if not v.name.upper().startswith('XMLVAR_ANIMATION'):
            return var_text(v), v.unit
    for body in set_br.values():
        for e in rpn.effects(body):
            if e.kind == 'L':
                return 'L:' + e.name, None
    for e in click_effects:
        if e.kind in 'LA' and e.value == 'toggle':
            return '%s:%s' % (e.kind, e.name), e.unit
    for v in rpn.reads(init):
        return var_text(v), v.unit
    return None, None


def literal_only(effs):
    """Keeps H/K events and L/A writes with a literal value. Returns (kept, dropped_count)."""
    kept, dropped = [], 0
    for e in effs:
        if e.kind == 'H' or (e.kind == 'K' and e.value != 'expr'):
            kept.append(e)
        elif e.kind == 'K':
            dropped += 1
        elif e.kind in 'LA' and e.value not in ('expr', 'toggle', ''):
            kept.append(e)
        elif e.kind in 'LA':
            dropped += 1
    return kept, dropped


def dedupe_texts(texts):
    out = []
    for t in texts:
        if t not in out:
            out.append(t)
    return out


def expand_input_event_calls(behavior, effs):
    """A mouse branch that calls another input event (1 (>B:X_Inc)) runs that event's code."""
    out = []
    for e in effs:
        if e.kind != 'B':
            out.append(e)
            continue
        m = re.match(r'^(.*)_(Inc|Dec|Set)$', e.name, re.IGNORECASE)
        target = behavior.inputevent(m.group(1)) if m else None
        if target is None:
            continue
        code = target['%s_code' % m.group(2).lower()]
        out.extend(rpn.effects(re.sub(r'\bp0\b', e.value or '1', code)))
    return out


def _writes_state(text, state_var):
    return text.startswith(state_var + '=') or text.startswith(state_var + ',')


def switch_transitions(behavior, state_var, values, set_br, click, follow_state=False):
    """{position: [effect text]} — the Set branch's literal writes for the position (or a plain
    write of the state variable), then what the cockpit's click code does on its way to that
    position. iniBuilds' own two-position click code ("(L:S) ! (>L:S) (>H:SYS) ...") is resolved
    for the transition with resolve_conditions, so a write that depends on the old or new
    position appears only for the position it belongs to. Event-dispatched template code
    ((M:Event) tests and goto labels) cannot be walked linearly; its events are taken as found
    and the positional H: families are narrowed by filter_for_position. follow_state: the
    control is in FOLLOWS_STATE (resolve_conditions' others_follow_state)."""
    two = len(values) == 2
    out = OrderedDict()
    for v in values:
        texts = []
        if v in set_br:
            kept, _ = literal_only(rpn.effects(set_br[v]))
            texts.extend(rpn.effect_text(e) for e in kept)
        if state_var and state_var.startswith('L:') and not any(_writes_state(t, state_var) for t in texts):
            texts.insert(0, '%s=%s' % (state_var, v))
        if two and state_var and '(M:Event)' not in click:
            old = values[1] if v == values[0] else values[0]
            code = rpn.resolve_conditions(click, state_var, v, old, follow_state)
        else:
            code = click
        mouse = expand_input_event_calls(behavior, rpn.effects(code))
        mouse = [e for e in mouse
                 if not (e.kind in 'LA' and state_var and ('%s:%s' % (e.kind, e.name)).upper() == state_var.upper())]
        kept, _ = literal_only(mouse)
        if not two or '(M:Event)' in click:
            kept = [e for e in kept if e.kind in 'HK']
        texts.extend(rpn.effect_text(e) for e in rpn.filter_for_position(kept, v))
        out[v] = dedupe_texts(texts)
    return out


def range_values(rng):
    lo, hi = int(rng[0]), int(rng[1])
    return [str(v) for v in range(lo, hi + 1)]


def new_entry(node, label, area, panel, title, ie):
    return OrderedDict([
        ('id', node), ('file', label), ('area', area), ('panel', panel), ('title', title),
        ('kind', 'none'), ('state_var', None), ('state_unit', None), ('positions', OrderedDict()),
        ('input_event', ie['id'] if ie else None), ('transitions', OrderedDict()),
        ('press', []), ('release', []), ('inc', []), ('dec', []), ('set_template', []),
        ('range', None), ('rest', None), ('note', None)])


def build_control(behavior, mr, ie, loc, label):
    path = behavior.path(mr['owner'])
    node = (mr['highlight'] or (path[-1] if path else '')).upper()
    area, panel = area_and_panel(path, label)
    title = loc_text(loc, mr['titles'][0]) if mr['titles'] else ''
    entry = new_entry(node, label, area, panel, title, ie)

    click = mr['codes'].get('IMDefault') or mr['codes'].get('IMDrag') or ''
    branches = rpn.mouse_branches(click)
    click_effects = expand_input_event_calls(behavior, rpn.effects(click))
    set_code = ie['set_code'] if ie else ''
    set_br = rpn.set_branches(set_code)
    rng = rpn.set_range(set_code)
    tt = ie['tt_value'] if ie else ''
    units = (ie['units'] if ie else '').lower()
    state_var, state_unit = find_state_var(tt, set_br, click_effects, ie['init'] if ie else '')

    # 1. Momentary push-buttons: the Set code only animates; Lock is the press, Unlock the release.
    if (ie and rpn.is_push_template(set_code)) or (not ie and 'Lock' in branches and 'Unlock' in branches):
        press_src = branches.get('Lock', click) if branches else click
        press, dropped = literal_only(expand_input_event_calls(behavior, rpn.effects(press_src)))
        release, _ = literal_only(rpn.effects(branches.get('Unlock', '')))
        entry['press'] = dedupe_texts(rpn.effect_text(e) for e in press)
        entry['release'] = dedupe_texts(rpn.effect_text(e) for e in release)
        entry['kind'] = 'button' if entry['press'] else 'none'
        if dropped:
            entry['note'] = 'press computes a value the map cannot replay'
            entry['kind'] = 'none'
        return entry

    # 2. Potentiometer knobs: Set clamps the value and writes it straight to the L:var.
    if ie and units == 'percent':
        writes = [e for e in rpn.effects(set_code) if e.kind == 'L']
        if writes:
            entry['kind'] = 'knob'
            entry['state_var'] = 'L:' + writes[0].name
            entry['range'] = list(rng) if rng else [0.0, 100.0]
            entry['set_template'] = ['L:%s={v}' % writes[0].name]
        return entry

    # 3. Switches and selectors with Set code: one absolute transition per position.
    if ie and rng and rng[1] > rng[0]:
        positions = positions_from_tooltip(tt, loc)
        values = range_values(rng)
        entry['kind'] = 'switch'
        entry['state_var'], entry['state_unit'] = state_var, state_unit
        entry['positions'] = OrderedDict((v, positions.get(v, '')) for v in values)
        entry['transitions'] = switch_transitions(behavior, state_var, values, set_br, click, node in FOLLOWS_STATE)
        if any(e.kind in 'LA' and e.value == 'expr' for e in click_effects):
            entry['note'] = 'click code also computes a value; only literal writes are replayed'
        return entry

    # 4. Mouse-only multi-position switches (template code that writes one literal value per
    #    detent, such as the weather-radar range knob and the fire-bottle discharge switches).
    literal_writes = OrderedDict()
    for e in click_effects:
        if e.kind == 'L' and e.value not in ('expr', 'toggle', '') and not e.name.upper().startswith('XMLVAR_'):
            literal_writes.setdefault(e.name, [])
            if e.value not in literal_writes[e.name]:
                literal_writes[e.name].append(e.value)
    multi = [(n, vals) for n, vals in literal_writes.items() if len(vals) >= 2]
    if not ie and multi:
        name, vals = multi[0]
        values = sorted(vals, key=float)
        entry['kind'] = 'switch'
        entry['state_var'] = 'L:' + name
        entry['positions'] = OrderedDict((v, '') for v in values)
        entry['transitions'] = OrderedDict((v, ['L:%s=%s' % (name, v)]) for v in values)
        return entry

    # 5. Click-toggle controls with no input event: two positions, 0 and 1.
    toggles = [e for e in click_effects if e.kind in 'LA' and e.value == 'toggle']
    if not ie and toggles and not branches:
        tv = toggles[0]
        entry['kind'] = 'switch'
        entry['state_var'] = '%s:%s' % (tv.kind, tv.name)
        entry['state_unit'] = tv.unit
        entry['positions'] = OrderedDict([('0', ''), ('1', '')])
        entry['transitions'] = switch_transitions(behavior, entry['state_var'], ['0', '1'], {}, click,
                                                  node in FOLLOWS_STATE)
        return entry

    # 6. Encoders: the wheel branches step the value.
    up = expand_input_event_calls(behavior, rpn.effects(branches.get('WheelUp', '')))
    down = expand_input_event_calls(behavior, rpn.effects(branches.get('WheelDown', '')))
    if up or down:
        inc, dropped_up = literal_only(up)
        dec, dropped_down = literal_only(down)
        if dropped_up or dropped_down:
            entry['note'] = 'knob computes a value the map cannot replay'
            return entry
        if inc or dec:
            entry['kind'] = 'encoder'
            entry['inc'] = dedupe_texts(rpn.effect_text(e) for e in inc)
            entry['dec'] = dedupe_texts(rpn.effect_text(e) for e in dec)
        return entry

    entry['note'] = 'no replayable action found'
    return entry


def apply_curation(entry):
    cid = entry['id']
    if entry['kind'] == 'switch' and not any(entry['transitions'].values()):
        # A position that writes nothing (a momentary jog switch whose click code only computes,
        # such as the outflow-valve position switches) cannot be offered as a position.
        entry['kind'] = 'none'
        entry['note'] = 'positions write nothing replayable'
        return entry
    if cid in LATCH_BUTTONS and '1' in entry['transitions']:
        entry['kind'] = 'latch'
        entry['press'] = list(entry['transitions']['1'])
    if cid in SPRING_SWITCHES and entry['kind'] == 'switch':
        entry['kind'] = 'spring'
        entry['rest'] = float(SPRING_SWITCHES[cid])
    return entry


def build_breaker(behavior, mr, ie, loc, index):
    set_br = rpn.set_branches(ie['set_code'])
    click = mr['codes'].get('IMDefault') or ''
    click_effects = rpn.effects(click)
    state_var, _ = find_state_var(ie['tt_value'], set_br, click_effects, ie['init'])
    return OrderedDict([
        ('index', index), ('input_event', ie['id']),
        ('title', loc_text(loc, mr['titles'][0]) if mr['titles'] else ''),
        ('state_var', state_var),
        ('transitions', switch_transitions(behavior, state_var, ['0', '1'], set_br, click))])


def build_map(behaviors, loc, package_version=''):
    """behaviors: list of (Behavior, file label). Returns the map as an OrderedDict."""
    controls, breakers, lamps, seen = [], [], set(), set()
    for behavior, label in behaviors:
        for code in behavior.material_codes:
            for v in rpn.reads(code, 'L'):
                lamps.add(v.name)
        for mr in behavior.mouserects:
            ie = None
            for e in mr['entries']:
                ie = behavior.inputevent(e)
                if ie:
                    break
            if ie and BREAKER_RE.match(ie['id']):
                breakers.append(build_breaker(behavior, mr, ie, loc, int(BREAKER_RE.match(ie['id']).group(1))))
                continue
            node = (mr['highlight'] or '').upper()
            if not node or node.endswith('_SEQ1_NEW') or node in seen:
                continue
            seen.add(node)
            controls.append(apply_curation(build_control(behavior, mr, ie, loc, label)))
    breakers.sort(key=lambda b: b['index'])
    return OrderedDict([
        ('generator', 'tools/l1011-gen/generate_l1011_map.py'),
        ('package', 'inibuilds-aircraft-l1011'),
        ('package_version', package_version),
        ('controls', controls),
        ('breakers', breakers),
        ('lamps', sorted(lamps, key=str.upper))])


def to_json(data):
    return json.dumps(data, indent=1, ensure_ascii=False) + '\n'


def main(argv=None):
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    ap.add_argument('--package', required=True, help='the inibuilds-aircraft-l1011 package folder')
    ap.add_argument('--out', default=DEFAULT_OUT)
    args = ap.parse_args(argv)
    with open(os.path.join(args.package, 'manifest.json'), 'r', encoding='utf-8-sig') as f:
        version = json.load(f).get('package_version', '')
    loc = load_loc(args.package)
    behaviors = [(Behavior.from_file(os.path.join(args.package, BEHAVIOR_DIR, rel)), label)
                 for rel, label in COCKPIT_FILES]
    data = build_map(behaviors, loc, version)
    with open(args.out, 'w', encoding='utf-8', newline='\n') as f:
        f.write(to_json(data))
    kinds = {}
    for c in data['controls']:
        kinds[c['kind']] = kinds.get(c['kind'], 0) + 1
    print('controls %d %s, breakers %d, lamps %d -> %s' % (
        len(data['controls']), dict(sorted(kinds.items())), len(data['breakers']), len(data['lamps']), args.out))
    return 0


if __name__ == '__main__':
    sys.exit(main())
