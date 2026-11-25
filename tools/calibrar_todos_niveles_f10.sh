#!/bin/bash
# Calibra todos los niveles seguidos (cada uno con tools/calibrar_nivel_f10.py) y al final genera los valores por defecto del servidor.
# Antes de cada nivel reinicia el servidor y el cliente (el servidor pierde ids de objeto tras ~35 pasadas: "Maximum object id exceeded").
# Uso: tools/calibrar_todos_niveles_f10.sh "30 20 10 5 1 15 25"
cd "$(dirname "$0")/.."
export MSYS_NO_PATHCONV=1
# el cliente apunta al servidor local mientras dura la calibracion y se devuelve a la IP original al terminar (tambien si se corta)
INI=../mu-main/out/build/windows-x64/src/Release/config.ini
sed -i 's/^ServerIP=127\.127\.127\.127/ServerIP=127.0.0.1/' "$INI"
trap "sed -i 's/^ServerIP=127\.0\.0\.1/ServerIP=127.127.127.127/' $INI" EXIT
for L in $1; do
  bash tools/reiniciar_para_calibrar.sh || { echo "fallo al reiniciar antes del nivel $L"; break; }
  python -I -X utf8 tools/calibrar_nivel_f10.py "$L" 20 40 || { echo "fallo en el nivel $L"; break; }
done
python -I -X utf8 tools/generar_tablas_f10.py
