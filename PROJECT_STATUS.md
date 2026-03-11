# SherpaTravelScraper_v3 - Estado

- Fecha inicio: 2026-03-11
- Estado: [DESIGN_READY] - REQ-SHERPA-003 en diseño técnico
- Base: clon técnico de SherpaTravelScraper (hardening aplicado sin artefactos de build)

## Cambios v3 aplicados
1. Hardening de repositorio: limpieza de artefactos (`bin/`, `obj/`, `.vs/`, `.env.backup`) y refuerzo de `.gitignore`.
2. Lógica por `RENA_TIPONACIONALIDAD` implementada en orquestación + scraper:
   - `ORIGEN` => extracción solo tab **Departure**
   - `DESTINO` => extracción solo tab **Return**
   - `AMBOS` => extracción de ambos tabs
3. Soporte para extracción parcial en resultados (metadata de tabs extraídas y flujo tolerante a tab único).
4. Build de solución ejecutado con resultado OK.

## Requerimientos Activos

### REQ-SHERPA-003: Estrategia híbrida de scraping
- **Estado:** [DESIGN_READY]
- **Descripción:** Implementar flujo híbrido: intentar URL directa primero, fallback a formulario
- **Análisis:** `docs/IMPACT_ANALYSIS_REQ003.md`
- **Diseño:** `docs/TECH_DESIGN_REQ003.md`
- **Entregables pendientes:**
  - [ ] `UrlBuilderService` - Generador de URLs dinámicas
  - [ ] `ContentVerifier` - Verificador de contenido cargado
  - [ ] `ScrapingResult` / `ScrapingMethod` - Modelos de resultado
  - [ ] Modificación `SherpaScraperService` - Lógica híbrida
  - [ ] Tests unitarios e integración
  - [ ] QA de regresión
