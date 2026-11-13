"""Genera SKILLS_REGISTRO.md: registro ordenado de todas las skills del MOBA, leido del codigo.

Fuentes (si cambian, se vuelve a correr este script):
  - Persistence/Initialization/Skills/SkillNumber.cs            -> numero de cada skill
  - Persistence/Initialization/VersionSeasonSix/SkillsInitializer.cs -> nombre, alcance, mana de MU, tipo
  - GameLogic/PlugIns/Moba/MobaLoadouts.cs                      -> kit de cada clase, buffs, picks por defecto
  - GameLogic/PlugIns/Moba/MobaSkillDamage.cs                   -> daño base y por rango (tabla plana)
  - GameLogic/PlugIns/Moba/MobaCooldowns.cs                     -> enfriamiento por rango
  - GameLogic/PlugIns/Moba/MobaBuffs.cs                         -> duracion de los buffs
Uso: python -I tools/skills_registro.py
"""
import re
from pathlib import Path

RAIZ = Path(__file__).resolve().parent.parent
SRC = RAIZ / "src"
SALIDA = RAIZ / "SKILLS_REGISTRO.md"

FLAT = 1.5            # MobaSkillDamage.FlatMultiplier
MANA_X = 3            # el mana de MU se triplica en el MOBA
DEFAULT_CD = [6.5, 6.0, 5.5, 5.0, 4.5]

CLASES = [
    ("Dark Wizard / Soul Master / Grand Master", "Wizard"),
    ("Dark Knight / Blade Knight / Blade Master", "Knight"),
    ("Fairy Elf / Muse Elf / High Elf", "Elf"),
    ("Magic Gladiator / Duel Master", "MagicGladiator"),
    ("Dark Lord / Lord Emperor", "DarkLord"),
    ("Summoner / Bloody Summoner / Dimension Master", "Summoner"),
    ("Rage Fighter / Fist Master", "RageFighter"),
]


def leer(ruta):
    return (SRC / ruta).read_text(encoding="utf-8", errors="replace")


def numeros_skill():
    texto = leer("Persistence/Initialization/Skills/SkillNumber.cs")
    return {m.group(1): int(m.group(2)) for m in re.finditer(r"^\s*(\w+)\s*=\s*(\d+),", texto, re.M)}


def partir_argumentos(cuerpo):
    args, nivel, actual, comillas = [], 0, "", False
    for c in cuerpo:
        if c == '"':
            comillas = not comillas
        if not comillas:
            if c in "([{":
                nivel += 1
            elif c in ")]}":
                nivel -= 1
            elif c == "," and nivel == 0:
                args.append(actual.strip())
                actual = ""
                continue
        actual += c
    if actual.strip():
        args.append(actual.strip())
    return args


def skills_de_mu(numeros):
    """name, distance, mana, tipo, notas por numero de skill (version Season 6)."""
    texto = leer("Persistence/Initialization/VersionSeasonSix/SkillsInitializer.cs")
    resultado = {}
    for m in re.finditer(r"this\.CreateSkill\((.*)\);", texto):
        args = partir_argumentos(m.group(1))
        if len(args) < 2:
            continue
        clave = args[0].replace("SkillNumber.", "")
        if clave not in numeros:
            continue
        nombre = args[1].strip('"')
        posicionales = [a for a in args[2:] if ":" not in a.split("(")[0] or a.startswith("Character")]
        nombrados = {}
        for a in args[2:]:
            mm = re.match(r"(\w+):\s*(.*)$", a)
            if mm:
                nombrados[mm.group(1)] = mm.group(2)
        sin_nombre = [a for a in args[2:] if not re.match(r"\w+:\s", a)]
        # posicionales: clases, tipoDanio, daño, distancia
        distancia = nombrados.get("distance")
        if distancia is None and len(sin_nombre) >= 4:
            distancia = sin_nombre[3]
        mana = nombrados.get("manaConsumption", "0")
        tipo = nombrados.get("skillType", "DirectHit").replace("SkillType.", "")
        notas = []
        if "elementalModifier" in nombrados:
            notas.append(nombrados["elementalModifier"].replace("ElementalType.", "elemento "))
        if nombrados.get("movesToTarget") == "true":
            notas.append("se lanza sobre el objetivo")
        if nombrados.get("movesTarget") == "true":
            notas.append("empuja")
        resultado[numeros[clave]] = {
            "nombre": nombre,
            "alcance": distancia or "?",
            "mana": mana,
            "tipo": tipo,
            "notas": ", ".join(notas),
        }
    return resultado


