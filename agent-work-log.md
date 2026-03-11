# Log de Trabajo - SherpaTravelScraper Fix

## Tarea: Fix SherpaTravelScraper - Guardar JSON y Markdown correctamente

**Problema:** La IA genera markdown con JSON embebido, pero el código actual no extrae el JSON correctamente.

**Inicio:** 2026-03-08 03:11 UTC

### Pasos a seguir:
1. Modificar AiExtractionService.cs para extraer JSON del markdown de Ollama
2. Compilar (corregir errores si los hay)
3. Limpiar tabla txnet_detrequisitos
4. Ejecutar proyecto
5. Verificar en BD que JSON y Markdown se guardan correctamente
6. Si falla, analizar y corregir

---

### Progreso:

**03:11 UTC** - Buscando ubicación del proyecto...
- Proyecto encontrado en: /home/ubuntu/.openclaw/workspace/dev/SherpaTravelScraper/src/SherpaTravelScraper/

**03:13 UTC** - Análisis del problema:
- El método `ExtraerCompletosConOllamaAsync` devolvía el markdown directamente sin parsear
- El método `ParsearRespuestaCompleta` ya existía con la lógica para extraer JSON del markdown
- Se modificó el código para usar `ParsearRespuestaCompleta` y guardar tanto el JSON parseado como el markdown original

**03:14 UTC** - Compilando proyecto...
- Build exitoso

**03:15 UTC** - Limpieza de tabla txnet_detrequisitos
- 2 filas eliminadas

**03:18 UTC** - Primera ejecución de prueba
- El JSON parseado se guardaba como markdown en lugar de JSON estructurado
- Identificado problema en SherpaScraperService.cs: se pasaba `extraccion.Markdown` como datos en lugar del JSON serializado

**03:19 UTC** - Segunda modificación (SherpaScraperService.cs)
- Se modificó para serializar el objeto `RequisitosViajeCompleto` a JSON usando `JsonSerializer.Serialize()`
- Se corrigió error SQL en `MarcarFallidaAsync` (faltaba `= 1` en `WHEN @EsBloqueo = 1`)

**03:21 UTC** - Verificación en base de datos
- JSON guardado correctamente con estructura: `{"infoViaje": {"origen": "ARG", "destino": "COL", ...}}`
- Markdown guardado correctamente con contenido original de la IA
- Ambos campos tienen datos válidos

---

### Cambios realizados:

1. **AiExtractionService.cs** (línea ~182):
   - Se modificó para usar `ParsearRespuestaCompleta()` en lugar de devolver markdown directamente
   - Se agregó lógica para guardar el markdown en `resultado.Markdown` cuando el parseo es exitoso
   - Fallback con baja confianza (0.3) cuando no se puede parsear el JSON

2. **SherpaScraperService.cs** (línea ~356):
   - Se agregó serialización del objeto `RequisitosViajeCompleto` a JSON
   - Se pasa el JSON como `datos` y el markdown como `markdown` separadamente

3. **TravelRepository.cs** (línea ~240):
   - Se corrigió error SQL: `WHEN @EsBloqueo THEN` → `WHEN @EsBloqueo = 1 THEN`

---

### Estado: ✅ COMPLETADO
- JSON y Markdown se guardan correctamente en la base de datos
- La estructura del JSON es válida y completa
- El markdown original se preserva para referencia
