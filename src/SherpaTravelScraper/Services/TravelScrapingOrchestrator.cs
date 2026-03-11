using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using SherpaTravelScraper.Models;
using SherpaTravelScraper.Utils;

namespace SherpaTravelScraper.Services;

/// <summary>
/// Orquestador principal del proceso de scraping
/// </summary>
public class TravelScrapingOrchestrator
{
    private readonly TravelRepository _repository;
    private readonly CombinacionGenerator _generator;
    private readonly SherpaScraperService _scraper;
    private readonly StealthConfig _stealthConfig;
    private readonly IConfiguration _configuration;
    private readonly ILogger<TravelScrapingOrchestrator> _logger;

    public TravelScrapingOrchestrator(
        TravelRepository repository,
        CombinacionGenerator generator,
        SherpaScraperService scraper,
        StealthConfig stealthConfig,
        IConfiguration configuration,
        ILogger<TravelScrapingOrchestrator> logger)
    {
        _repository = repository;
        _generator = generator;
        _scraper = scraper;
        _stealthConfig = stealthConfig;
        _configuration = configuration;
        _logger = logger;
    }

    /// <summary>
    /// Ejecuta el proceso completo de scraping
    /// </summary>
    public async Task EjecutarAsync(CancellationToken cancellationToken = default)
    {
        var inicio = DateTime.Now;
        _logger.LogInformation("=== INICIANDO PROCESO DE SCRAPING ===");
        _logger.LogInformation("Hora de inicio: {Inicio}", inicio);

        try
        {
            // Paso 1: Leer nacionalidades
            _logger.LogInformation("Paso 1: Leyendo nacionalidades...");
            var nacionalidades = await _repository.ObtenerNacionalidadesAsync();
            
            if (nacionalidades.Count == 0)
            {
                _logger.LogError("No hay nacionalidades activas en la base de datos");
                return;
            }

            _logger.LogInformation("{Count} nacionalidades cargadas", nacionalidades.Count);

            // Paso 2: Generar combinaciones
            _logger.LogInformation("Paso 2: Generando combinaciones N×N...");
            
            // Usar ID temporal para generar combinaciones
            var combinacionesTemp = _generator.GenerarCombinaciones(nacionalidades, 0);
            var totalCombinaciones = combinacionesTemp.Count;
            
            _logger.LogInformation("Total combinaciones a procesar: {Total}", totalCombinaciones);

            // Paso 3: Crear ejecución en BD
            var ejecucionId = await _repository.CrearEjecucionAsync(totalCombinaciones);
            
            // Actualizar IDs de combinaciones
            combinacionesTemp.ForEach(c => c.EjecucionId = ejecucionId);
            
            // Guardar en BD
            await _repository.GuardarCombinacionesPendientesAsync(ejecucionId, combinacionesTemp);

            // Paso 4: Inicializar scraper
            _logger.LogInformation("Paso 4: Inicializando scraper...");
            await _scraper.InicializarAsync();

            // Paso 5: Procesar combinaciones (LIMITADO A 5 PARA PRUEBA)
            await ProcesarCombinacionesAsync(ejecucionId, cancellationToken, limite: 5);

            // Paso 6: Finalizar
            await _repository.ActualizarProgresoAsync(ejecucionId);
            var ejecucion = await ObtenerEstadoEjecucionAsync(ejecucionId);
            var exitosa = ejecucion?.CombinacionesFallidas == 0;
            
            await _repository.FinalizarEjecucionAsync(ejecucionId, exitosa);

            var duracion = DateTime.Now - inicio;
            _logger.LogInformation("=== PROCESO FINALIZADO ===");
            _logger.LogInformation("Duración: {Duracion:hh\\:mm\\:ss}", duracion);
            _logger.LogInformation("Total: {Total}, OK: {Ok}, Fallidas: {Fallidas}",
                ejecucion?.TotalCombinaciones, ejecucion?.CombinacionesOk, ejecucion?.CombinacionesFallidas);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fatal durante la ejecución");
            throw;
        }
    }

