using System.Text;
using Microsoft.Extensions.Logging;
using SherpaTravelScraper.Models;

namespace SherpaTravelScraper.Services;

/// <summary>
/// Servicio para construir URLs dinámicas de Sherpa con parámetros
/// </summary>
public class UrlBuilderService
{
    private readonly ILogger<UrlBuilderService> _logger;
    
    public UrlBuilderService(ILogger<UrlBuilderService> logger)
    {
        _logger = logger;
    }
    
    /// <summary>
    /// Construye la URL base para navegación a página de formulario
    /// </summary>
    public string BuildBaseUrl(string destinoIso3)
    {
        return $"https://apply.joinsherpa.com/travel-restrictions/{destinoIso3}";
    }
    
    /// <summary>
    /// Construye URL completa con parámetros para navegación directa
    /// </summary>
    public string BuildDirectUrl(
        string destinoIso3,
        string origenIso3,
        string nacionalidadIso3,
        string? idioma = null,
        DateTime? fechaSalida = null,
        DateTime? fechaRegreso = null)
    {
        var sb = new StringBuilder();
        
        // URL base
        sb.Append($"https://apply.joinsherpa.com/travel-restrictions/{destinoIso3}");
        
        // Parámetros obligatorios
        var locale = MapIdiomaALocale(idioma ?? "EN-US");
        sb.Append($"?language={locale}");
        sb.Append($"&nationality={nacionalidadIso3}");
        sb.Append($"&originCountry={origenIso3}");
        
        // Propósito de viaje (fijo)
        sb.Append("&travelPurposes=TOURISM");
        
        // Fechas
        var departureDate = fechaSalida ?? DateTime.Now.AddDays(1);
        var returnDate = fechaRegreso ?? DateTime.Now.AddDays(8);
        sb.Append($"&departureDate={departureDate:yyyy-MM-dd}");
        sb.Append($"&returnDate={returnDate:yyyy-MM-dd}");
        
        // Tipo de viaje (fijo)
        sb.Append("&tripType=roundTrip");
        
        // Affiliate ID (fijo)
        sb.Append("&affiliateId=sherpa");
        
        // Vacunación (fijo)
        sb.Append("&fullyVaccinated=true");
        
        var url = sb.ToString();
        
        _logger.LogDebug("URL construida: {Url}", url);
        
        return url;
    }
    
    /// <summary>
    /// Construye URL directa desde un objeto Combinacion
    /// </summary>
    public string BuildDirectUrlFromCombinacion(Combinacion combinacion, DateTime? fechaBase = null)
    {
        var baseDate = fechaBase ?? DateTime.Now;
        
        return BuildDirectUrl(
            destinoIso3: combinacion.Destino,
            origenIso3: combinacion.Origen,
            nacionalidadIso3: combinacion.Origen, // Asumimos que la nacionalidad es el origen
            idioma: combinacion.Idioma,
            fechaSalida: baseDate.AddDays(1),
            fechaRegreso: baseDate.AddDays(8)
        );
    }
    
    /// <summary>
    /// Mapea código de idioma interno a formato locale de Sherpa
    /// </summary>
    private string MapIdiomaALocale(string idioma)
    {
        return idioma.ToUpper() switch
        {
            "ES" or "ES-ES" => "es-ES",
            "EN" or "EN-US" => "en-US",
            "PT" or "PT-BR" => "pt-BR",
            "FR" or "FR-FR" => "fr-FR",
            "DE" or "DE-DE" => "de-DE",
            _ => "en-US" // Default
        };
    }
}
