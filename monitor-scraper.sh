#!/bin/bash
# Monitor para SherpaTravelScraper - Reinicia automáticamente si se cae

export PATH="/home/ubuntu/.dotnet:/opt/mssql-tools18/bin:$PATH"
export PLAYWRIGHT_BROWSERS_PATH=/home/ubuntu/.cache/ms-playwright

cd /home/ubuntu/.openclaw/workspace/dev/SherpaTravelScraper/src/SherpaTravelScraper

LOG_FILE="/tmp/sherpa-monitor.log"
PID_FILE="/tmp/sherpa-scraper.pid"
STATUS_FILE="/home/ubuntu/.openclaw/workspace/dev/SherpaTravelScraper/progress-status.json"

echo "[$(date)] Monitor iniciado" >> $LOG_FILE

# Función para verificar estado en BD
check_progress() {
    local total=$(sqlcmd -S 192.168.5.112 -U sa -P "Isami06cz%" -C -d TravelRequirementsDB -Q "SET NOCOUNT ON; SELECT COUNT(*) FROM txnet_combinaciones_procesar" 2>/dev/null | tail -1 | tr -d ' ')
    local completadas=$(sqlcmd -S 192.168.5.112 -U sa -P "Isami06cz%" -C -d TravelRequirementsDB -Q "SET NOCOUNT ON; SELECT COUNT(*) FROM txnet_combinaciones_procesar WHERE comb_estado = 'C'" 2>/dev/null | tail -1 | tr -d ' ')
    local fallidas=$(sqlcmd -S 192.168.5.112 -U sa -P "Isami06cz%" -C -d TravelRequirementsDB -Q "SET NOCOUNT ON; SELECT COUNT(*) FROM txnet_combinaciones_procesar WHERE comb_estado = 'E'" 2>/dev/null | tail -1 | tr -d ' ')
    local registros_bd=$(sqlcmd -S 192.168.5.112 -U sa -P "Isami06cz%" -C -d TravelRequirementsDB -Q "SET NOCOUNT ON; SELECT COUNT(*) FROM txnet_detrequisitos" 2>/dev/null | tail -1 | tr -d ' ')
    
    # Guardar estado en JSON
    cat > $STATUS_FILE << EOF
{
    "timestamp": "$(date -u +%Y-%m-%dT%H:%M:%SZ)",
    "total_combinaciones": ${total:-0},
    "completadas": ${completadas:-0},
    "fallidas": ${fallidas:-0},
    "pendientes": $((total - completadas - fallidas)),
    "registros_bd": ${registros_bd:-0},
    "porcentaje": $(if [ "$total" -gt 0 ]; then echo "scale=2; ($completadas * 100) / $total" | bc; else echo "0"; fi)
}
EOF
    
    echo "[$total total, $completadas completadas, $fallidas fallidas, $registros_bd en BD]"
}

# Función para iniciar el scraper
start_scraper() {
    echo "[$(date)] Iniciando scraper..." >> $LOG_FILE
    nohup dotnet run --configuration Release >> /tmp/sherpa-full-run.log 2>&1 &
    echo $! > $PID_FILE
    echo "[$(date)] Scraper iniciado con PID: $(cat $PID_FILE)" >> $LOG_FILE
}

# Verificar si ya hay un proceso corriendo
if [ -f "$PID_FILE" ]; then
    OLD_PID=$(cat $PID_FILE)
    if ps -p $OLD_PID > /dev/null 2>&1; then
        echo "[$(date)] Scraper ya está corriendo (PID: $OLD_PID)" >> $LOG_FILE
    else
        echo "[$(date)] Proceso anterior terminado, reiniciando..." >> $LOG_FILE
        start_scraper
    fi
else
    start_scraper
fi

# Loop de monitoreo
count=0
while true; do
    sleep 60  # Verificar cada minuto
    count=$((count + 1))
    
    # Cada 5 minutos, guardar progreso
    if [ $((count % 5)) -eq 0 ]; then
        progress=$(check_progress)
        echo "[$(date)] Progreso: $progress" >> $LOG_FILE
    fi
    
    # Verificar si el proceso sigue vivo
    if [ -f "$PID_FILE" ]; then
        PID=$(cat $PID_FILE)
        if ! ps -p $PID > /dev/null 2>&1; then
            echo "[$(date)] ⚠️ Proceso caído (PID: $PID), reiniciando..." >> $LOG_FILE
            start_scraper
        fi
    else
        echo "[$(date)] ⚠️ Archivo PID no existe, reiniciando..." >> $LOG_FILE
        start_scraper
    fi
    
    # Verificar si ya terminó todo (no hay pendientes)
    pendientes=$(sqlcmd -S 192.168.5.112 -U sa -P "Isami06cz%" -C -d TravelRequirementsDB -Q "SET NOCOUNT ON; SELECT COUNT(*) FROM txnet_combinaciones_procesar WHERE comb_estado = 'P'" 2>/dev/null | tail -1 | tr -d ' ')
    if [ "$pendientes" = "0" ]; then
        echo "[$(date)] ✅ Todas las combinaciones procesadas. Monitor terminando." >> $LOG_FILE
        check_progress
        exit 0
    fi
done
