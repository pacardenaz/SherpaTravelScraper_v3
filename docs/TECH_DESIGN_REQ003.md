# REQ-SHERPA-003: Diseño Técnico

**Fecha:** 2026-03-11  
**Proyecto:** SherpaTravelScraper_v3  
**Requerimiento:** Estrategia híbrida de scraping - URL directa + fallback

---

## 1. Diagrama de Flujo del Método Híbrido

```
┌─────────────────────────────────────────────────────────────────┐
│  INICIO: Scrapear Combinación (Origen, Destino, Pasaporte)     │
└─────────────────────────────────────────────────────────────────┘
                            │
                            ▼
┌─────────────────────────────────────────────────────────────────┐
│  PASO 1: Generar URL Directa                                    │
│  - Construir URL con parámetros dinámicos                      │
│  - Incluir fechas default (+1 día, +8 días)                    │
└─────────────────────────────────────────────────────────────────┘
                            │
                            ▼
┌─────────────────────────────────────────────────────────────────┐
│  PASO 2: Navegar a URL Directa (con timeout corto: 15s)        │
│  - Page.GotoAsync(url, WaitUntilState.NetworkIdle)             │
└─────────────────────────────────────────────────────────────────┘
                            │
                            ▼
                    ┌───────────────┐
                    │  ¿Carga       │
                    │  exitosa?     │
                    └───────────────┘
                     SÍ │        │ NO
                        ▼        ▼
┌──────────────────────────┐  ┌─────────────────────────────────┐
│  PASO 3a: Verificar      │  │  PASO 3b: Fallback a Formulario │
│  Contenido               │  │  - Navegar a página base        │
│  - ¿Tabs presentes?      │  │  - Llenar formulario            │
│  - ¿Contenido >500 chars?│  │  - Click "See requirements"     │
│  - ¿Estructura válida?   │  │  - Esperar carga de resultado   │
└──────────────────────────┘  └─────────────────────────────────┘
            │                              │
    SÍ      │      NO                     │
     ▼      │       ▼                     ▼
┌───────────┴──────────┐      ┌─────────────────────────────────┐
│  PASO 4a: Extraer    │      │  PASO 4b: Extraer desde         │
│  desde URL Directa   │      │  Formulario (método actual)     │
│  - Extraer Departure │      │  - Extraer Departure            │
│  - Extraer Return    │      │  - Extraer Return               │
└──────────────────────┘      └─────────────────────────────────┘
            │                              │
            └──────────────┬───────────────┘
                           ▼
┌─────────────────────────────────────────────────────────────────┐
│  PASO 5: Guardar en BD                                          │
│  - Aplicar lógica RENA_TIPONACIONALIDAD                        │
│  - Almacenar JSON según tipo (ORIGEN/DESTINO/AMBOS)            │
└─────────────────────────────────────────────────────────────────┘
                           │
                           ▼
┌─────────────────────────────────────────────────────────────────┐
│  FIN: Retornar resultado                                        │
└─────────────────────────────────────────────────────────────────┘
```

---

## 2. Cambios en la Arquitectura

### 2.1 Nueva Interfaz: `ISherpaScraperService`

```csharp
public interface ISherpaScraperService
{
    /// <summary>
    /// Scrapea una combinación usando estrategia híbrida:
    /// 1. Intenta URL directa primero
    /// 2. Fallback a formulario si falla
    /// </summary>
    Task<ScrapingResult> ScrapeAsync(
        Combinacion combinacion, 
        CancellationToken ct = default);
    
    /// <summary>
    /// Scrapea usando URL directa con parámetros
    /// </summary>
    Task<ScrapingResult?> ScrapeWithDirectUrlAsync(
        Combinacion combinacion, 
        CancellationToken ct = default);
    
    /// <summary>
    /// Scrapea usando formulario (método legacy)
    /// </summary>
    Task<ScrapingResult> ScrapeWithFormAsync(
        Combinacion combinacion, 
        CancellationToken ct = default);
}
```

### 2.2 Nuevo Modelo: `ScrapingResult`

```csharp
public class ScrapingResult
{
    public string? DepartureHtml { get; set; }
    public string? ReturnHtml { get; set; }
    public ScrapingMethod UsedMethod { get; set; }
    public TimeSpan Duration { get; set; }
    public bool IsPartial { get; set; }
    public string? ErrorMessage { get; set; }
}

public enum ScrapingMethod
{
    DirectUrl,      // URL con parámetros funcionó
    FormFill,       // Fallback a formulario
    Unknown
}
```

### 2.3 Nuevo Servicio: `UrlBuilderService`

```csharp
public class UrlBuilderService
{
    public string BuildSherpaUrl(Combinacion combinacion)
    {
        var sb = new StringBuilder();
        sb.Append($"https://apply.joinsherpa.com/travel-restrictions/{combinacion.Destino}");
        sb.Append($"?language={combinacion.Locale ?? "es-ES"}");
        sb.Append($"&nationality={combinacion.Pasaporte}");
        sb.Append($"&originCountry={combinacion.Origen}");
        sb.Append($"&travelPurposes=TOURISM");
        sb.Append($"&departureDate={GetDepartureDate(combinacion)}");
        sb.Append($"&returnDate={GetReturnDate(combinacion)}");
        sb.Append($"&tripType=roundTrip");
        sb.Append($"&affiliateId=sherpa");
        sb.Append($"&fullyVaccinated=true");
        return sb.ToString();
    }
    
    private string GetDepartureDate(Combinacion c) 
        => c.FechaInicial?.ToString("yyyy-MM-dd") 
           ?? DateTime.Now.AddDays(1).ToString("yyyy-MM-dd");
    
    private string GetReturnDate(Combinacion c)
        => c.FechaFinal?.ToString("yyyy-MM-dd")
           ?? DateTime.Now.AddDays(8).ToString("yyyy-MM-dd");
}
```

