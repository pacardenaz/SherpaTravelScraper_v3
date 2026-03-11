# SherpaTravelScraper_v3 - Project Requirements

## Resumen Ejecutivo
Sistema batch resiliente en C# .NET 10 para web scraping de requisitos de viaje desde https://apply.joinsherpa.com/travel-restrictions/, procesando combinaciones N×N de nacionalidades y almacenando resultados en SQL Server.

## Diferenciadores v3
1. Respeta `RENA_TIPONACIONALIDAD` (ORIGEN / DESTINO / AMBOS) para decidir qué pestañas extraer.
2. Estrategia AI HTML con OpenRouter para parseo estructurado.
3. Mantiene fallback DOM + resiliencia + checkpointing.

## Reglas de Extracción por Tipo
- `ORIGEN`  -> extraer solo `Departure`
- `DESTINO` -> extraer solo `Return`
- `AMBOS`   -> extraer `Departure` y `Return`

## Restricción Crítica de Scraping
NO usar acceso GET directo como única estrategia. Se debe usar flujo de formulario:
1) abrir URL base
2) completar passport/from/to/fechas
3) click `See requirements`
4) esperar resultados
5) extraer tabs según tipo configurado.

## Arquitectura
- `CombinacionGenerator`
- `SherpaScraperService`
- `TravelRepository`
- `TravelScrapingOrchestrator`
- Estrategias de extracción: `DomScraping | AiVision | AiHtml`

## Configuración mínima (appsettings)
- `ConnectionStrings:DefaultConnection`
- `Playwright:Headless`
- `Scraping:*`
- `UserAgents[]`
- `AI:BaseUrl` (OpenRouter)
- `AI:ApiKey`
- `AI:Model`

## Criterios de Aceptación
- Genera matriz N×N válida
- Delays aleatorios 3-8s
- Stealth Playwright activo
- Checkpointing y reanudación
- Manejo de errores sin detener batch
- Persistencia de HTML raw + JSON estructurado
- Compila en .NET 10

## Estado
- Inicializado desde base existente para acelerar desarrollo v3.
- Pendiente: ajuste final de naming/metadata v3 en solución y docs.
