"""Escribe un JSON de parametros como valores iniciales de f07_saturacion.py. Uso: python -I -X utf8 resultados/f07_bake.py params.json"""
import sys, json, re
from pathlib import Path
base = Path(__file__).resolve().parent.parent / "f07_saturacion.py"
d = json.loads(Path(sys.argv[1]).read_text(encoding="utf-8"))
d = d.get("params", d)
s = base.read_text(encoding="utf-8")
body = "_FINAL = {\n" + "".join('    "%s": %s,\n' % (k, repr(round(float(v), 6))) for k, v in d.items()) + "}\n"
s = re.sub(r"# BAKE-INICIO\n.*?# BAKE-FIN", lambda m: "# BAKE-INICIO\n" + body + "# BAKE-FIN", s, flags=re.S)
base.write_text(s, encoding="utf-8")
print("horneado", len(d), "parametros")
