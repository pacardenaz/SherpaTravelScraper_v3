#!/usr/bin/env bash
# Prueba con llama3.2-vision:latest

echo "=========================================="
echo "🧪 PRUEBA CON llama3.2-vision:latest"
echo "=========================================="
echo ""

OLLAMA_URL="http://192.168.5.91:11434/api/chat"
MODEL="llama3.2-vision:latest"

echo "📍 Configuración:"
echo "   Ollama: $OLLAMA_URL"
echo "   Modelo: $MODEL"
echo "   Ruta: USA → MEX"
echo ""

echo "Verificando que el modelo está disponible..."
if curl -s http://192.168.5.91:11434/api/tags | grep -q "llama3.2-vision"; then
    echo "   ✅ llama3.2-vision encontrado"
else
    echo "   ❌ llama3.2-vision no encontrado"
    echo "   Modelos disponibles:"
    curl -s http://192.168.5.91:11434/api/tags | grep '"name"' | sed 's/.*"name": "\([^"]*\)".*/      - \1/'
    exit 1
fi

echo ""
echo "🚀 Enviando prompt de prueba..."
echo ""

# Crear request JSON
cat > /tmp/llama32_request.json << 'JSONEOF'
{
  "model": "llama3.2-vision:latest",
  "messages": [
    {
      "role": "system",
      "content": "Eres un experto en requisitos de viaje. Responde en español con JSON estructurado."
    },
    {
      "role": "user",
      "content": "Ciudadano de Estados Unidos viaja a México. Extraer: requiere_visa (true/false), validez_pasaporte (meses), vacunas_obligatorias (lista), restricciones_COVID (si/no). Responder SOLO con JSON."
    }
  ],
  "stream": false,
  "options": {
    "temperature": 0.1,
    "num_predict": 512
  }
}
JSONEOF

echo "⏳ Esperando respuesta..."
echo "   (llama3.2-vision es más grande, puede tomar 10-20 segundos en primera carga)"
echo ""

START_TIME=$(date +%s)

RESPONSE=$(curl -s -X POST "$OLLAMA_URL" \
  -H "Content-Type: application/json" \
  -d @/tmp/llama32_request.json)

END_TIME=$(date +%s)
DURATION=$((END_TIME - START_TIME))

echo "✅ Respuesta recibida en ${DURATION}s"
echo ""

# Extraer contenido
CONTENT=$(echo "$RESPONSE" | jq -r '.message.content' 2>/dev/null || echo "Error")

echo "📋 RESULTADO:"
echo "=========================================="
echo "$CONTENT"
echo "=========================================="
echo ""

# Verificar JSON
if echo "$CONTENT" | jq . >/dev/null 2>&1; then
    echo "✅ JSON válido detectado"
else
    echo "ℹ️  Formato libre"
fi

echo ""
echo "📊 Métricas:"
echo "$RESPONSE" | jq -r '
  "   Modelo: \(.model)",
  "   Tiempo total: \(.total_duration / 1000000000) s",
  "   Load time: \(.load_duration / 1000000000) s",
  "   Tokens prompt: \(.prompt_eval_count)",
  "   Tokens generados: \(.eval_count)"
'

echo ""
echo "=========================================="
echo "✅ Prueba completada!"
echo "=========================================="
