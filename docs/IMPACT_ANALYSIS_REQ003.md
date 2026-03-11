# REQ-SHERPA-003: Análisis de Impacto

**Fecha:** 2026-03-11  
**Proyecto:** SherpaTravelScraper_v3  
**Requerimiento:** Estrategia híbrida de scraping - URL directa + fallback a formulario

---

## 1. Objetivo Funcional

Implementar un flujo híbrido de scraping que:
1. **Intento primario**: Navegar directamente a una URL generada dinámicamente con parámetros
2. **Fallback automático**: Si el contenido no está disponible o es incompleto, proceder con el método actual de llenado de formulario

---

## 2. URL Template Analizada

```
https://apply.joinsherpa.com/travel-restrictions/{destino}?language={locale}
  &nationality={tupasaporte}
  &originCountry={origen}
  &travelPurposes=TOURISM
  &departureDate={fehainicial}
  &returnDate={fechafinal}
  &tripType=roundTrip
  &affiliateId=sherpa
  &fullyVaccinated=true
```

**Ejemplo real:**
```
https://apply.joinsherpa.com/travel-restrictions/USA?language=es-ES
  &nationality=COL
  &originCountry=COL
  &travelPurposes=TOURISM
  &departureDate=2026-03-11
  &returnDate=2026-03-18
  &tripType=roundTrip
  &affiliateId=sherpa
  &fullyVaccinated=true
```

---

## 3. Mapeo de Parámetros

| Parámetro URL | Campo en sistema | Origen en BD |
|---------------|------------------|--------------|
| `{destino}` | País destino | `Combinacion.Destino` |
| `{locale}` | Idioma | `Combinacion.Locale` o default "es-ES" |
| `{tupasaporte}` | Nacionalidad pasaporte | `Combinacion.Pasaporte` |
| `{origen}` | País origen | `Combinacion.Origen` |
| `{fehainicial}` | Fecha salida | `Combinacion.FechaInicial` o default +1 día |
| `{fechafinal}` | Fecha regreso | `Combinacion.FechaFinal` o default +8 días |

**Valores fijos:**
- `travelPurposes=TOURISM`
- `tripType=roundTrip`
- `affiliateId=sherpa`
- `fullyVaccinated=true`

---

## 4. Módulos Afectados

### 4.1 `SherpaScraperService.cs`
**Impacto:** ALTO
- Nuevo método `ScrapeWithDirectUrlAsync()`
- Modificación de `ScrapeAsync()` para intentar URL primero
- Lógica de verificación de contenido cargado
- Fallback al método existente `FillFormAndScrapeAsync()`

### 4.2 `TravelScrapingOrchestrator.cs`
**Impacto:** MEDIO
- Posible ajuste en la llamada al scraper
- Métricas de éxito/falla por método
- Logging del método utilizado

### 4.3 `Combinacion.cs` (Modelo)
**Impacto:** BAJO
- Agregar propiedades para fechas si no existen
- `FechaInicial`, `FechaFinal`, `Locale`

### 4.4 `Program.cs`
**Impacto:** BAJO
- Posible configuración adicional para timeouts de URL directa

---

## 5. Análisis Técnico: ¿Funcionará la URL Directa?

### 5.1 Hipótesis
La URL con parámetros debería cargar la misma página resultado que se obtiene después de llenar el formulario y hacer clic en "See requirements".

### 5.2 Factores a Considerar

| Factor | Análisis | Riesgo |
|--------|----------|--------|
| **JavaScript Rendering** | Sherpa es una SPA (Single Page Application). La URL podría requerir JS execution igual que el formulario. | MEDIO |
| **Estado de sesión** | No se identifican cookies de sesión requeridas en la URL. | BAJO |
| **Rate limiting** | URLs directas podrían ser más susceptibles a rate limiting. | MEDIO |
| **CORS/Validaciones** | La validación de parámetros podría rechazar algunas combinaciones. | BAJO |
| **Contenido dinámico** | Los tabs Departure/Return podrían cargar lazy. | ALTO |

### 5.3 Estrategia de Verificación de Contenido

Para determinar si la URL directa funcionó:

1. **Check 1: Elementos de tabs presentes**
   - Verificar existencia de botones/tab Departure y Return
   - Timeout: 5 segundos

2. **Check 2: Contenido no vacío**
   - Verificar que el tab activo tenga texto/html sustancial
   - Threshold: >500 caracteres

3. **Check 3: Estructura esperada**
   - Buscar elementos clave: visa requirements, passport info, documents

4. **Fallback trigger:**
   - Si cualquiera de los checks falla → proceder con formulario

---

## 6. Compatibilidad con RENA_TIPONACIONALIDAD

La lógica existente debe preservarse:

| Tipo | Extracción URL Directa | Extracción Formulario |
|------|------------------------|----------------------|
| `ORIGEN` | Solo tab Departure | Solo tab Departure |
| `DESTINO` | Solo tab Return | Solo tab Return |
| `AMBOS` | Ambos tabs | Ambos tabs |

**Nota:** La URL directa carga ambos tabs, pero según el tipo, solo se extrae el relevante.

---

## 7. Riesgos Identificados

| ID | Riesgo | Probabilidad | Impacto | Mitigación |
|----|--------|--------------|---------|------------|
| R1 | URL directa no carga contenido completo | ALTA | MEDIO | Fallback garantizado al formulario |
| R2 | Diferencias en DOM entre métodos | MEDIA | ALTO | Normalización de extracción |
| R3 | Timeout de URL más largo que formulario | BAJA | BAJO | Configurar timeout corto para URL |
| R4 | Rate limiting por requests directos | MEDIA | MEDIO | Retry con backoff + fallback |
| R5 | Regresión en método de formulario existente | BAJA | ALTO | Tests de regresión obligatorios |

---

## 8. Supuestos

1. La URL con parámetros es válida y documentada por Sherpa
2. Los parámetros de fecha deben estar en formato ISO (YYYY-MM-DD)
3. El método de formulario actual continuará funcionando como fallback
4. No se requiere autenticación adicional para la URL directa
5. El contenido de los tabs es equivalente entre ambos métodos de acceso

---

## 9. Preguntas Abiertas

1. ¿Existe documentación oficial de Sherpa sobre estos parámetros de URL?
2. ¿Hay parámetros adicionales no documentados que sean necesarios?
3. ¿El parámetro `language` acepta otros formatos además de `es-ES`?
4. ¿Es necesario incluir `affiliateId=sherpa` o puede omitirse?

---

## 10. Conclusión del Análisis

**Estado:** [ANALYSIS_READY]

El requerimiento es **técnicamente viable**. La estrategia híbrida agregará resiliencia al sistema:
- Si la URL directa funciona: más rápido (sin llenar formulario)
- Si falla: fallback automático al método probado

**Recomendación:** Proceder al diseño técnico detallado (GATE-1).

**Próximo paso:** Crear `TECH_DESIGN.md` con:
- Diagrama de flujo del método híbrido
- Interfaz/contracto del nuevo método
- Estrategia de logging/métricas
- Plan de testing

---

*Análisis realizado por: Coordinador Principal*  
*Fecha: 2026-03-11*
