#!/bin/bash
# Script para ejecutar SherpaTravelScraper con método ia-html

echo "🚀 SherpaTravelScraper - Prueba con IA HTML"
echo "=============================================="

# Verificar configuración actual
cd /home/ubuntu/.openclaw/workspace/dev/SherpaTravelScraper/src/SherpaTravelScraper

echo ""
echo "📋 Configuración actual:"
echo "Método: $(grep -A0 '"Method"' appsettings.json | head -1 | sed 's/.*: "\([^"]*\)".*/\1/')"
echo "Provider: $(grep -A0 '"Provider"' appsettings.json | head -1 | sed 's/.*: "\([^"]*\)".*/\1/')"
echo "Modelo: $(grep -A0 '"Model"' appsettings.json | tail -1 | sed 's/.*: "\([^"]*\)".*/\1/')"

echo ""
echo "🧹 Limpiando tablas..."
cd /home/ubuntu/.openclaw/workspace/dev/SherpaTravelScraper/src/CleanDatabase
dotnet run 2> /dev/null

echo ""
echo "🏃 Ejecutando scraper (máx 5 combinaciones)..."
cd /home/ubuntu/.openclaw/workspace/dev/SherpaTravelScraper/src/SherpaTravelScraper
timeout 300 dotnet run --configuration Release 2>&1 | tee /tmp/scraper_test.log

echo ""
echo "📊 Verificando resultados..."
sleep 2
cd /home/ubuntu/.openclaw/workspace/dev/SherpaTravelScraper/src/CleanDatabase
dotnet run 2> /dev/null

echo ""
echo "✅ Prueba completada"
echo ""
echo "📁 Logs guardados en: /tmp/scraper_test.log"
