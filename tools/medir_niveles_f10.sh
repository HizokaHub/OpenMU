#!/bin/bash
# Mide el equilibrio 1v1 "real" (ranks y equipo del nivel) en varios niveles con los psi actuales. Uso: tools/medir_niveles_f10.sh "5 10 15 20 25 30" [duelos=20]
cd "$(dirname "$0")/.."
export MSYS_NO_PATHCONV=1
for L in $1; do
  m=$(grep -c "MOBA-DUEL\] report=" logs/servidor.log)
  python -I tools/bot-pruebas/piloto.py chat "/mobaduel all ${2:-20} real 60 $L"
  for i in $(seq 1 120); do sleep 5; n=$(grep -c 'MOBA-DUEL\] report=' logs/servidor.log); [ "$n" -gt "$m" ] && break; done
  echo "=== nivel $L"
  python -I -X utf8 tools/resumen_f10.py "nivel$L" 2>&1 | sed -n 2,14p | cut -c1-200
done
