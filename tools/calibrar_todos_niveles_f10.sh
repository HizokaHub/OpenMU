#!/bin/bash
# Calibra todos los niveles seguidos (cada uno con tools/calibrar_nivel_f10.py) y al final genera los valores por defecto del servidor.
# Uso: tools/calibrar_todos_niveles_f10.sh "30 20 10 5 1 15 25"
cd "$(dirname "$0")/.."
export MSYS_NO_PATHCONV=1
for L in $1; do
  python -I -X utf8 tools/calibrar_nivel_f10.py "$L" 20 60 || { echo "fallo en el nivel $L"; break; }
done
python -I -X utf8 tools/generar_tablas_f10.py
