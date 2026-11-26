#!/bin/bash
# Abre el juego, corre una tanda minima (1 duelo por cruce) en cada nivel y muestra lo que compro cada clase ([MOBA-BUILD]); al final cierra todo y devuelve la IP.
# Uso: tools/comprobar_equipos_f10.sh "3 5 10 15 20 25 30" ["escudo doble"]   (que llevan BK y MG; se repiten los niveles por cada opcion)
cd "$(dirname "$0")/.."
export MSYS_NO_PATHCONV=1
INI=../mu-main/out/build/windows-x64/src/Release/config.ini
sed -i 's/^ServerIP=127\.127\.127\.127/ServerIP=127.0.0.1/' "$INI"
cerrar() {
  python -I tools/bot-pruebas/piloto.py cerrar
  powershell -NoProfile -ExecutionPolicy Bypass -File tools/iniciar-todo.ps1 -Parar -Forzar > /dev/null
  sed -i 's/^ServerIP=127\.0\.0\.1/ServerIP=127.127.127.127/' "$INI"
}
trap cerrar EXIT
bash tools/reiniciar_para_calibrar.sh > /tmp/reinicio.log 2>&1 || { echo "no pude abrir el juego"; tail -5 /tmp/reinicio.log; exit 1; }
for M in ${2:-escudo doble}; do
for intento in 1 2 3 4; do
  tam=$(stat -c %s logs/servidor.log)
  python -I tools/bot-pruebas/piloto.py chat "/mobaf10 manos $M"
  sleep 3
  tail -c +$((tam+1)) logs/servidor.log | grep -q "MOBA-F10\] duelos" && break
  [ "$intento" = 4 ] && { echo "no pude elegir las manos ($M)"; exit 1; }
done
for L in $1; do
  m=$(grep -c "MOBA-DUEL\] report=" logs/servidor.log)
  for intento in 1 2 3; do
    tam=$(stat -c %s logs/servidor.log)
    python -I tools/bot-pruebas/piloto.py chat "/mobaduel all 1 real 60 $L"
    sleep 8
    tail -c +$((tam+1)) logs/servidor.log | grep -q "MOBA-DUEL" && break
    echo "(aviso) la tanda del nivel $L no empezo (intento $intento)"
  done
  for i in $(seq 1 90); do n=$(grep -c "MOBA-DUEL\] report=" logs/servidor.log); [ "$n" -gt "$m" ] && break; sleep 3; done
  echo "nivel $L ($M): tanda lista"
done
done
grep "MOBA-BUILD\]" logs/servidor.log | sed 's/.*\[MOBA-BUILD\] //' > tools/f10_equipos_por_nivel.txt
grep -c "MOBA-DUEL\] failed" logs/servidor.log | sed 's/^/tandas fallidas: /'
echo "equipos registrados: $(wc -l < tools/f10_equipos_por_nivel.txt)"
