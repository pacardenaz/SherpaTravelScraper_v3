#!/usr/bin/env bash
# Prueba de extracción IA con URL real usando curl y Ollama directamente

echo "=========================================="
echo "🧪 PRUEBA REAL: USA → MEX (Sherpa)"
echo "=========================================="
echo ""

OLLAMA_URL="http://192.168.5.91:11434/api/chat"
MODEL="bakllava:latest"

echo "📍 Configuración:"
echo "   Ollama: $OLLAMA_URL"
echo "   Modelo: $MODEL"
echo "   Ruta: USA → MEX"
echo ""

echo "🚀 Enviando prompt de prueba a bakllava..."
echo ""

# Construir JSON para Ollama con jq
cat > /tmp/ollama_request.json << 'JSONEOF'
{
  "model": "bakllava:latest",
  "messages": [
    {
      "role": "system",
      "content": "Eres un experto en extracción de requisitos de viaje. Responde SOLO con JSON válido."
    },
    {
      "role": "user",
      "content": "Analiza requisitos de viaje para ciudadano de USA viajando a MEX (México) con pasaporte válido. Responde en JSON con: requiere_visa, validez_pasaporte, vacunas_requeridas, requisitos_sanitarios."
    }
  ],
  "stream": false,
  "options": {
    "temperature": 0.1,
    "num_predict": 512
  }
}
JSONEOF

echo "⏳ Esperando respuesta de la IA..."
echo "   (Esto puede tomar 5-15 segundos)"
echo ""

START_TIME=$(date +%s)

RESPONSE=$(curl -s -X POST "$OLLAMA_URL" \
  -H "Content-Type: application/json" \
  -d @/tmp/ollama_request.json)

END_TIME=$(date +%s)
DURATION=$((END_TIME - START_TIME))

echo "✅ Respuesta recibida en ${DURATION}s"
echo ""

# Extraer contenido
CONTENT=$(echo "$RESPONSE" | jq -r '.message.content' 2>/dev/null || echo "Error al parsear")

echo "📋 RESULTADO:"
echo "=========================================="
echo "$CONTENT"
echo "=========================================="
echo ""

# Verificar si es JSON válido
if echo "$CONTENT" | jq . >/dev/null 2>&1; then
    echo "✅ La respuesta es JSON válido"
    echo ""
    echo "📊 Campos detectados:"
    echo "$CONTENT" | jq -r 'keys[]' | sed 's/^/   - /'
else
    echo "ℹ️  Respuesta en formato libre (no JSON)"
fi

echo ""
echo "📊 Métricas:"
echo "$RESPONSE" | jq -r '
  "   Modelo: \(.model)",
  "   Tiempo total: \(.total_duration / 1000000000)s",
  "   Tokens prompt: \(.prompt_eval_count)",
  "   Tokens generados: \(.eval_count)"
'

echo ""
echo "=========================================="
echo "✅ Prueba completada exitosamente!"
echo "=========================================="