---

## 3. Implementación del Verificador de Contenido

```csharp
public class ContentVerifier
{
    private readonly IPage _page;
    
    public ContentVerifier(IPage page)
    {
        _page = page;
    }
    
    /// <summary>
    /// Verifica si el contenido cargado es válido y completo
    /// </summary>
    public async Task<ContentVerificationResult> VerifyAsync(
        CancellationToken ct = default)
    {
        var result = new ContentVerificationResult();
        
        // Check 1: Tabs presentes
        result.HasDepartureTab = await _page.IsVisibleAsync(
            "[data-testid='departure-tab'], button:has-text('Departure'), [role='tab']:has-text('Departure')");
        
        result.HasReturnTab = await _page.IsVisibleAsync(
            "[data-testid='return-tab'], button:has-text('Return'), [role='tab']:has-text('Return')");
        
        // Check 2: Contenido del tab activo
        var activeTabContent = await _page.InnerTextAsync(
            ".requirements-content, [data-testid='requirements-content'], .tab-content");
        
        result.ActiveTabContentLength = activeTabContent?.Length ?? 0;
        result.HasSubstantialContent = result.ActiveTabContentLength > 500;
        
        // Check 3: Estructura esperada
        result.HasVisaSection = await _page.IsVisibleAsync(
            "text=Visa, text=visa, .visa-requirement");
        result.HasPassportSection = await _page.IsVisibleAsync(
            "text=Passport, text=pasaporte, .passport-requirement");
        
        // Determinar éxito
        result.IsValid = result.HasDepartureTab 
                      && result.HasReturnTab 
                      && result.HasSubstantialContent;
        
        return result;
    }
}

public class ContentVerificationResult
{
    public bool IsValid { get; set; }
    public bool HasDepartureTab { get; set; }
    public bool HasReturnTab { get; set; }
    public int ActiveTabContentLength { get; set; }
    public bool HasSubstantialContent { get; set; }
    public bool HasVisaSection { get; set; }
    public bool HasPassportSection { get; set; }
}
```

---

## 4. Estrategia de Logging y Métricas

```csharp
public class ScrapingMetrics
{
    // Métricas a recolectar
    public int TotalAttempts { get; set; }
    public int DirectUrlSuccess { get; set; }
    public int FormFillSuccess { get; set; }
    public int Failures { get; set; }
    
    // Tiempos promedio
    public TimeSpan AvgDirectUrlTime { get; set; }
    public TimeSpan AvgFormFillTime { get; set; }
}

// En el servicio:
_logger.LogInformation(
    "REQ-SHERPA-003: Método={Method}, Éxito={Success}, Duración={Duration}ms, Combinación={Origen}-{Destino}",
    result.UsedMethod,
    string.IsNullOrEmpty(result.ErrorMessage),
    result.Duration.TotalMilliseconds,
    combinacion.Origen,
    combinacion.Destino);
```

---

## 5. Configuración

```json
{
  "Scraping": {
    "HybridMode": {
      "Enabled": true,
      "DirectUrlTimeoutSeconds": 15,
      "VerifyContentTimeoutSeconds": 5,
      "MinContentLength": 500,
      "FallbackOnVerificationFailure": true
    }
  }
}
```

---

## 6. Plan de Testing

### 6.1 Tests Unitarios
- `UrlBuilderServiceTests`: Validar construcción correcta de URLs
- `ContentVerifierTests`: Mock de página Playwright, verificar lógica de validación

### 6.2 Tests de Integración
- `HybridScrapingTests`: Verificar flujo completo con URLs reales
- `FallbackTests`: Forzar fallo de URL directa, verificar que fallback funciona

### 6.3 Tests de Regresión
- Verificar que el método de formulario sigue funcionando igual
- Verificar compatibilidad con `RENA_TIPONACIONALIDAD`

---

## 7. Plan de Implementación

### Fase 1: Estructura base (30 min)
1. Crear `UrlBuilderService`
2. Crear `ContentVerifier`
3. Crear modelos `ScrapingResult`, `ScrapingMethod`

### Fase 2: Lógica híbrida (1 hora)
1. Modificar `SherpaScraperService` con método híbrido
2. Implementar `ScrapeWithDirectUrlAsync`
3. Implementar lógica de fallback

### Fase 3: Integración (30 min)
1. Actualizar `TravelScrapingOrchestrator`
2. Agregar métricas y logging
3. Actualizar inyección de dependencias

### Fase 4: Testing (1 hora)
1. Tests unitarios
2. Tests de integración
3. Validación de regresión

---

## 8. Criterios de Aceptación Técnicos

- [ ] `UrlBuilderService` genera URLs correctamente para todas las combinaciones
- [ ] `ContentVerifier` detecta contenido válido con >= 90% precisión
- [ ] Fallback ocurre automáticamente en < 20 segundos si URL directa falla
- [ ] Método de formulario (legacy) sigue funcionando sin cambios
- [ ] Métricas se registran correctamente para análisis posterior
- [ ] No hay regresión en `RENA_TIPONACIONALIDAD`
- [ ] Build pasa sin errores ni warnings
- [ ] Todos los tests nuevos pasan
- [ ] Tests de regresión existentes siguen pasando

---

**Estado:** [DESIGN_READY]  
**Próximo paso:** Iniciar implementación (GATE-2)

*Diseño realizado por: Coordinador Principal*  
*Fecha: 2026-03-11*
