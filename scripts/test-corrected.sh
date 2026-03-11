#!/usr/bin/env bash
# Prueba de extracción completa con prompt corregido

echo "=========================================="
echo "🧪 PRUEBA: Extracción de requisitos de viaje"
echo "=========================================="
echo ""

OLLAMA_URL="http://192.168.5.91:11434/api/chat"
MODEL="llama3.2-vision:latest"

cat > /tmp/test_request.json << 'JSONEND'
{
  "model": "llama3.2-vision:latest",
  "messages": [
    {
      "role": "system",
      "content": "Eres un asistente especializado en analizar información de viajes. Extraes datos públicos de requisitos de entrada a países. Responde con JSON estructurado."
    },
    {
      "role": "user",
      "content": "Analiza y extrae información sobre requisitos de viaje para un ciudadano estadounidense visitando México como turista. Proporciona:\n\n1. Para el viaje de IDA (USA → MEX):\n   - ¿Necesita visa?\n   - Requisitos de pasaporte\n   - Requisitos de salud/vacunas\n   - Documentos adicionales\n\n2. Para el viaje de VUELTA (MEX → USA):\n   - ¿Necesita visa?\n   - Requisitos de pasaporte\n   - Requisitos de salud\n   - Documentos adicionales\n\nResponde SOLO con JSON en este formato exacto:\n{\n  \"departure\": {\n    \"visa_requerida\": true/false,\n    \"tipo_visa\": \"...\",\n    \"pasaporte_validez\": \"...\",\n    \"vacunas\": [...],\n    \"documentos\": [...]\n  },\n  \"return\": {\n    \"visa_requerida\": true/false,\n    \"tipo_visa\": \"...\",\n    \"pasaporte_validez\": \"...\",\n    \"vacunas\": [...],\n    \"documentos\": [...]\n  }\n}"
    }
  ],
  "stream": false,
  "options": {
    "temperature": 0.1,
    "num_predict": 1024
  }
}
JSONEND

echo "Enviando solicitud a llama3.2-vision..."
echo ""

START=$(date +%s)
RESPONSE=$(curl -s -X POST "$OLLAMA_URL" -H "Content-Type: application/json" -d @/tmp/test_request.json)
END=$(date +%s)

echo "✅ Respuesta en $((END-START)) segundos"
echo ""

CONTENT=$(echo "$RESPONSE" | jq -r '.message.content')

echo "Resultado:"
echo "=========================================="
echo "$CONTENT" | jq . 2>/dev/null || echo "$CONTENT"
echo "=========================================="

echo ""
echo "Métricas:"
echo "$RESPONSE" | jq '{tokens_in: .prompt_eval_count, tokens_out: .eval_count, total_s: (.total_duration/1000000000)}'
