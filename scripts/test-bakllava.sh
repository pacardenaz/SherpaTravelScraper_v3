#!/usr/bin/env bash
# Script de prueba para extracción con IA usando bakllava
# Ejecutar en la máquina donde está OpenClaw para probar conexión a Ollama

echo "=== Prueba de Extracción IA con bakllava ==="
echo ""
echo "Ollama endpoint: http://192.168.5.91:11434"
echo "Modelo: bakllava:latest"
echo ""

# Crear una imagen de prueba simple (1x1 pixel rojo codificado en base64)
TEST_IMAGE="iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8DwHwAFBQIAX8jx0gAAAABJRU5ErkJggg=="

echo "1. Verificando conexión a Ollama..."
curl -s http://192.168.5.91:11434/api/tags > /dev/null
if [ $? -eq 0 ]; then
    echo "   ✅ Ollama responde"
else
    echo "   ❌ Ollama no responde en http://192.168.5.91:11434"
    exit 1
fi

echo ""
echo "2. Verificando modelo bakllava..."
if curl -s http://192.168.5.91:11434/api/tags | grep -q "bakllava"; then
    echo "   ✅ bakllava está disponible"
else
    echo "   ❌ bakllava no encontrado"
    echo "   Modelos disponibles:"
    curl -s http://192.168.5.91:11434/api/tags | grep '"name"' | sed 's/.*"name": "\([^"]*\)".*/      - \1/'
    exit 1
fi

echo ""
echo "3. Enviando prueba de extracción..."
echo "   (Esta prueba envía una imagen pequeña y un prompt de ejemplo)"
echo ""

# Construir JSON para Ollama chat
JSON_PAYLOAD=$(cat <<EOF
{
  "model": "bakllava:latest",
  "messages": [
    {
      "role": "system",
      "content": "Eres un experto en extracción de requisitos de viaje. Responde SOLO con JSON válido."
    },
    {
      "role": "user",
      "content": "Analiza esta imagen de una página de requisitos de viaje y extrae:\n1. Si se requiere visa\n2. Validez del pasaporte\n3. Vacunas requeridas\n\nResponde en JSON con formato: {requiere_visa: true/false, validez_pasaporte: string, vacunas: []}",
      "images": ["$TEST_IMAGE"]
    }
  ],
  "stream": false,
  "options": {
    "temperature": 0.1,
    "num_predict": 512
  }
}
EOF
)

echo "   Enviando request..."
START_TIME=$(date +%s)

RESPONSE=$(curl -s -X POST http://192.168.5.91:11434/api/chat \
  -H "Content-Type: application/json" \
  -d "$JSON_PAYLOAD" 2>&1)

END_TIME=$(date +%s)
DURATION=$((END_TIME - START_TIME))

echo ""
echo "4. Resultado (tiempo: ${DURATION}s):"
echo "========================================"
echo "$RESPONSE" | jq -r '.message.content' 2>/dev/null || echo "$RESPONSE" | head -20
echo "========================================"

echo ""
echo "5. Respuesta completa (JSON):"
echo "$RESPONSE" | jq . 2>/dev/null || echo "$RESPONSE" | head -50

echo ""
echo "=== Prueba completada ==="
echo ""
echo "Nota: Esta prueba usó una imagen de 1 pixel. Para pruebas reales:"
echo "  1. Ejecuta SherpaTravelScraper con una combinación específica"
echo "  2. El screenshot real se enviará a bakllava"
echo "  3. La respuesta será parseada como JSON estructurado"
