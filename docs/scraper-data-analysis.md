# Análisis de Datos - SherpaTravelScraper

**Fecha:** 2026-03-09  
**Ejecución:** Scraper con límite de 5 combinaciones (timeout 5 min)

## Resumen de Ejecución

### Limpieza de Base de Datos ✅
- **Tabla txnet_detrequisitos:** 3 filas eliminadas
- **Tabla txnet_combinaciones_procesar:** 4 filas actualizadas a estado 'P'
- **Resultado:** Base de datos limpia lista para pruebas

### Ejecución del Scraper
- **Combinaciones procesadas:** 3 (ARG→CAN ES, ARG→CAN EN-US, ARG→COL ES)
- **Estado:** Timeout después de ~5 minutos (comportamiento esperado)
- **Logs:** Navegación exitosa, extracción JavaScript ejecutada

## Análisis de Datos Extraídos

### Registros Guardados
| ID | Origen | Destino | JSON | Markdown | Éxito |
|----|--------|---------|------|----------|-------|
| 152 | ARG | CAN | 285 chars | 0 chars | Sí |
| 153 | ARG | CAN | 285 chars | 0 chars | Sí |
| 154 | ARG | COL | 285 chars | 0 chars | Sí |

**Total:** 3 registros

### Calidad de Datos

#### ✅ Lo que funciona:
1. **JSON válido:** Todos los registros tienen JSON parseable
2. **Estructura base presente:** Tienen campos `infoViaje`, `departure`, `confianza`, `extraidoCon`
3. **Guardado en BD:** Los registros se persisten correctamente

#### ❌ Problemas identificados:

1. **Campos de requisitos en NULL:**
   - `reqvd_requisitos_destino`: NULL en todos
   - `reqvd_requisitos_visado`: NULL en todos
   - `reqvd_pasaportes_documentos`: NULL en todos
   - `reqvd_sanitarios`: NULL en todos

2. **JSON con datos vacíos:**
   - `infoViaje.origen` y `infoViaje.destino` están vacíos
   - No contiene información de visa/pasaporte/salud

3. **Markdown vacío:**
   - `reqvd_markdown` tiene 0 caracteres en todos los registros

4. **Porcentaje de éxito: 0%**
   - Ningún registro cumple con el criterio de datos completos

## Diagnóstico Técnico

### Causa Raíz
El método de extracción JavaScript no está capturando los datos correctamente del sitio web. Los logs muestran:
- Los selectores CSS no encuentran elementos (`[data-testid*='requirements']`: 0 elementos)
- La página carga pero no se extrae contenido estructurado
- El HTML guardado en debug muestra "My Sherpa" como título, pero sin contenido de requisitos

### Posibles causas:
1. **Cambios en el sitio web:** Sherpa puede haber actualizado su estructura HTML
2. **Carga dinámica:** El contenido puede cargarse vía JavaScript después del evento inicial
3. **Detección de bot:** El sitio podría estar detectando el scraper y mostrando contenido diferente
4. **Selectores obsoletos:** Los selectores configurados ya no coinciden con el DOM actual

## Recomendaciones

### Corto plazo:
1. **Revisar los selectores CSS** en `SherpaScraperService.cs` para adaptarlos a la estructura actual
2. **Aumentar tiempo de espera** para carga dinámica de contenido
3. **Agregar más logs de debug** para ver el HTML completo que se está recibiendo

### Mediano plazo:
1. **Implementar extracción con IA** usando OpenRouter (ya configurado pero desactivado)
2. **Agregar screenshots** como respaldo para extracción visual
3. **Considerar alternativas:** otras fuentes de datos de requisitos de viaje

### Largo plazo:
1. **Migrar a modelo híbrido:** JavaScript + IA de visión
2. **Monitoreo continuo** del sitio fuente para detectar cambios
3. **Cache de datos** para reducir dependencia del scraper en tiempo real

## Conclusión

El scraper está **funcionando técnicamente** (navega, extrae, guarda), pero la **calidad de los datos extraídos es insuficiente**. Se requiere trabajo de ajuste en los selectores de extracción o habilitar el método de IA para obtener datos útiles.

**Estado:** ⚠️ REQUIERE CORRECCIÓN

---
*Reporte generado automáticamente por el equipo de desarrollo*