def tabla_dano():
    texto = leer("GameLogic/PlugIns/Moba/MobaSkillDamage.cs")
    bloque = texto.split("private static readonly Dictionary<short, (int Base")[1]
    return {int(m.group(1)): (int(m.group(2)), int(m.group(3)), float(m.group(4)))
            for m in re.finditer(r"\[(\d+)\]\s*=\s*\((\d+),\s*(\d+),\s*([\d.]+)\)", bloque)}


def tabla_cooldowns():
    texto = leer("GameLogic/PlugIns/Moba/MobaCooldowns.cs")
    bloque = texto.split("PerRankSeconds = new()")[1]
    return {int(m.group(1)): [float(x) for x in m.group(2).split(",")]
            for m in re.finditer(r"\[(\d+)\]\s*=\s*new\[\]\s*\{([^}]*)\}", bloque)}


def duraciones():
    texto = leer("GameLogic/PlugIns/Moba/MobaBuffs.cs")
    bloque = texto.split("DurationSeconds { get; } = new Dictionary<short, float>")[1].split("};")[0]
    return {int(m.group(1)): float(m.group(2)) for m in re.finditer(r"\[(\d+)\]\s*=\s*(\d+)", bloque)}


def kits():
    texto = leer("GameLogic/PlugIns/Moba/MobaLoadouts.cs")
    def lista(nombre_dic, familia):
        m = re.search(r"\[Family\.%s\]\s*=\s*new short\[\]\s*\{([^}]*)\}" % familia, texto.split(nombre_dic)[1])
        return [int(x) for x in m.group(1).split(",") if x.strip()] if m else []
    bloque_buffs = texto.split("HashSet<short> Buffs = new()")[1].split("};")[0]
    bloque_buffs = re.sub(r"//.*", "", bloque_buffs)
    buffs = {int(x) for x in re.findall(r"\b(\d+)\b", bloque_buffs)}
    bloque_summ = texto.split("HashSet<short> Summons = new()")[1].split("};")[0]
    bloque_summ = re.sub(r"//.*", "", bloque_summ)
    summons = {int(x) for x in re.findall(r"\b(\d+)\b", bloque_summ)}
    kit = {f: lista("Dictionary<Family, short[]> Skills", f) for _, f in CLASES}
    defecto = {f: lista("Dictionary<Family, short[]> DefaultPicks", f) for _, f in CLASES}
    return kit, defecto, buffs, summons


def numero(valor, defecto=None):
    try:
        return float(valor)
    except (TypeError, ValueError):
        return defecto