    /// <summary>
    /// Procesa las combinaciones pendientes
    /// </summary>
    private async Task ProcesarCombinacionesAsync(int ejecucionId, CancellationToken cancellationToken, int? limite = null)
    {
        var maxReintentos = _configuration.GetValue<int>("Scraping:MaxReintentos", 3);
        var checkpointInterval = _configuration.GetValue<int>("Scraping:CheckpointInterval", 10);
        
        var procesadas = 0;
        var checkpointCount = 0;

        while (!cancellationToken.IsCancellationRequested)
        {
            // Obtener pendientes
            var pendientes = await _repository.ObtenerPendientesAsync(ejecucionId, maxReintentos);
            
            if (pendientes.Count == 0)
            {
                _logger.LogInformation("No hay más combinaciones pendientes");
                break;
            }

            _logger.LogInformation("Procesando lote de {Count} combinaciones...", pendientes.Count);

            foreach (var combinacion in pendientes)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    _logger.LogWarning("Cancelación solicitada. Deteniendo...");
                    return;
                }

                await ProcesarCombinacionIndividualAsync(combinacion);
                
                procesadas++;
                checkpointCount++;

                // Verificar límite para pruebas
                if (limite.HasValue && procesadas >= limite.Value)
                {
                    _logger.LogInformation("Límite de prueba alcanzado: {Limite} combinaciones. Deteniendo.", limite.Value);
                    return;
                }

                // Checkpoint cada N combinaciones
                if (checkpointCount >= checkpointInterval)
                {
                    await _repository.ActualizarProgresoAsync(ejecucionId);
                    _logger.LogInformation("Checkpoint: {Procesadas} combinaciones procesadas", procesadas);
                    checkpointCount = 0;
                }
            }

            // Actualizar progreso al final de cada lote
            await _repository.ActualizarProgresoAsync(ejecucionId);
        }
    }

    /// <summary>
    /// Procesa una combinación individual
    /// </summary>
    private async Task ProcesarCombinacionIndividualAsync(Combinacion combinacion)
    {
        var maxReintentos = _configuration.GetValue<int>("Scraping:MaxReintentos", 3);
        
        try
        {
            // Marcar como en proceso
            await _repository.MarcarEnProcesoAsync(combinacion.Id);
            _logger.LogDebug("Procesando: {Origen} -> {Destino} ({Idioma})", 
                combinacion.Origen, combinacion.Destino, combinacion.Idioma);

            // Realizar scraping (REQ-SHERPA-003: Estrategia híbrida)
            var fechaBase = DateTime.Now;
            var resultado = await _scraper.ScrapearConEstrategiaHibridaAsync(
                combinacion.Origen,
                combinacion.Destino,
                combinacion.Idioma,
                fechaBase,
                combinacion.TipoNacionalidad);

            if (resultado.Exitoso)
            {
                // Guardar resultado exitoso
                await _repository.GuardarRequisitoAsync(
                    combinacion.EjecucionId,
                    combinacion.Origen,
                    combinacion.Destino,
                    combinacion.Idioma,
                    resultado.UrlConsultada,
                    resultado.HtmlRaw,
                    resultado);

                await _repository.MarcarCompletadaAsync(combinacion.Id);
                _logger.LogDebug("✓ Completada: {Origen} -> {Destino}", 
                    combinacion.Origen, combinacion.Destino);
            }
            else
            {
                // Determinar si es bloqueo
                var esBloqueo = resultado.MensajeError?.Contains("BLOQUEO") == true ||
                               resultado.MensajeError?.Contains("403") == true;

                await _repository.MarcarFallidaAsync(combinacion.Id, resultado.MensajeError, esBloqueo);
                
                if (esBloqueo)
                {
                    _logger.LogWarning("⚠ Bloqueo detectado en {Origen} -> {Destino}. Esperando 5 minutos...",
                        combinacion.Origen, combinacion.Destino);
                    
                    // Esperar 5 minutos y rotar User-Agent
                    await Task.Delay(TimeSpan.FromMinutes(5));
                    _stealthConfig.GetNextUserAgent();
                }
                else
                {
                    _logger.LogWarning("✗ Fallida ({Intento}/{Max}): {Origen} -> {Destino} - {Error}",
                        combinacion.Reintentos + 1, maxReintentos,
                        combinacion.Origen, combinacion.Destino, resultado.MensajeError);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error procesando {Origen} -> {Destino}", 
                combinacion.Origen, combinacion.Destino);
            
            await _repository.MarcarFallidaAsync(combinacion.Id, ex.Message);
        }
    }

    /// <summary>
    /// Obtiene el estado actual de una ejecución
    /// </summary>
    private async Task<Ejecucion?> ObtenerEstadoEjecucionAsync(int ejecucionId)
    {
        // Simplificación: en una implementación real, agregar método específico al repo
        return new Ejecucion 
        { 
            Id = ejecucionId,
            // Los valores reales se actualizan en BD
        };
    }
}
