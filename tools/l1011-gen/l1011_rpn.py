"""Pure helpers for the RPN ("reverse Polish") code inside the iniBuilds L-1011 behavior XML.

The cockpit's click code is the authority on what a control does: it writes the switch's
L:var and then fires the H: events the HTML gauges listen for (H:ELECTRICAL, H:FUEL,
H:AUTOPILOT_SWITCH_AFCS_HDG ...). The input event's own <Set> code writes only the L:var, so a
write through it alone leaves the systems unchanged (design doc section 4.4). These helpers
pull the literal writes and events out of that code so the generator can record, per control
and per position, exactly what to replay.
"""
import re
from collections import OrderedDict, defaultdict, namedtuple

TOKEN_RE = re.compile(r"\(\*.*?\*\)|\((?:>)?[A-Za-z]:[^()]*?\)|'[^']*'(?:_n)?|\S+")
VAR_RE = re.compile(r"^\((>)?([A-Za-z]):(.*)\)$")
NUM_RE = re.compile(r"^-?\d+(?:\.\d+)?$")
GOTO_RE = re.compile(r"^g\d+$")
PUSH_TEMPLATE_RE = re.compile(r"^\s*p0\s+p0\s+if\{.*\}\s*els\{.*\}\s+1\s+\(>O:_ButtonAnimVar\)\s*$")
RANGE_MIN_MAX_RE = re.compile(r"\s*p0\s+(-?\d+(?:\.\d+)?)\s+min\s+(-?\d+(?:\.\d+)?)\s+max")
RANGE_MAX_MIN_RE = re.compile(r"\s*p0\s+(-?\d+(?:\.\d+)?)\s+max\s+(-?\d+(?:\.\d+)?)\s+min")
SUFFIX_RE = re.compile(r"^(.*)_(\d+)$")

Var = namedtuple('Var', 'write kind name unit')
Effect = namedtuple('Effect', 'kind name value unit')


def tokens(code):
    return TOKEN_RE.findall(code or '')


def parse_var(tok):
    m = VAR_RE.match(tok)
    if not m:
        return None
    kind = m.group(2).upper()
    body = m.group(3).strip()
    unit = None
    if kind in ('A', 'L', 'B', 'E') and ',' in body:
        name, unit = body.split(',', 1)
        name, unit = name.strip(), unit.strip()
    else:
        name = body
    return Var(m.group(1) == '>', kind, name, unit)


def num(text):
    f = float(text)
    return str(int(f)) if f.is_integer() else text


def reads(code, kinds='LA'):
    out, seen = [], set()
    for tok in tokens(code):
        v = parse_var(tok)
        if v is not None and not v.write and v.kind in kinds and v.name:
            key = (v.kind, v.name.upper())
            if key not in seen:
                seen.add(key)
                out.append(v)
    return out


def effects(code):
    """Every L/A/H/K/B write in the code, in order, with its literal value when there is one."""
    out = []
    t = tokens(code)
    for i, tok in enumerate(t):
        v = parse_var(tok)
        if v is None or not v.write or v.kind not in 'LAHKB' or not v.name:
            continue
        lits = []
        j = i - 1
        while j >= 0 and NUM_RE.match(t[j]):
            lits.insert(0, t[j])
            j -= 1
        if v.kind in 'LA':
            if lits:
                value = num(lits[-1])
            elif i >= 2 and t[i - 1] == '!':
                prev = parse_var(t[i - 2])
                value = 'toggle' if prev is not None and not prev.write and prev.name.upper() == v.name.upper() else 'expr'
            else:
                value = 'expr'
        elif v.kind == 'K':
            value = _event_params(t, i, 2 if v.name.startswith('2:') else 1)
        elif v.kind == 'B':
            value = num(lits[-1]) if lits else ''
        else:
            value = ''
        out.append(Effect(v.kind, v.name, value, v.unit))
    return out


COMPONENT_RE = re.compile(r"^'[^']*'_n$")
NO_VALUE_TOKENS = ('if{', 'els{', '}', 'quit')
LABEL_RE = re.compile(r"^(?::\d+|g\d+)$")


def _pushes_value(tok):
    return not (tok in NO_VALUE_TOKENS or tok.startswith('(>') or LABEL_RE.match(tok))


def _event_params(t, i, count):
    """The literal parameters of the K: event at t[i]: numbers or MSFS 2024 component
    references such as 'Slats'_n. 'expr' when a computed value feeds it, '' when nothing does."""
    ops = []
    j = i - 1
    while j >= 0 and len(ops) < count and (NUM_RE.match(t[j]) or COMPONENT_RE.match(t[j])):
        ops.insert(0, num(t[j]) if NUM_RE.match(t[j]) else t[j])
        j -= 1
    if len(ops) < count and j >= 0 and _pushes_value(t[j]):
        return 'expr'
    return ','.join(ops)


def _block_end(t, opener):
    """t[opener] ends with '{'. Returns (body tokens, index just after the matching '}')."""
    depth, body, i = 1, [], opener + 1
    while i < len(t):
        tok = t[i]
        if tok.endswith('{'):
            depth += 1
        elif tok == '}':
            depth -= 1
            if depth == 0:
                return body, i + 1
        body.append(tok)
        i += 1
    return body, i


def _block(t, opener):
    return _block_end(t, opener)[0]


