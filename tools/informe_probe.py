"""Convierte el informe JSON de /mobaskilltest en INFORME_SKILLS.md (tablas por clase + anomalias + tiempo para matar).

Uso: python -I tools/informe_probe.py [ruta_al_skilltest_*.json]   (sin argumento usa el mas reciente de src/Startup/bin/Debug)
"""
import glob
import json
import statistics
import sys
from pathlib import Path

RAIZ = Path(__file__).resolve().parent.parent
sys.path.insert(0, str(Path(__file__).resolve().parent))
import skills_registro as sr  # noqa: E402

EHP_BK = None  # se toma del informe (vida + escudo del muñeco BK nivel 30)
EHP_GENERICO = 60000
SEG_BASICO = 1.0


def cargar(ruta):
    if not ruta:
        ruta = sorted(glob.glob(str(RAIZ / "src/Startup/bin/Debug/skilltest_*.json")))[-1]
    return ruta, json.loads(Path(ruta).read_text(encoding="utf-8"))


def nombre(x):
    return x["Value"] if isinstance(x, dict) else str(x)


def por_skill(rows):
    d = {}
    for r in rows:
        d.setdefault(r.get("skill"), []).append(r)
    return d


def prom(v):
    return f"{v:,.0f}".replace(",", ".") if isinstance(v, (int, float)) else "-"


def simular_ttk(skills, mana_max, regen, ehp, dt=0.25, limite=300):
    """Rotacion codiciosa: cada 0,25 s lanza la skill lista de mayor daño efectivo por casilla de enfriamiento; sin maná, ataque basico."""
    t, dano, mana, listos, sin_mana = 0.0, 0.0, float(mana_max), {s["n"]: 0.0 for s in skills}, 0.0
    orden = sorted(skills, key=lambda s: -s["eff"] / max(s["cd"], 0.5))
    proximo_basico = 0.0
    while t < limite and dano < ehp:
        lanzada = False
        for s in orden:
            if listos[s["n"]] <= t and mana >= s["mana"]:
                mana -= s["mana"]
                dano += s["eff"]
                listos[s["n"]] = t + s["cd"]
                lanzada = True
                break
        if not lanzada and t >= proximo_basico:
            dano += skills[0].get("basico", 0)
            proximo_basico = t + SEG_BASICO
            if mana < min(s["mana"] for s in skills):
                sin_mana += dt
        mana = min(mana_max, mana + regen * dt)
        t += dt
    return (round(t, 1) if dano >= ehp else None), round(sin_mana, 1)


