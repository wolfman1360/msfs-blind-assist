r"""Reports what changed between two L-1011 control maps, for reviewing a regeneration.

    git show HEAD:MSFSBlindAssist/Resources/l1011_control_map.json > %TEMP%\old_map.json
    python tools/l1011-gen/diff_maps.py %TEMP%\old_map.json MSFSBlindAssist/Resources/l1011_control_map.json

Every line it prints is something a panel row, a spoken word or a cockpit write may have
changed for; read each one before committing the new map.
"""
import json
import sys

COMPARED = ('kind', 'state_var', 'positions', 'transitions', 'press', 'release', 'inc', 'dec',
            'set_template', 'range', 'rest', 'title')


def diff(old, new):
    lines = []
    old_by = {c['id']: c for c in old.get('controls', [])}
    new_by = {c['id']: c for c in new.get('controls', [])}
    for cid in sorted(set(new_by) - set(old_by)):
        lines.append('added   %s (%s)' % (cid, new_by[cid]['kind']))
    for cid in sorted(set(old_by) - set(new_by)):
        lines.append('removed %s (%s)' % (cid, old_by[cid]['kind']))
    for cid in sorted(set(old_by) & set(new_by)):
        for key in COMPARED:
            if old_by[cid].get(key) != new_by[cid].get(key):
                lines.append('changed %s %s: %s -> %s' % (cid, key, json.dumps(old_by[cid].get(key)),
                                                          json.dumps(new_by[cid].get(key))))
    old_b = {b['index']: b for b in old.get('breakers', [])}
    new_b = {b['index']: b for b in new.get('breakers', [])}
    if set(old_b) != set(new_b):
        lines.append('breakers %d -> %d' % (len(old_b), len(new_b)))
    for i in sorted(set(old_b) & set(new_b)):
        if old_b[i] != new_b[i]:
            lines.append('changed breaker %d' % i)
    added_lamps = sorted(set(new.get('lamps', [])) - set(old.get('lamps', [])))
    removed_lamps = sorted(set(old.get('lamps', [])) - set(new.get('lamps', [])))
    if added_lamps:
        lines.append('lamps added: ' + ', '.join(added_lamps))
    if removed_lamps:
        lines.append('lamps removed: ' + ', '.join(removed_lamps))
    return lines


def main(argv=None):
    argv = sys.argv[1:] if argv is None else argv
    if len(argv) != 2:
        print(__doc__)
        return 2
    with open(argv[0], encoding='utf-8') as f:
        old = json.load(f)
    with open(argv[1], encoding='utf-8') as f:
        new = json.load(f)
    lines = diff(old, new)
    print('\n'.join(lines) if lines else 'no differences')
    return 0


if __name__ == '__main__':
    sys.exit(main())