def mouse_branches(code):
    """{mouse event: body} for every "(M:Event) 'NAME' scmi 0 == if{ BODY }" test."""
    t = tokens(code)
    out = OrderedDict()
    for i in range(len(t) - 5):
        if (t[i] == '(M:Event)' and t[i + 2] == 'scmi' and t[i + 3] == '0'
                and t[i + 4] == '==' and t[i + 5] == 'if{'):
            out.setdefault(t[i + 1].strip("'"), ' '.join(_block(t, i + 5)))
    return out


def set_branches(set_code):
    """{position: body} for each "l0 N == if{ BODY g1 }" branch of an input event's Set code."""
    t = tokens(set_code)
    out = OrderedDict()
    for i in range(len(t) - 3):
        if t[i] == 'l0' and NUM_RE.match(t[i + 1]) and t[i + 2] == '==' and t[i + 3] == 'if{':
            body = [x for x in _block(t, i + 3) if not GOTO_RE.match(x)]
            out.setdefault(num(t[i + 1]), ' '.join(body))
    return out


def set_range(set_code):
    m = RANGE_MIN_MAX_RE.match(set_code or '')
    if m:
        return float(m.group(2)), float(m.group(1))
    m = RANGE_MAX_MIN_RE.match(set_code or '')
    if m:
        return float(m.group(1)), float(m.group(2))
    return None


def is_push_template(set_code):
    return bool(PUSH_TEMPLATE_RE.match(set_code or ''))


def positional_families(names):
    """Base names that appear with at least two different _N suffixes: X_0, X_1 -> {'X'}."""
    fam = defaultdict(set)
    for n in names:
        m = SUFFIX_RE.match(n)
        if m:
            fam[m.group(1)].add(m.group(2))
    return {base for base, suffixes in fam.items() if len(suffixes) >= 2}


def filter_for_position(effs, value):
    """Drops the members of a positional H: family that do not belong to this position."""
    fams = positional_families([e.name for e in effs if e.kind == 'H'])
    out = []
    for e in effs:
        if e.kind == 'H':
            m = SUFFIX_RE.match(e.name)
            if m and m.group(1) in fams and num(m.group(2)) != value:
                continue
        out.append(e)
    return out


def effect_text(e, value=None):
    if e.kind == 'H':
        return 'H:' + e.name
    if e.kind == 'K':
        return 'K:' + e.name + ('=' + e.value if e.value else '')
    name = e.name + (', ' + e.unit if e.unit else '')
    return '%s:%s=%s' % (e.kind, name, e.value if value is None else value)


COMPARE = {'==': lambda a, b: a == b, '!=': lambda a, b: a != b, '>': lambda a, b: a > b,
           '<': lambda a, b: a < b, '>=': lambda a, b: a >= b, '<=': lambda a, b: a <= b}


def _same(v, kind_name):
    return v is not None and ('%s:%s' % (v.kind, v.name)).upper() == kind_name.upper()


def resolve_conditions(code, state, new_value, old_value, others_follow_state):
    """Rewrites a two-way click code for ONE known transition so that every condition this
    function can decide is replaced by the branch that would run.

    state: 'L:NAME' (or 'A:NAME') of the control's position variable. new_value/old_value:
    position text ('0', '1'...); old_value may be None (unknown). The state variable reads
    old_value until the code toggles it ("(L:S) ! (>L:S)") or writes a literal to it, and
    new_value after. others_follow_state: for a two-position switch, a condition on ANY other
    variable read before the toggle is decided as if that variable matched the switch (the
    iniBuilds click code flips the stock system to the switch's new position this way:
    "(A:TURB ENG IGNITION SWITCH:1, bool) ! if{ 1 (>K:..SET1) } els{ 0 (>K:..SET1) }").
    A condition it cannot decide is dropped together with both its branches."""
    t = tokens(code)
    out = []
    cur = old_value
    i = 0
    while i < len(t):
        v = parse_var(t[i])
        if v is not None and not v.write and _same(v, state) and i + 2 < len(t) and t[i + 1] == '!'                 and _same(parse_var(t[i + 2]), state) and parse_var(t[i + 2]).write:
            out.extend(t[i:i + 3])
            cur = new_value
            i += 3
            continue
        if v is not None and v.write and _same(v, state):
            if out and NUM_RE.match(out[-1]):
                cur = num(out[-1])
            out.append(t[i])
            i += 1
            continue
        if v is not None and not v.write and v.kind in 'LA':
            reading = cur if _same(v, state) else (cur if others_follow_state else None)
            j = i + 1
            negate = False
            op = None
            operand = None
            if j < len(t) and t[j] == '!':
                negate, j = True, j + 1
            elif j + 1 < len(t) and NUM_RE.match(t[j]) and t[j + 1] in COMPARE:
                operand, op, j = float(t[j]), t[j + 1], j + 2
            if j < len(t) and t[j] == 'if{':
                if_body, end = _block_end(t, j)
                else_body = []
                if end < len(t) and t[end] == 'els{':
                    else_body, end = _block_end(t, end)
                if reading is None:
                    i = end
                    continue
                value = float(reading)
                if op is not None:
                    truth = COMPARE[op](value, operand)
                elif negate:
                    truth = value == 0
                else:
                    truth = value != 0
                chosen = if_body if truth else else_body
                out.extend(tokens(resolve_conditions(' '.join(chosen), state, new_value, cur, others_follow_state)))
                i = end
                continue
            if _same(v, state) and cur is not None:
                out.append(cur)
                i += 1
                continue
        out.append(t[i])
        i += 1
    return ' '.join(out)
