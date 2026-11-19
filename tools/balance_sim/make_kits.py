"""Genera kits.json con el kit real de cada clase (las 6 skills por defecto del MOBA) leyendo SKILLS_REGISTRO.md (que sale del codigo del servidor).
Uso: python -I -X utf8 make_kits.py   (volver a correrlo si cambia el registro de skills)."""
import json, re
from pathlib import Path

REG = Path(__file__).resolve().parent.parent.parent / "SKILLS_REGISTRO.md"
SECTIONS = {"Dark Wizard": "GM", "Dark Knight": "BK", "Fairy Elf": "FE", "Magic Gladiator": "MG", "Dark Lord": "DL", "Summoner": "SUM", "Rage Fighter": "RF"}
RANGE = {"BK": 2, "RF": 2, "DL": 4, "MG": 3, "FE": 6, "GM": 6, "SUM": 6}


def num(x):
    m = re.search(r"[\d.]+", x)
    return float(m.group()) if m else None


txt = REG.read_text(encoding="utf-8")
kits = {}
parts = re.split(r"^## ", txt, flags=re.M)[1:]
for part in parts:
    head = part.splitlines()[0]
    cls = next((v for k, v in SECTIONS.items() if head.startswith(k)), None)
    if not cls:
        continue
    picks = re.search(r"Picks por defecto.*?:\s*(.+)", part).group(1)
    wanted = [int(n) for n in re.findall(r"(\d+)\s+[A-Z]", picks)]
    rows = {}
    for line in part.splitlines():
        c = [x.strip() for x in line.strip().strip("|").split("|")]
        if len(c) >= 10 and c[0].isdigit():
            rows[int(c[0])] = c
    skills = []
    for n in wanted:
        c = rows[n]
        d = [num(x) for x in c[6].split("/")]
        cd = [num(x) for x in c[8].split("/")]
        skills.append({"n": n, "nombre": c[1], "area": "Area" in c[2], "alcance": int(c[4]) if c[4].isdigit() else RANGE[cls], "mana": num(c[5]) or 0.0,
                       "dano_r1": d[0], "dano_r5": d[1], "escala": num(c[7]), "cd_r1": cd[0], "cd_r5": cd[1]})
    kits[cls] = skills
Path(__file__).with_name("kits.json").write_text(json.dumps(kits, indent=1, ensure_ascii=False), encoding="utf-8")
print({k: [(s["nombre"], s["dano_r1"], s["dano_r5"], s["cd_r1"], s["cd_r5"]) for s in v][:2] for k, v in kits.items()})
