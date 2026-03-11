using Microsoft.Extensions.Logging;
using SherpaTravelScraper.Models;

namespace SherpaTravelScraper.Services;

/// <summary>
/// Genera todas las combinaciones N×N de nacionalidades para procesar
/// </summary>
public class CombinacionGenerator
{
    private readonly ILogger<CombinacionGenerator> _logger;

    public CombinacionGenerator(ILogger<CombinacionGenerator> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Genera todas las combinaciones válidas origen-destino con fallback EN-US
    /// </summary>
    public List<Combinacion> GenerarCombinaciones(List<Nacionalidad> nacionalidades, int ejecucionId)
    {
        var combinaciones = new List<Combinacion>();
        var activas = nacionalidades.Where(n => n.EsActivo).ToList();

        _logger.LogInformation("Generando combinaciones para {Count} nacionalidades activas", activas.Count);

        foreach (var origen in activas.Where(n => n.PuedeSerOrigen))
        {
            foreach (var destino in activas.Where(n => n.PuedeSerDestino))
            {
                // Omitir cuando origen == destino
                if (origen.CodigoIso3 == destino.CodigoIso3)
                    continue;

                // Crear combinación con idioma default del origen
                combinaciones.Add(new Combinacion
                {
                    EjecucionId = ejecucionId,
                    Origen = origen.CodigoIso3,
                    Destino = destino.CodigoIso3,
                    Idioma = origen.IdiomaDefault,
                    TipoNacionalidad = origen.Tipo,
                    Estado = "P"
                });

                // Generar fallback EN-US si el idioma default no es EN-US
                if (origen.IdiomaDefault != "EN-US")
                {
                    combinaciones.Add(new Combinacion
                    {
                        EjecucionId = ejecucionId,
                        Origen = origen.CodigoIso3,
                        Destino = destino.CodigoIso3,
                        Idioma = "EN-US",
                        TipoNacionalidad = origen.Tipo,
                        Estado = "P"
                    });
                }
            }
        }

        _logger.LogInformation("Total combinaciones generadas: {Count}", combinaciones.Count);
        return combinaciones;
    }

    /// <summary>
    /// Calcula el total esperado de combinaciones (para validación)
    /// </summary>
    public static int CalcularTotalEsperado(List<Nacionalidad> nacionalidades)
    {
        var activas = nacionalidades.Where(n => n.EsActivo).ToList();
        var origenes = activas.Count(n => n.PuedeSerOrigen);
        var destinos = activas.Count(n => n.PuedeSerDestino);
        
        // Restar las combinaciones donde un país es origen y destino (no permitido)
        var mismoPaisOrigenDestino = activas.Count(n => n.PuedeSerOrigen && n.PuedeSerDestino);
        
        var combinacionesGeograficas = (origenes * destinos) - mismoPaisOrigenDestino;
        
        // Contar cuántos tienen fallback EN-US
        var conFallback = activas.Count(n => n.PuedeSerOrigen && n.IdiomaDefault != "EN-US");
        var destinosValidos = activas.Count(n => n.PuedeSerDestino);
        var fallbackExtras = conFallback * destinosValidos - conFallback;
        
        return combinacionesGeograficas + fallbackExtras;
    }
}