def main():
    nums = numeros_skill()
    mu = skills_de_mu(nums)
    dano = tabla_dano()
    cds = tabla_cooldowns()
    dur = duraciones()
    kit, defecto, buffs, summons = kits()

    L = []
    L.append("# Registro de skills del MOBA\n")
    L.append("Generado por `tools/skills_registro.py` a partir del código (no se edita a mano: se vuelve a generar). "
             "Orden: por clase y, dentro de cada una, por número de skill.\n")
    L.append("**Cómo leer las columnas**\n")
    L.append("- **Daño r1 / r5**: daño plano de la tabla `MobaSkillDamage` a rango 1 y rango 5 (ya con el multiplicador global ×1,5), "
             "antes de la escala por nivel, stats e ítems. **Escala stat** = fracción extra de daño con el stat principal al máximo "
             "(se multiplica por 2,5 en el código; 0,55 → hasta +137 %).")
    L.append("- **CD r1 / r5**: enfriamiento en segundos a rango 1 y 5 (sin entrada propia usa 6,5 → 4,5 s). **DPS r1 / r5** = daño ÷ CD "
             "(solo para comparar skills entre sí; la ventana de gracia de 3 s permite encadenar un lanzamiento extra).")
    L.append("- **Maná**: coste de MU × 3 (regla del MOBA). **Alcance** en casillas (valor del config de MU).")
    L.append("- **Elegible**: la skill entra en las 4-6 que el jugador elige; los **buffs** y el Teleport los tiene siempre; "
             "las **invocaciones** del Elfa son apariencias de una misma 5.ª skill.\n")

    por_clase_alertas = []
    for titulo, fam in CLASES:
        todas = sorted(set(kit[fam]))
        L.append(f"## {titulo}\n")
        L.append(f"Picks por defecto (si el jugador no eligió): {', '.join(str(n) + ' ' + mu.get(n, {}).get('nombre', '?') for n in defecto[fam])}.\n")
        L.append("| Nº | Skill | Tipo | Rol | Alcance | Maná | Daño r1 / r5 | Escala stat | CD r1 / r5 | DPS r1 / r5 | Duración | Notas |")
        L.append("|---|---|---|---|---|---|---|---|---|---|---|---|")
        for n in todas:
            info = mu.get(n, {"nombre": f"(skill {n} no está en el config)", "alcance": "?", "mana": "0", "tipo": "?", "notas": ""})
            if n in buffs:
                rol = "Buff"
            elif n in summons:
                rol = "Invocación"
            else:
                rol = "Ataque elegible"
            mana = numero(info["mana"], 0) * MANA_X
            cd = cds.get(n)
            cd_txt = f"{cd[0]:g} / {cd[4]:g} s" if cd else f"{DEFAULT_CD[0]:g} / {DEFAULT_CD[4]:g} s (por defecto)"
            cd1, cd5 = (cd[0], cd[4]) if cd else (DEFAULT_CD[0], DEFAULT_CD[4])
            if n in dano or rol == "Ataque elegible":
                b, p, s = dano.get(n, (70, 18, 0.50))   # sin entrada: valores por defecto del codigo
                marca = "" if n in dano else " (defecto)"
                d1, d5 = b * FLAT, (b + 4 * p) * FLAT
                d_txt = f"{d1:.0f} / {d5:.0f}{marca}"
                s_txt = f"{s:.2f}"
                dps_txt = f"{d1 / cd1:.0f} / {d5 / cd5:.0f}"
                por_clase_alertas.append((titulo, n, info["nombre"], d1 / cd1, d5 / cd5, n not in cds))
            else:
                d_txt, s_txt, dps_txt = "—", "—", "—"
            d = f"{dur[n]:g} s" if n in dur else ("instantáneo" if n in (26, 234) else "—")
            L.append(f"| {n} | {info['nombre']} | {info['tipo']} | {rol} | {info['alcance']} | {mana:g} | {d_txt} | {s_txt} | {cd_txt} | {dps_txt} | {d} | {info['notas']} |")
        L.append("")

    # alertas de equilibrio por la tabla
    L.append("## Lectura rápida de equilibrio (solo con la tabla, sin probar en juego)\n")
    skills_dano = sorted({(n, nom, a, b, c) for _, n, nom, a, b, c in por_clase_alertas}, key=lambda t: -t[2])
    if skills_dano:
        medio = sorted(x[2] for x in skills_dano)[len(skills_dano) // 2]
        L.append(f"DPS de rango 1 mediano entre las skills con daño: **{medio:.0f}**. Skills muy por encima (×1,6 o más) o muy por debajo (×0,5 o menos):\n")
        L.append("| Skill | DPS r1 | Respecto a la mediana | CD por defecto |")
        L.append("|---|---|---|---|")
        for n, nom, a, b, por_defecto in skills_dano:
            if a >= medio * 1.6 or a <= medio * 0.5:
                L.append(f"| {n} {nom} | {a:.0f} | ×{a / medio:.2f} | {'sí' if por_defecto else 'no'} |")
        L.append("")
    sin_tabla = sorted(n for fam in kit.values() for n in fam if n not in dano and n not in buffs and n not in summons)
    sin_tabla = sorted(set(sin_tabla))
    L.append("Skills ofensivas del kit **sin entrada en la tabla de daño** (usan 70 + 18 por rango por defecto): "
             + (", ".join(f"{n} {mu.get(n, {}).get('nombre', '?')}" for n in sin_tabla) or "ninguna") + ".\n")
    SALIDA.write_text("\n".join(L), encoding="utf-8")
    print(f"Escrito {SALIDA} ({len(L)} lineas)")


if __name__ == "__main__":
    main()
