# SherpaTravelScraper_v3 - Estado

- Fecha inicio: 2026-03-11
- Estado: [READY_FOR_TESTING]
- Base: clon técnico de SherpaTravelScraper (hardening aplicado sin artefactos de build)

## Cambios v3 aplicados
1. Hardening de repositorio: limpieza de artefactos (`bin/`, `obj/`, `.vs/`, `.env.backup`) y refuerzo de `.gitignore`.
2. Lógica por `RENA_TIPONACIONALIDAD` implementada en orquestación + scraper:
   - `ORIGEN` => extracción solo tab **Departure**
   - `DESTINO` => extracción solo tab **Return**
   - `AMBOS` => extracción de ambos tabs
3. Soporte para extracción parcial en resultados (metadata de tabs extraídas y flujo tolerante a tab único).
4. Build de solución ejecutado con resultado OK.
