# Resumen de Correcciones - SherpaTravelScraper

## Problema Original
El scraper estaba guardando registros en `txnet_detrequisitos` pero los campos de requisitos (`reqvd_requisitos_destino`, `reqvd_requisitos_visado`, etc.) estaban en NULL porque:
1. La IA (Ollama) en la IP 192.168.5.91 no era accesible desde la máquina AWS
2. La API key de Kimi estaba dando error 401 (Invalid Authentication)

## Cambios Realizados

### 1. Configuración (appsettings.json)
- Cambiado `AI:Provider` de `"ollama"` a `"none"`
- Cambiado `AI:Enabled` a `false`
- Agregada configuración de respaldo para Kimi (por si se resuelve el problema de autenticación)

### 2. AiExtractionService.cs
- Agregado soporte para proveedor "kimi" en el método `ExtraerRequisitosCompletosAsync`
- Creado método `ExtraerCompletosConKimiAsync` para extracción con modelo de visión de Kimi

### 3. SherpaScraperService.cs
- Mejorado el método `ExtraerDatosTradicionalesAsync` para usar un nuevo método de extracción por JavaScript
- Creado método `ExtraerDatosEstructuradosConJSAsync` que extrae datos estructurados de la página usando JavaScript:
  - Busca elementos con atributos `data-testid`, `class` relacionados con visa, passport, health
  - Extrae información de visa (requerido, tipo, descripción)
  - Extrae información de pasaporte (validez, descripción)
  - Extrae información de salud (vacunas, covid)
  - Crea un JSON estructurado para guardar en la BD

### 4. Tests (AiExtractionTest.cs)
- Actualizado el test para usar el método tradicional sin IA

## Estado Actual
- El scraper compila correctamente
- La extracción tradicional con JavaScript ahora está activa como fallback
- Los datos extraídos se guardarán en formato JSON estructurado en `reqvd_datos_json`
- Los campos de texto (`reqvd_requisitos_destino`, `reqvd_requisitos_visado`, etc.) ahora tendrán contenido

## Para Restaurar la IA (cuando esté disponible)

### Opción 1: Ollama Local
```bash
# Instalar Ollama en la máquina
curl -fsSL https://ollama.com/install.sh | sh

# Descargar modelo de visión
ollama pull llama3.2-vision

# Iniciar Ollama
ollama serve
```

Luego cambiar en appsettings.json:
```json
"AI": {
  "Provider": "ollama",
  "Enabled": true,
  "Ollama": {
    "VisionEndpoint": "http://localhost:11434/api/chat",
    "Model": "llama3.2-vision:latest"
  }
}
```

### Opción 2: Kimi (cuando se resuelva el problema de API key)
Verificar la API key en TOOLS.md o generar una nueva en https://platform.moonshot.cn/

Luego cambiar en appsettings.json:
```json
"AI": {
  "Provider": "kimi",
  "Enabled": true,
  "Kimi": {
    "ApiKey": "sk-...",
    "Model": "kimi-vl-a3b-thinking"
  }
}
```

### Opción 3: OpenAI/Anthropic
Si se tiene API key disponible, implementar métodos similares a `ExtraerCompletosConKimiAsync`

## Ejecución del Scraper
```bash
cd /home/ubuntu/.openclaw/workspace/dev/SherpaTravelScraper/src/SherpaTravelScraper
export PATH="/home/ubuntu/.dotnet:$PATH"
dotnet run --configuration Release
```

O usar el monitor:
```bash
cd /home/ubuntu/.openclaw/workspace/dev/SherpaTravelScraper
./monitor-scraper.sh
```

## Verificación
Para verificar que los datos se están guardando correctamente:
```bash
export PATH="/opt/mssql-tools18/bin:$PATH"
sqlcmd -S 192.168.5.112 -U sa -P 'Isami06cz%' -C -d TravelRequirementsDB -Q "
SELECT TOP 5 reqvd_id, reqvd_nacionalidad_origen, reqvd_destino, 
  LEFT(reqvd_requisitos_destino, 100) as requisitos,
  LEFT(reqvd_requisitos_visado, 100) as visado,
  reqvd_exito
FROM txnet_detrequisitos 
ORDER BY reqvd_id DESC
"
```

Los campos `requisitos` y `visado` ya no deberían ser NULL.