def main():
    ruta, d = cargar(sys.argv[1] if len(sys.argv) > 1 else None)
    kit_reg, defecto, buffs_reg, summons_reg = sr.kits()
    mu = sr.skills_de_mu(sr.numeros_skill())
    L = [f"# Informe de skills (medido en el servidor, nivel {d['level']})", "",
         f"Fuente: `{Path(ruta).name}` · duración de la medición: {d['seconds']} s · generado por `tools/informe_probe.py` desde `/mobaskilltest`.", "",
         "**Cómo leer:** *raw* = daño por golpe antes de la mitigación (el que dice la tabla × escala de nivel); *efectivo* = vida + escudo que de verdad pierde un muñeco BK nivel 30 con todo su equipo base. "
         "*modelo* = lo que predice `MobaSkillDamage` a ese nivel sin stats ni ítems. *ratio* = raw medido ÷ punto medio del modelo.", ""]
    anomalias = []
    resumen_ttk = []
    for c in d["classes"]:
        if "error" in c:
            L += [f"## Clase {c['class']}: ERROR", f"`{c['error']}`", ""]
            anomalias.append(f"Clase {c['class']}: la medición falló ({c['error']}).")
            continue
        cn = nombre(c["className"])
        L += [f"## {cn} (clase {c['class']}, familia {c['family']})", "",
              f"Vida {prom(c['maxHealth'])} · maná {prom(c['maxMana'])} (regen {c['manaRegenPerSecond']}/s) · combo definido: **{'sí' if c['comboDefined'] else 'no'}** · muñeco objetivo: {prom(c['targetMaxHealth'])} vida + {prom(c['targetMaxShield'])} escudo", ""]
        rows = c.get("rows", [])
        ehp_bk = c["targetMaxHealth"] + c["targetMaxShield"]
        sk = por_skill(rows)
        filas = []
        sim_skills = []
        for n, rs in sorted(sk.items()):
            dmg = {r["rank"]: r for r in rs if r["test"] == "dmg"}
            if not dmg:
                continue
            r1, r5 = dmg.get(1), dmg.get(5)
            rng = [r for r in rs if r["test"] == "range"]
            hasta = [r["distance"] for r in rng if r.get("hitsDirect", 0) > 0]
            declarado = r1["declaredRange"]
            med1 = (r1["modelMin"] + r1["modelMax"]) / 2
            med5 = (r5["modelMin"] + r5["modelMax"]) / 2
            ratio1 = round(r1["rawAvg"] / med1, 2) if r1.get("rawAvg") and med1 else None
            ratio5 = round(r5["rawAvg"] / med5, 2) if r5.get("rawAvg") and med5 else None
            prog = round(r5["rawAvg"] / r1["rawAvg"], 2) if r1.get("rawAvg") and r5.get("rawAvg") else None
            eff1 = (r1["hpTotal"] + r1["sdTotal"])
            eff5 = (r5["hpTotal"] + r5["sdTotal"])
            cd1, cd5 = r1.get("cooldownSeconds"), r5.get("cooldownSeconds")
            efectos = sorted({e.split(":")[1] for r in (r1, r5) for lst in r.get("targetEffects", []) for e in lst.split(",") if e})
            filas.append((n, nombre(r1["name"]), r1["type"].replace("AreaSkill", "Area"), r1["manaSpent"], cd1, cd5, r1.get("rawAvg"), r5.get("rawAvg"), ratio1, ratio5, prog,
                          eff5, max(r1["victims"], r5["victims"]), r5["maxPctOfMaxHp"], declarado, hasta, efectos, r1["hitsDirect"], r5["hitsDirect"], r1["cast"] and r5["cast"]))
            if not (r1["cast"] and r5["cast"]):
                anomalias.append(f"{cn}: {n} {nombre(r1['name'])} no se lanzó desde el servidor (cast=False).")
            if ratio1 and not 0.55 <= ratio1 <= 1.35:
                anomalias.append(f"{cn}: {n} {nombre(r1['name'])} rango 1 pega {int(ratio1 * 100)} % del modelo.")
            if ratio5 and not 0.65 <= ratio5 <= 1.35:
                anomalias.append(f"{cn}: {n} {nombre(r1['name'])} rango 5 pega {int(ratio5 * 100)} % del modelo.")
            if prog and prog < 1.5:
                anomalias.append(f"{cn}: {n} {nombre(r1['name'])} casi no mejora de rango 1 a 5 (×{prog}).")
            if r1["manaSpent"] == 0:
                anomalias.append(f"{cn}: {n} {nombre(r1['name'])} no gasta maná (manaSpent=0).")
            reg = sr.FLAT and None
            if cd5 and cd5 > 0:
                sim_skills.append({"n": n, "eff": eff5 / max(r5["hitsDirect"], 1) if False else eff5, "cd": cd5, "mana": r5["manaSpent"] or 0})
        L.append("| Nº | Skill | Tipo | Maná | CD r1/r5 | raw r1 → r5 | ratio r1/r5 | ×r5/r1 | Golpes r5 | Efectivo r5 | Víctimas | % vida máx. del golpe | Alcance decl. → golpea a | Efectos |")
        L.append("|---|---|---|---|---|---|---|---|---|---|---|---|---|---|")
        for f in filas:
            L.append(f"| {f[0]} | {f[1]} | {f[2]} | {prom(f[3])} | {f[4]}/{f[5]} s | {prom(f[6])} → {prom(f[7])} | {f[8]}/{f[9]} | {f[10]} | {f[18]}/{f[19]} | {prom(f[11])} | {f[12]} | {f[13]} | {f[14]} → {','.join(str(x) for x in f[15]) or 'nada'} | {', '.join(f[16]) or '-'} |")
        L.append("")
        # tiempo para matar
        if sim_skills:
            lvl30 = next((x for x in c.get("levelCurve", []) if x["level"] == d["level"]), None)
            basico = (lvl30["basicMin"] + lvl30["basicMax"]) / 2 * 0.93 if lvl30 else 0
            top = sorted(sim_skills, key=lambda s: -s["eff"] / max(s["cd"], 0.5))[:6]
            top[0]["basico"] = basico
            ttk_bk, sm_bk = simular_ttk(top, c["maxMana"], c["manaRegenPerSecond"], ehp_bk)
            ttk_gen, sm_gen = simular_ttk(top, c["maxMana"], c["manaRegenPerSecond"], EHP_GENERICO)
            dps = sum(s["eff"] / max(s["cd"], 0.5) for s in top)
            L.append(f"**Tiempo para matar** (rotación de sus 6 mejores skills a rango 5, 1 objetivo, sin stats ni ítems): contra {prom(ehp_bk)} de vida+escudo (BK nivel 30) **{ttk_bk if ttk_bk else '> 300'} s**; contra 60.000 **{ttk_gen if ttk_gen else '> 300'} s**. DPS sostenido aprox. {prom(dps)}/s. Tiempo sin maná (BK): {sm_bk} s.")
            resumen_ttk.append((cn, ttk_bk, ttk_gen, round(dps)))
            L.append("")
        # curva de nivel
        if c.get("levelCurve"):
            L.append("Curva por nivel: " + " · ".join(f"L{x['level']}: vida {prom(x['maxHealth'])}, maná {prom(x['maxMana'])}, básico {x['basicMin']}-{x['basicMax']}" for x in c["levelCurve"]))
            L.append("")
        # buffs
        bf = [r for r in rows if r["test"] == "buff"]
        if bf:
            L += ["### Buffs y curas", "", "| Nº | Buff | Rango | Maná | CD | Efecto activo | Atributos que cambian | Daño que hace antes → después | Daño que recibe antes → después | Cura / escudo justo al lanzar | Daño reflejado antes → después |", "|---|---|---|---|---|---|---|---|---|---|---|"]
            for r in bf:
                pd = (r["damageDealtAfter"] / r["damageDealtBefore"] - 1) * 100 if r["damageDealtBefore"] else 0
                pt = (r["damageTakenAfter"] / r["damageTakenBefore"] - 1) * 100 if r["damageTakenBefore"] else 0
                cambios = "; ".join(f"{k}: {v}" for k, v in r.get("attributeChanges", {}).items()) or "-"
                L.append(f"| {r['skill']} | {nombre(r['name'])} | {r['rank']} | {prom(r['manaSpent'])} | {r['cooldownSeconds']} s | {r.get('effects') or '-'} | {cambios} | {prom(r['damageDealtBefore'])} → {prom(r['damageDealtAfter'])} ({pd:+.0f} %) | {prom(r['damageTakenBefore'])} → {prom(r['damageTakenAfter'])} ({pt:+.0f} %) | +{prom(r['hpHealed'])} vida / {prom(r['shieldGained'])} escudo | {prom(r.get('reflectedBefore'))} → {prom(r.get('reflectedAfter'))} |")
            L.append("")
            for n, rs in por_skill(bf).items():
                a = {r["rank"]: r for r in rs}
                if 1 in a and 5 in a:
                    if a[1].get("attributeChanges") == a[5].get("attributeChanges") and a[1].get("attributeChanges"):
                        anomalias.append(f"{cn}: el buff {n} {nombre(a[1]['name'])} produce los MISMOS cambios de atributos a rango 1 y 5 (solo cambian duración/enfriamiento).")
                    if not a[1].get("attributeChanges") and not a[1].get("effects"):
                        anomalias.append(f"{cn}: el buff {n} {nombre(a[1]['name'])} no deja ningún efecto activo ni cambia atributos.")
        # invocaciones
        inv = [r for r in rows if r["test"] == "summon"]
        if inv:
            L.append("Invocaciones: " + ", ".join(f"{r['skill']} {nombre(r['name'])} maná {prom(r['manaSpent'])} creada={r.get('summonCreated')}" for r in inv))
            L.append("")
        saltadas = [r for r in rows if str(r["test"]).startswith("skip")]
        if saltadas:
            L.append("No medibles en el servidor: " + ", ".join(f"{r['skill']} {nombre(r['name'])}" for r in saltadas))
            L.append("")
        # combo
        cb = c.get("combo", {})
        if cb.get("sequences"):
            L += ["### Combo", ""]
            for sq in cb["sequences"]:
                pasos = " → ".join(f"{s['skill']}{' [combo]' if s.get('combo') else ''} ({s.get('rawAvg', s.get('note', '?'))})" for s in sq["steps"])
                L.append(f"- {sq['sequence']}: {pasos}")
            L.append(f"- Bono de combo del modelo: {cb.get('comboBonusModel')}")
            L.append("")
        # area / aliados
        areas = [(n, nombre(rs[0]["name"]), max(r["victims"] for r in rs if r["test"] == "dmg")) for n, rs in sk.items() if str(rs[0].get("type", "")).startswith("Area") and any(r["test"] == "dmg" for r in rs)]
        solos = [f"{n} {nm}" for n, nm, v in areas if v <= 1]
        if areas:
            L.append(f"Skills de área medidas contra 3 muñecos juntos: golpean a varios = {', '.join(f'{n} {nm} ({v})' for n, nm, v in areas if v > 1) or 'ninguna'}; **a uno solo** = {', '.join(solos) or 'ninguna'}.")
            fuego = [f"{n}" for n, rs in sk.items() if any(r.get("allyHit") for r in rs)]
            L.append(f"Fuego amigo (golpeó al aliado cercano): {', '.join(fuego) or 'ninguna'}.")
            L.append("")
            if len(solos) == len(areas):
                anomalias.append(f"{cn}: NINGUNA skill de área golpeó a más de un muñeco (¿el área solo golpea al objetivo clicado? revisar `AreaSkillHitsPlayer`).")
    # comparacion entre clases
    L += ["## Tiempo para matar por clase (rotación a rango 5, 1 objetivo, sin stats ni ítems)", "", "| Clase | vs BK nivel 30 | vs 60.000 | DPS sostenido |", "|---|---|---|---|"]
    for cn, a, b, dps in resumen_ttk:
        L.append(f"| {cn} | {a if a else '> 300'} s | {b if b else '> 300'} s | {dps}/s |")
    L += ["", "## Anomalías detectadas automáticamente", ""]
    L += [f"- {a}" for a in anomalias] or ["- ninguna"]
    out = RAIZ / "INFORME_SKILLS.md"
    out.write_text("\n".join(L) + "\n", encoding="utf-8")
    print(f"Escrito {out} ({len(L)} líneas, {len(anomalias)} anomalías)")


if __name__ == "__main__":
    main()
