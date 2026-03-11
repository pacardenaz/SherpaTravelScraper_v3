using SherpaTravelScraper.Models;

namespace SherpaTravelScraper.Services;

/// <summary>
/// Resultado de una operación de scraping
/// </summary>
public class ScrapingResult
{
    /// <summary>
    /// HTML del tab Departure
    /// </summary>
    public string? DepartureHtml { get; set; }
    
    /// <summary>
    /// HTML del tab Return
    /// </summary>
    public string? ReturnHtml { get; set; }
    
    /// <summary>
    /// Método utilizado para obtener el resultado
    /// </summary>
    public ScrapingMethod UsedMethod { get; set; }
    
    /// <summary>
    /// Duración total del scraping
    /// </summary>
    public TimeSpan Duration { get; set; }
    
    /// <summary>
    /// Indica si el resultado es parcial (solo un tab según TipoNacionalidad)
    /// </summary>
    public bool IsPartial { get; set; }
    
    /// <summary>
    /// Mensaje de error si el scraping falló
    /// </summary>
    public string? ErrorMessage { get; set; }
    
    /// <summary>
    /// URL utilizada para el scraping
    /// </summary>
    public string? UrlUsed { get; set; }
    
    /// <summary>
    /// Indica si el scraping fue exitoso
    /// </summary>
    public bool IsSuccess => string.IsNullOrEmpty(ErrorMessage) && 
                             (!string.IsNullOrEmpty(DepartureHtml) || !string.IsNullOrEmpty(ReturnHtml));

    /// <summary>
    /// Crea un resultado exitoso
    /// </summary>
    public static ScrapingResult Success(
        string? departureHtml, 
        string? returnHtml, 
        ScrapingMethod method,
        string urlUsed,
        TimeSpan duration)
    {
        return new ScrapingResult
        {
            DepartureHtml = departureHtml,
            ReturnHtml = returnHtml,
            UsedMethod = method,
            UrlUsed = urlUsed,
            Duration = duration
        };
    }

    /// <summary>
    /// Crea un resultado de fallo
    /// </summary>
    public static ScrapingResult Failure(string errorMessage, ScrapingMethod attemptedMethod, string? urlUsed = null)
    {
        return new ScrapingResult
        {
            ErrorMessage = errorMessage,
            UsedMethod = attemptedMethod,
            UrlUsed = urlUsed
        };
    }
}

/// <summary>
/// Métodos disponibles para realizar scraping
/// </summary>
public enum ScrapingMethod
{
    /// <summary>
    /// Método no determinado
    /// </summary>
    Unknown,
    
    /// <summary>
    /// Navegación directa por URL con parámetros
    /// </summary>
    DirectUrl,
    
    /// <summary>
    /// Llenado de formulario y submit
    /// </summary>
    FormFill
}
