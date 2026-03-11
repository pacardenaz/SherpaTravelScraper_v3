#!/usr/bin/env bash
# Script para verificar y configurar Ollama para SherpaTravelScraper
# Ejecutar en la máquina donde corre Ollama (192.168.5.91)

echo "=== Verificación de Ollama ==="
echo "Verificando conexión a Ollama en http://192.168.5.91:11434..."

# Verificar que Ollama está corriendo
curl -s http://192.168.5.91:11434/api/tags > /dev/null
if [ $? -ne 0 ]; then
    echo "❌ Ollama no responde. Verificar que esté corriendo."
    exit 1
fi

echo "✅ Ollama está respondiendo"
echo ""

# Verificar modelos disponibles
echo "=== Modelos disponibles ==="
curl -s http://192.168.5.91:11434/api/tags | grep '"name"' | sed 's/.*"name": "\([^"]*\)".*/  - \1/'

echo ""
echo "=== Modelos recomendados para visión ==="
echo ""
echo "Actualmente tienes estos modelos con soporte de visión:"
echo "  ✅ bakllava:latest - Ya instalado (7B, bueno para imágenes)"
echo ""
echo "Modelos adicionales recomendados:"
echo ""
echo "1. llama3.2-vision:latest (11B) - Mejor calidad para documentos"
echo "   Comando: ollama pull llama3.2-vision"
echo ""
echo "2. llava:latest (7B) - Alternativa confiable"
echo "   Comando: ollama pull llava"
echo ""
echo "3. qwen2.5-vl:latest - Especializado en documentos/visión"
echo "   Comando: ollama pull qwen2.5-vl"
echo ""

# Probar modelo bakllava
echo "=== Probando bakllava con imagen de prueba ==="
echo "Creando imagen de prueba simple..."

# Crear imagen base64 simple (1x1 pixel rojo)
TEST_IMAGE="iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8DwHwAFBQIAX8jx0gAAAABJRU5ErkJggg=="

echo "Enviando request de prueba..."
curl -s -X POST http://192.168.5.91:11434/api/generate \
  -H "Content-Type: application/json" \
  -d '{
    "model": "bakllava:latest",
    "prompt": "Describe this image in one word.",
    "images": ["'$TEST_IMAGE'"],
    "stream": false
  }' | jq -r '.response // "Error: " + .error' 2>/dev/null || echo "Respuesta recibida (verificar JSON)"

echo ""
echo "=== Configuración recomendada ==="
echo ""
echo "Para usar con SherpaTravelScraper, configura en appsettings.json:"
echo ''
echo '{'
echo '  "AI": {'
echo '    "Provider": "ollama",'
echo '    "Ollama": {'
echo '      "Endpoint": "http://192.168.5.91:11434/api/generate",'
echo '      "VisionEndpoint": "http://192.168.5.91:11434/api/chat",'
echo '      "Model": "bakllava:latest",'
echo '      "TimeoutSeconds": 120'
echo '    }'
echo '  }'
echo '}'
echo ''
echo "✅ Verificación completada"
