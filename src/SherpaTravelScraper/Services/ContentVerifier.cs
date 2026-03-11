using Microsoft.Playwright;
using Microsoft.Extensions.Logging;

namespace SherpaTravelScraper.Services;

/// <summary>
/// Verifica si el contenido cargado en una página de Sherpa es válido y completo
/// </summary>
public class ContentVerifier
{
    private readonly IPage _page;
    private readonly ILogger<ContentVerifier> _logger;
    
    // Selectores para tabs (múltiples opciones por si cambian)
    private static readonly string[] DepartureTabSelectors = new[]
    {
        "[data-testid='departure-tab']",
        "button:has-text('Departure')",
        "button:has-text('Salida')",
        "[role='tab']:has-text('Departure')",
        "[role='tab']:has-text('Salida')",
        "button[id*='departure']",
        "button[id*='departure']"
    };
    
    private static readonly string[] ReturnTabSelectors = new[]
    {
        "[data-testid='return-tab']",
        "button:has-text('Return')",
        "button:has-text('Regreso')",
        "[role='tab']:has-text('Return')",
        "[role='tab']:has-text('Regreso')",
        "button[id*='return']"
    };
    
    // Selectores para contenido
    private static readonly string[] ContentSelectors = new[]
    {
        ".requirements-content",
        "[data-testid='requirements-content']",
        ".tab-content",
        "[data-testid='tab-content']",
        ".sherpa-requirements",
        ".requirement-content"
    };
    
    // Keywords que indican contenido válido
    private static readonly string[] ValidContentKeywords = new[]
    {
        "visa", "passport", "pasaporte", "document", "documento",
        "requirement", "requisito", "restriction", "restricción",
        "entry", "entrada", "arrival", "llegada"
    };

    public ContentVerifier(IPage page, ILogger<ContentVerifier> logger)
    {
        _page = page;
        _logger = logger;
    }
    
    /// <summary>
    /// Verifica si el contenido cargado es válido y completo
    /// </summary>
    /// <param name="minContentLength">Longitud mínima de contenido para considerarlo válido</param>
    /// <param name="cancellationToken">Token de cancelación</param>
    public async Task<ContentVerificationResult> VerifyAsync(
        int minContentLength = 500,
        CancellationToken cancellationToken = default)
    {
        var result = new ContentVerificationResult();
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        
        try
        {
            _logger.LogDebug("Iniciando verificación de contenido...");
            
            // Check 1: Verificar si los tabs están presentes
            result.HasDepartureTab = await IsAnyElementVisibleAsync(DepartureTabSelectors);
            
            _logger.LogDebug("Tab presente - Departure: {HasDeparture}", 
                result.HasDepartureTab);
            
            // Check 2: Obtener contenido del tab activo
            var activeTabContent = await GetActiveTabContentAsync();
            result.ActiveTabContentLength = activeTabContent?.Length ?? 0;
            result.HasSubstantialContent = result.ActiveTabContentLength >= minContentLength;
            
            _logger.LogDebug("Longitud de contenido activo: {Length} chars (mínimo requerido: {Min})",
                result.ActiveTabContentLength, minContentLength);
            
            // Check 3: Verificar si hay contenido relevante (keywords)
            if (!string.IsNullOrEmpty(activeTabContent))
            {
                var contentLower = activeTabContent.ToLowerInvariant();
                result.HasValidContentKeywords = ValidContentKeywords.Any(kw => 
                    contentLower.Contains(kw.ToLowerInvariant()));
            }
            
            // Check 4: Verificar secciones específicas
            result.HasVisaSection = await ContainsKeywordsAsync(new[] { "visa", "visado" });
            result.HasPassportSection = await ContainsKeywordsAsync(new[] { "passport", "pasaporte" });
            
            // Determinar éxito general
            // Consideramos exitoso si:
            // - El tab Departure está presente
            // - El contenido es sustancial
            // - Hay keywords válidas
            result.IsValid = result.HasDepartureTab 
                          && result.HasSubstantialContent
                          && result.HasValidContentKeywords;
            
            stopwatch.Stop();
            result.VerificationDurationMs = stopwatch.ElapsedMilliseconds;
            
            _logger.LogInformation(
                "Verificación completada en {Duration}ms - Válido: {IsValid}, " +
                "Departure: {Departure}, Content: {ContentLength} chars",
                result.VerificationDurationMs,
                result.IsValid,
                result.HasDepartureTab,
                result.ActiveTabContentLength);
            
            return result;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogError(ex, "Error durante verificación de contenido");
            
            result.IsValid = false;
            result.ErrorMessage = ex.Message;
            result.VerificationDurationMs = stopwatch.ElapsedMilliseconds;
            
            return result;
        }
    }
    
