#!/usr/bin/env bash
# Prueba de extracción completa con Departure y Return usando llama3.2-vision

echo "=========================================="
echo "🧪 PRUEBA COMPLETA: Departure + Return"
echo "Modelo: llama3.2-vision:latest"
echo "=========================================="
echo ""

OLLAMA_URL="http://192.168.5.91:11434/api/chat"
MODEL="llama3.2-vision:latest"
ORIGEN="USA"
DESTINO="MEX"

echo "📍 Configuración:"
echo "   Ollama: $OLLAMA_URL"
echo "   Modelo: $MODEL"
echo "   Ruta: $ORIGEN → $DESTINO"
echo ""

echo "⏳ Enviando prompt completo (puede tardar 10-15s)..."
echo ""

# Crear JSON de request con prompt completo
cat > /tmp/full_request.json << JSONEND
{
  "model": "llama3.2-vision:latest",
  "messages": [
    {
      "role": "system",
      "content": "Eres un experto en requisitos de viaje. Extrae información de tabs Departure e Return. Responde SOLO con JSON válido."
    },
    {
      "role": "user",
      "content": "Viaje de USA a MEX (México). Extraer para DEPARTURE (ida) y RETURN (vuelta):\\n1. Visa requerida: sí/no, tipo, duración\\n2. Pasaporte: validez mínima, páginas en blanco\\n3. Salud: vacunas, COVID, seguro\\n4. Documentos adicionales\\n\\nResponder en JSON con:\\ndeparture: {visa: {...}, pasaporte: {...}, salud: {...}}\\nreturn: {visa: {...}, pasaporte: {...}, salud: {...}}\\nconfianza: 0.0-1.0"
    }
  ],
  "stream": false,
  "options": {
    "temperature": 0.1,
    "num_predict": 2048
  }
}
JSONEND

START_TIME=$(date +%s)

RESPONSE=$(curl -s -X POST "$OLLAMA_URL" \
  -H "Content-Type: application/json" \
  -d @/tmp/full_request.json)

END_TIME=$(date +%s)
DURATION=$((END_TIME - START_TIME))

echo "✅ Respuesta recibida en ${DURATION}s"
echo ""

# Extraer contenido
CONTENT=$(echo "$RESPONSE" | jq -r '.message.content' 2>/dev/null || echo "Error parsing")

echo "📋 RESULTADO (JSON estructurado):"
echo "=========================================="

# Intentar formatear como JSON
if echo "$CONTENT" | jq . >/dev/null 2>&1; then
    echo "$CONTENT" | jq .
    echo ""
    echo "✅ JSON válido y formateado"
    
    # Extraer campos clave
    echo ""
    echo "🔍 Campos extraídos:"
    echo "$CONTENT" | jq -r '
      if has("departure") then "   ✅ Departure presente" else "   ❌ Departure ausente" end,
      if has("return") then "   ✅ Return presente" else "   ❌ Return ausente" end,
      if has("confianza") then "   ✅ Confianza: \(.confianza)" else "   ℹ️  Sin confianza" end
    '
else
    echo "$CONTENT"
    echo ""
    echo "ℹ️  Respuesta en formato libre"
fi

echo ""
echo "=========================================="
echo ""
echo "📊 Métricas:"
echo "$RESPONSE" | jq -r '
  "   Tiempo total: \(.total_duration / 1000000000) s",
  "   Load: \(.load_duration / 1000000000) s",
  "   Tokens entrada: \(.prompt_eval_count)",
  "   Tokens salida: \(.eval_count)"
'

echo ""
echo "=========================================="
echo "✅ Prueba de extracción completa finalizada!"
echo "=========================================="
