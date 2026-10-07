"""Resumen de balance azul/rojo de un log del servidor del MOBA.

Uso: python tools/moba-balance-report.py <log> [<log> ...]

Lee las lineas [MOBA-KILL] y [MOBA-SCORE] y cuenta, por equipo: muertes de campeones segun quien las causo
(campeon enemigo, creeps enemigos, torreta enemiga, monstruo de jungla), nivel medio por ventana de 5 min y
las primeras muertes con su lugar. Sirve para ver si un equipo gana siempre (snowball / asimetria) sin leer el log a mano.
"""
import re
import sys
from collections import Counter, defaultdict

KILL = re.compile(r'^(\d{4}-\d\d-\d\d \d\d:\d\d:\d\d).*\[MOBA-KILL\] "([^"]+)" \(Lv(\d+) .*?\) killed by "([^"]+)" @ (\d+),(\d+)')
SCORE = re.compile(r'\[MOBA-SCORE\] t=(\d+)s (Blue|Red) "[^"]+" "[^"]+" Lv(\d+) KDA=(\d+)/(\d+)/(\d+)')
BOT = re.compile(r'^([br])\d+_\d+$')


def team_of_champion(name):
    m = BOT.match(name)
    return {'b': 'Blue', 'r': 'Red'}[m.group(1)] if m else 'GM'


def classify_killer(killer, x, y):
    """Devuelve (equipo del asesino, tipo)."""
    champ = BOT.match(killer)
    if champ:
        return ({'b': 'Blue', 'r': 'Red'}[champ.group(1)], 'campeon')
    head = killer.split(' - Id')[0]
    if head in ('Spider', 'Worm'):
        return ('Blue', 'creeps')
    if head in ('Goblin', 'Strange Rabbit'):
        return ('Red', 'creeps')
    if head == 'Stone Golem':
        return ('Blue' if y < 128 else 'Red', 'torreta')
    return ('-', head.lower())


def report(path):
    deaths = []
    scores = defaultdict(lambda: defaultdict(list))
    start = None
    for line in open(path, encoding='utf-8', errors='ignore'):
        if '[MOBA-FIGHT]' in line and start is None:
            start = line[:19]
        m = KILL.match(line)
        if m:
            stamp, victim, level, killer, x, y = m.groups()
            deaths.append((stamp, victim, int(level), killer, int(x), int(y)))
            continue
        s = SCORE.search(line)
        if s:
            t, team, level = int(s.group(1)), s.group(2), int(s.group(3))
            scores[team][t // 300].append(level)

    print(f'== {path}  (inicio {start})  muertes de campeones: {len(deaths)}')
    by_victim = Counter()
    by_cause = defaultdict(Counter)
    for stamp, victim, level, killer, x, y in deaths:
        vt = team_of_champion(victim)
        kt, kind = classify_killer(killer, x, y)
        by_victim[vt] += 1
        by_cause[vt][f'{kind} {kt}'] += 1
    for team in ('Blue', 'Red', 'GM'):
        if by_victim[team]:
            causes = ', '.join(f'{k}: {v}' for k, v in by_cause[team].most_common())
            print(f'  muertes de {team}: {by_victim[team]}  ({causes})')
    blue_kills = sum(v for k, v in by_cause['Red'].items() if k.startswith('campeon Blue'))
    red_kills = sum(v for k, v in by_cause['Blue'].items() if k.startswith('campeon Red'))
    print(f'  kills campeon-vs-campeon: Azul {blue_kills} / Rojo {red_kills}')
    if scores:
        print('  nivel medio por ventana de 5 min (Azul / Rojo):')
        for window in sorted(set(w for t in scores.values() for w in t)):
            b = scores['Blue'].get(window, [])
            r = scores['Red'].get(window, [])
            fb = f'{sum(b) / len(b):.1f}' if b else '-'
            fr = f'{sum(r) / len(r):.1f}' if r else '-'
            print(f'    min {window * 5:>3}-{window * 5 + 5:<3}: {fb} / {fr}')
    print('  primeras 10 muertes (hora, victima, nivel, lugar, asesino):')
    for stamp, victim, level, killer, x, y in deaths[:10]:
        print(f'    {stamp[11:]}  {victim:<10} Lv{level} @({x},{y})  {killer.split(" - Id")[0]}')


if __name__ == '__main__':
    for log in sys.argv[1:]:
        report(log)