    /// <summary>
    /// Verifica rápidamente si hay contenido mínimo (para decisiones de fallback rápidas)
    /// </summary>
    public async Task<bool> HasMinimumContentAsync(int minLength = 300)
    {
        try
        {
            var content = await GetActiveTabContentAsync();
            return (content?.Length ?? 0) >= minLength;
        }
        catch
        {
            return false;
        }
    }
    
    /// <summary>
    /// Verifica si alguno de los selectores tiene un elemento visible
    /// </summary>
    private async Task<bool> IsAnyElementVisibleAsync(string[] selectors)
    {
        foreach (var selector in selectors)
        {
            try
            {
                var isVisible = await _page.IsVisibleAsync(selector, new PageIsVisibleOptions
                {
                    Timeout = 2000 // Timeout corto por selector
                });
                
                if (isVisible)
                {
                    _logger.LogTrace("Elemento visible encontrado con selector: {Selector}", selector);
                    return true;
                }
            }
            catch (TimeoutException)
            {
                // Ignorar timeout, probar siguiente selector
            }
            catch (Exception ex)
            {
                _logger.LogTrace("Error verificando selector {Selector}: {Error}", selector, ex.Message);
            }
        }
        
        return false;
    }
    
    /// <summary>
    /// Obtiene el contenido HTML/texto del tab activo
    /// </summary>
    private async Task<string?> GetActiveTabContentAsync()
    {
        foreach (var selector in ContentSelectors)
        {
            try
            {
                var element = await _page.QuerySelectorAsync(selector);
                if (element != null)
                {
                    // Intentar obtener innerText primero (más ligero)
                    var text = await element.InnerTextAsync();
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        return text;
                    }
                    
                    // Fallback a innerHTML
                    var html = await element.InnerHTMLAsync();
                    if (!string.IsNullOrWhiteSpace(html))
                    {
                        return html;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogTrace("Error obteniendo contenido con selector {Selector}: {Error}", 
                    selector, ex.Message);
            }
        }
        
        // Fallback: obtener todo el body
        try
        {
            return await _page.InnerTextAsync("body");
        }
        catch
        {
            return null;
        }
    }
    
    /// <summary>
    /// Verifica si la página contiene alguna de las keywords
    /// </summary>
    private async Task<bool> ContainsKeywordsAsync(string[] keywords)
    {
        try
        {
            var content = await GetActiveTabContentAsync();
            if (string.IsNullOrEmpty(content))
                return false;
                
            var contentLower = content.ToLowerInvariant();
            return keywords.Any(kw => contentLower.Contains(kw.ToLowerInvariant()));
        }
        catch
        {
            return false;
        }
    }
}

/// <summary>
/// Resultado de la verificación de contenido
/// </summary>
public class ContentVerificationResult
{
    /// <summary>
    /// Indica si el contenido es válido según todos los criterios
    /// </summary>
    public bool IsValid { get; set; }
    
    /// <summary>
    /// El tab Departure está presente
    /// </summary>
    public bool HasDepartureTab { get; set; }
    
    /// <summary>
    /// El tab Return está presente
    /// </summary>
    public bool HasReturnTab { get; set; }
    
    /// <summary>
    /// Longitud del contenido del tab activo
    /// </summary>
    public int ActiveTabContentLength { get; set; }
    
    /// <summary>
    /// El contenido tiene longitud sustancial (supera el mínimo)
    /// </summary>
    public bool HasSubstantialContent { get; set; }
    
    /// <summary>
    /// El contenido contiene keywords válidas (visa, passport, etc.)
    /// </summary>
    public bool HasValidContentKeywords { get; set; }
    
    /// <summary>
    /// Sección de visa está presente
    /// </summary>
    public bool HasVisaSection { get; set; }
    
    /// <summary>
    /// Sección de pasaporte está presente
    /// </summary>
    public bool HasPassportSection { get; set; }
    
    /// <summary>
    /// Mensaje de error si la verificación falló
    /// </summary>
    public string? ErrorMessage { get; set; }
    
    /// <summary>
    /// Duración de la verificación en milisegundos
    /// </summary>
    public long VerificationDurationMs { get; set; }
}
