#!/bin/bash
# Reinicia el servidor (con F10 encendido por MOBA_F10=1) y vuelve a entrar al MOBA con el bot. Sirve para limpiar la fuga de ids de objeto/memoria entre niveles.
cd "$(dirname "$0")/.."
export MSYS_NO_PATHCONV=1
python -I tools/bot-pruebas/piloto.py cerrar
powershell -NoProfile -ExecutionPolicy Bypass -File tools/iniciar-todo.ps1 -Parar -Forzar
sleep 5
MOBA_F10=1 powershell -NoProfile -ExecutionPolicy Bypass -File tools/iniciar-todo.ps1 -SinPortal
sleep 10
python -I tools/bot-pruebas/piloto.py entrar || { sleep 5; python -I tools/bot-pruebas/piloto.py entrar; }
sleep 8   # el cliente aun esta cargando la arena justo tras /moba: un chat enviado antes se pierde y deja la barra de chat desajustada
for i in 1 2 3 4 5; do
  tam=$(stat -c %s logs/servidor.log)
  python -I tools/bot-pruebas/piloto.py chat "/mobaf10 on"
  sleep 3
  tail -c +$((tam+1)) logs/servidor.log | grep -q "F10 ACTIV" && { echo "F10 encendido"; exit 0; }
  echo "(aviso) /mobaf10 on no llego (intento $i)"
done
exit 1
