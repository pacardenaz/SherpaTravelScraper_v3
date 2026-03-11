using System.Text.Json;
using SherpaTravelScraper.Models;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;
using SherpaTravelScraper.Utils;

namespace SherpaTravelScraper.Services;

/// <summary>
/// Servicio de scraping para el sitio de Sherpa
/// </summary>

public class SherpaScraperService : IAsyncDisposable
{
    private enum TabExtraccion
    {
        Departure,
        Return,
        Ambos
    }

    private readonly ILogger<SherpaScraperService> _logger;
    private readonly StealthConfig _stealthConfig;
    private readonly AiExtractionService? _aiService;
    private readonly IConfiguration _configuration;
    private IPlaywright? _playwright;
    private IBrowser? _browser;
    private bool _disposed;

    public SherpaScraperService(
        ILogger<SherpaScraperService> logger,
        StealthConfig stealthConfig,
        IConfiguration configuration,
        AiExtractionService? aiService = null)
    {
        _logger = logger;
        _stealthConfig = stealthConfig;
        _configuration = configuration;
        _aiService = aiService;
    }

    /// <summary>
    /// Inicializa Playwright y el browser
    /// </summary>
    public async Task InicializarAsync()
    {
        if (_playwright != null) return;

        _logger.LogInformation("Inicializando Playwright...");
        
        _playwright = await Playwright.CreateAsync();
        
        var headless = _configuration.GetValue<bool>("Playwright:Headless", true);
        
        _browser = await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = headless,
            Args = new[]
            {
                "--disable-blink-features=AutomationControlled",
                "--disable-web-security",
                "--disable-features=IsolateOrigins,site-per-process",
                "--disable-site-isolation-trials",
                "--disable-dev-shm-usage",
                "--no-sandbox",
                "--disable-setuid-sandbox",
                "--disable-accelerated-2d-canvas",
                "--disable-gpu",
                "--window-size=1920,1080",
                "--start-maximized"
            }
        });

        _logger.LogInformation("Playwright inicializado correctamente");
    }

    /// <summary>
    /// Navega a una URL con retry y backoff exponencial
    /// </summary>
    private async Task<IResponse?> NavegarConRetryAsync(IPage page, string url)
    {
        var maxRetries = _configuration.GetValue<int>("Scraping:MaxReintentos", 3);
        var navigationTimeout = _configuration.GetValue<int>("Scraping:NavigationTimeoutMs", 20000);
        
        for (int attempt = 1; attempt <= maxRetries; attempt++)
        {
            try
            {
                _logger.LogInformation("🌐 Navegando a URL (intento {Attempt}/{MaxRetries}): {Url}", 
                    attempt, maxRetries, url);
                
                var response = await page.GotoAsync(url, new PageGotoOptions
                {
                    WaitUntil = WaitUntilState.NetworkIdle,
                    Timeout = navigationTimeout
                });
                
                if (response != null)
                {
                    _logger.LogInformation("✅ Navegación exitosa - Status: {Status}", response.Status);
                    return response;
                }
                
                _logger.LogWarning("⚠️ Respuesta nula al navegar");
            }
            catch (TimeoutException ex)
            {
                _logger.LogWarning(ex, "⏱️ Timeout en intento {Attempt} de navegación", attempt);
                
                if (attempt < maxRetries)
                {
                    var backoffDelay = TimeSpan.FromSeconds(Math.Pow(2, attempt) + new Random().NextDouble());
                    _logger.LogInformation("⏳ Esperando {Delay:F1}s antes de reintentar...", backoffDelay.TotalSeconds);
                    await Task.Delay(backoffDelay);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error en intento {Attempt} de navegación", attempt);
                
                if (attempt < maxRetries)
                {
                    var backoffDelay = TimeSpan.FromSeconds(Math.Pow(2, attempt));
                    await Task.Delay(backoffDelay);
                }
            }
        }
        
        _logger.LogError("❌ Navegación fallida después de {MaxRetries} intentos", maxRetries);
        return null;
    }

    /// <summary>
    /// Realiza el scraping de requisitos para una combinación origen-destino
    /// </summary>
    public async Task<ResultadoScraping> ScrapearRequisitosAsync(
        string origenIso3,
        string destinoIso3,
        string idioma,
        DateTime fechaBase,
        string? tipoNacionalidad = null)
    {
        if (_browser == null)
            throw new InvalidOperationException("El servicio no ha sido inicializado. Llame a InicializarAsync primero.");

        // PASO 1: Construir URL base (sin parámetros de formulario)
        var urlBase = ConstruirUrlBase(destinoIso3);
        _logger.LogInformation("Scraping: {Origen} -> {Destino} (idioma: {Idioma})", origenIso3, destinoIso3, idioma);
        _logger.LogInformation("URL Base: {Url}", urlBase);

        var tabExtraccion = ResolverTabExtraccion(tipoNacionalidad);
        IPage? page = null;
        
        try
        {
            // Crear contexto con configuración stealth
            var contextOptions = _stealthConfig.GetStealthContextOptions();
            await using var context = await _browser.NewContextAsync(contextOptions);
            
            // Añadir headers adicionales
            await context.SetExtraHTTPHeadersAsync(new Dictionary<string, string>
            {
                ["Accept-Language"] = idioma.Replace("-", "_").Replace("EN", "en").Replace("ES", "es").Replace("PT", "pt"),
                ["Accept"] = "text/html,application/xhtml+xml,application/xml;q=0.9,image/webp,*/*;q=0.8",
                ["Accept-Encoding"] = "gzip, deflate, br",
                ["DNT"] = "1",
                ["Connection"] = "keep-alive",
                ["Upgrade-Insecure-Requests"] = "1"
            });

            page = await context.NewPageAsync();

            // Ejecutar scripts de stealth
            foreach (var script in StealthConfig.StealthScripts)
            {
                try
                {
                    await page.AddInitScriptAsync(script);
                }
                catch { /* Ignorar errores de scripts */ }
            }

            // PASO 2: Navegar a página con formulario
            var response = await NavegarConRetryAsync(page, urlBase);

            if (response == null)
                return ResultadoScraping.Fallo("No se pudo obtener respuesta del servidor después de múltiples intentos", urlBase);

            if (response.Status == 403)
                return ResultadoScraping.Fallo("BLOQUEO: Acceso prohibido (403)", urlBase);

            if (response.Status >= 400)
                return ResultadoScraping.Fallo($"Error HTTP {response.Status}", urlBase);

            // DEBUG: Información de diagnóstico
            _logger.LogInformation("=== DEBUG: Página cargada ===");
            _logger.LogInformation("URL final: {Url}", page.Url);
            _logger.LogInformation("Título: {Title}", await page.TitleAsync());
            
            // Delay aleatorio anti-detección
            await Task.Delay(_stealthConfig.GetRandomDelayMs() / 2);

            // PASO 3: Llenar formulario
            await LlenarFormularioSherpaAsync(page, origenIso3, destinoIso3, idioma, fechaBase);

            // PASO 4: Click en "See Requirements"
            _logger.LogInformation("🖱️ Click en 'See Requirements'...");
            var botonSelectores = new[] {
                "button:has-text('See requirements')",
                "button:has-text('See Requirements')",
                "button.w-full.large",
                "button[type='submit']",
                "button:has-text('Check')",
                "button:has-text('Submit')"
            };

            bool botonClickeado = false;
            foreach (var selector in botonSelectores)
            {
                try
                {
                    var elemento = await page.QuerySelectorAsync(selector);
                    if (elemento != null)
                    {
                        var isEnabled = await elemento.IsEnabledAsync();
                        if (isEnabled)
                        {
                            await elemento.ClickAsync();
                            _logger.LogInformation("✅ Botón clickeado: {Selector}", selector);
                            botonClickeado = true;
                            break;
                        }
                        else
                        {
                            _logger.LogWarning("⚠️ Botón encontrado pero deshabilitado: {Selector}", selector);
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogDebug("No se pudo hacer click con selector {Selector}: {Error}", selector, ex.Message);
                }
            }

            if (!botonClickeado)
            {
                _logger.LogWarning("⚠️ No se encontró botón de submit habilitado, intentando Enter en formulario");
                await page.Keyboard.PressAsync("Enter");
            }

            // PASO 5: Esperar a que cargue contenido real (tabs Departure/Return)
            await EsperarCargaContenidoRealAsync(page);

            // DEBUG: Información después de espera
            _logger.LogInformation("=== DEBUG: Después de espera de carga ===");
            await DiagnosticarPaginaAsync(page);

            // Screenshot para debugging (opcional)
            var debugScreenshots = _configuration.GetValue<bool>("Scraping:DebugScreenshots", false);
            if (debugScreenshots)
            {
                var screenshotPath = $"debug_{origenIso3}_{destinoIso3}_{DateTime.Now:yyyyMMdd_HHmmss}.png";
                await page.ScreenshotAsync(new PageScreenshotOptions 
                { 
                    Path = screenshotPath,
                    FullPage = true 
                });
                _logger.LogInformation("Screenshot guardado: {Path}", screenshotPath);
            }

            // Obtener HTML completo
            var htmlRaw = await page.ContentAsync();

            // Intentar extraer datos
            var resultado = await ExtraerDatosAsync(page, htmlRaw, urlBase, origenIso3, destinoIso3, idioma, tabExtraccion);
            
            // Delay post-request
            await Task.Delay(_stealthConfig.GetRandomDelayMs());

            return resultado;
        }
        catch (TimeoutException ex)
        {
            _logger.LogWarning(ex, "Timeout al esperar contenido");
            return ResultadoScraping.Fallo($"Timeout: {ex.Message}", urlBase);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error durante scraping");
            return ResultadoScraping.Fallo($"Error: {ex.Message}", urlBase);
        }
        finally
        {
            if (page != null)
            {
                try { await page.CloseAsync(); } catch { }
            }
        }
    }

    /// <summary>
    /// Construye la URL con parámetros dinámicos
    /// </summary>
    [Obsolete("Usar ConstruirUrlBase en su lugar para el nuevo flujo de formulario")]
    private string ConstruirUrl(string origenIso3, string destinoIso3, string idioma, DateTime fechaBase)
    {
        var departureDate = fechaBase.AddDays(15).ToString("yyyy-MM-dd");
        var returnDate = fechaBase.AddDays(22).ToString("yyyy-MM-dd");

        var queryParams = new Dictionary<string, string>
        {
            ["language"] = idioma,
            ["nationality"] = origenIso3,
            ["originCountry"] = origenIso3,
            ["departureDate"] = departureDate,
            ["returnDate"] = returnDate,
            ["travelPurposes"] = "TOURISM",
            ["tripType"] = "roundTrip",
            ["fullyVaccinated"] = "true",
            ["affiliateId"] = "sherpa"
        };

        var queryString = string.Join("&", queryParams.Select(p => $"{p.Key}={Uri.EscapeDataString(p.Value)}"));
        return $"https://apply.joinsherpa.com/travel-restrictions/{destinoIso3}?{queryString}";
    }

    /// <summary>
    /// Construye la URL base con solo el destino (Sherpa carga el resto por formulario)
    /// </summary>
    private string ConstruirUrlBase(string destinoIso3)
    {
        // URL mínima - Sherpa carga formulario con destino pre-seleccionado
        return $"https://apply.joinsherpa.com/travel-restrictions/{destinoIso3}";
    }

    /// <summary>
    /// Llena el formulario de Sherpa con los datos del viaje
    /// </summary>
    private async Task LlenarFormularioSherpaAsync(
        IPage page, 
        string origenIso3, 
        string destinoIso3, 
        string idioma, 
        DateTime fechaBase)
    {
        _logger.LogInformation("📝 Llenando formulario de Sherpa...");

        try
        {
            // 0) Cerrar banner de cookies si aparece (no bloqueante)
            try
            {
                var cookieBtn = page.Locator("button:has-text('Accept all cookies')").First;
                if (await cookieBtn.IsVisibleAsync(new() { Timeout = 1500 }))
                {
                    await cookieBtn.ClickAsync(new() { Timeout = 2000 });
                    await page.WaitForTimeoutAsync(300);
                    _logger.LogInformation("✅ Cookies aceptadas");
                }
            }
            catch { /* no-op */ }

            // 1) Abrir formulario solo si no está visible
            var whereFrom = page.Locator("button:has-text('Where from')").First;
            var formVisible = false;
            try
            {
                formVisible = await whereFrom.IsVisibleAsync(new() { Timeout = 1200 });
            }
            catch { formVisible = false; }

            if (!formVisible)
            {
                try
                {
                    var changeBtn = page.Locator("button:has-text('Change')").First;
                    if (await changeBtn.IsVisibleAsync(new() { Timeout = 1500 }))
                    {
                        await changeBtn.ClickAsync(new() { Timeout = 2000 });
                        await page.WaitForTimeoutAsync(400);
                        _logger.LogInformation("✅ Botón 'Change' clickeado");
                    }
                }
                catch
                {
                    _logger.LogDebug("'Change' no disponible, continuando...");
                }
            }

            // 2) Seleccionar origen
            var paisOrigen = ObtenerNombrePaisDesdeIso3(origenIso3);
            await SeleccionarPaisEnCampoAsync(page, "Where from", paisOrigen);
            _logger.LogInformation("✅ Origen seleccionado: {Origen} ({Nombre})", origenIso3, paisOrigen);

            // 3) Seleccionar destino
            var paisDestino = ObtenerNombrePaisDesdeIso3(destinoIso3);
            await SeleccionarPaisEnCampoAsync(page, "Where to", paisDestino);
            _logger.LogInformation("✅ Destino seleccionado: {Destino} ({Nombre})", destinoIso3, paisDestino);

            // 4) Fechas
            var departureDate = fechaBase.AddDays(15).ToString("yyyy-MM-dd");
            var returnDate = fechaBase.AddDays(22).ToString("yyyy-MM-dd");

            try
            {
                var depInput = page.Locator("input[placeholder='Departure'], input[aria-label*='Departure']").First;
                if (await depInput.IsVisibleAsync(new() { Timeout = 1500 }))
                {
                    await depInput.FillAsync(departureDate, new() { Timeout = 2000 });
                    _logger.LogInformation("✅ Fecha salida: {Fecha}", departureDate);
                }
            }
            catch { /* opcional */ }

            try
            {
                var retInput = page.Locator("input[placeholder='Return'], input[aria-label*='Return']").First;
                if (await retInput.IsVisibleAsync(new() { Timeout = 1500 }))
                {
                    await retInput.FillAsync(returnDate, new() { Timeout = 2000 });
                    _logger.LogInformation("✅ Fecha retorno: {Fecha}", returnDate);
                }
            }
            catch { /* opcional */ }

            // 5) Verificar propósito/tipo viaje (opcional, no bloqueante)
            _logger.LogInformation("✅ Formulario llenado correctamente");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Error llenando formulario");
            // No lanzar excepción, intentar continuar
        }
    }

    private async Task SeleccionarPaisEnCampoAsync(IPage page, string fieldLabel, string countryName)
    {
        // Abrir selector
        var btn = page.Locator($"button:has-text('{fieldLabel}')").First;
        await btn.ClickAsync(new() { Timeout = 4000 });
        await page.WaitForTimeoutAsync(300);

        // Buscar input activo de overlay
        var input = page.Locator(".cdk-overlay-pane input, input.mat-mdc-input-element, input[placeholder*='Search']").First;
        await input.FillAsync(countryName, new() { Timeout = 3000 });
        await page.WaitForTimeoutAsync(350);

        // Seleccionar opción (intentos)
        var options = new[]
        {
            $"[role='option']:has-text('{countryName}')",
            $".mat-mdc-option:has-text('{countryName}')",
            $".mat-mdc-list-item:has-text('{countryName}')",
            ".mat-mdc-option:first-child",
            ".mat-mdc-list-item:first-child"
        };

        foreach (var selector in options)
        {
            try
            {
                var opt = page.Locator(selector).First;
                if (await opt.IsVisibleAsync(new() { Timeout = 1200 }))
                {
                    await opt.ClickAsync(new() { Timeout = 2000 });
                    await page.WaitForTimeoutAsync(300);
                    return;
                }
            }
            catch { }
        }

        // fallback teclado
        await page.Keyboard.PressAsync("ArrowDown");
        await page.Keyboard.PressAsync("Enter");
        await page.WaitForTimeoutAsync(300);
    }

    /// <summary>
    /// Espera a que el contenido real de requisitos esté cargado (tabs Departure/Return visibles)
    /// </summary>
    private async Task EsperarCargaContenidoRealAsync(IPage page)
    {
        _logger.LogInformation("⏳ Esperando carga de contenido real...");
        
        var timeout = _configuration.GetValue<int>("Scraping:WaitForContentTimeoutMs", 60000); // 60 segundos
        
        try
        {
            // ESPERA 1: Tabs de Departure/Return
            var tabSelectors = new[] {
                "nav.tabs li.mat-h5.active",
                "nav.tabs li:has-text('Departure')",
                "[role='tab']:has-text('Departure')",
                ".tabs li.active",
                "nav.tabs"
            };
            
            bool tabsEncontrados = false;
            foreach (var selector in tabSelectors)
            {
                try
                {
                    await page.WaitForSelectorAsync(selector, new PageWaitForSelectorOptions 
                    { 
                        Timeout = timeout,
                        State = WaitForSelectorState.Visible
                    });
                    _logger.LogInformation("✅ Tabs encontrados: {Selector}", selector);
                    tabsEncontrados = true;
                    break;
                }
                catch { continue; }
            }
            
            if (!tabsEncontrados)
            {
                _logger.LogWarning("⚠️ No se encontraron tabs de Departure/Return");
            }
            
            // ESPERA 2: Contenido de requisitos (no vacío)
            await Task.Delay(3000); // Esperar a que renderice contenido
            
            var contentSelectors = new[] {
                ".restrictions-result__wrapper",
                ".restrictions-result__wrapper > div",
                "[class*='requirement']",
                "[class*='Requirement']",
                "section:has-text('Visa')",
                "article"
            };
            
            bool contenidoEncontrado = false;
            foreach (var selector in contentSelectors)
            {
                try
                {
                    var element = await page.QuerySelectorAsync(selector);
                    if (element != null)
                    {
                        var text = await element.TextContentAsync();
                        if (!string.IsNullOrWhiteSpace(text) && text.Length > 100)
                        {
                            _logger.LogInformation("✅ Contenido encontrado: {Selector} ({Length} chars)", 
                                selector, text.Length);
                            contenidoEncontrado = true;
                            break;
                        }
                    }
                }
                catch { continue; }
            }
            
            if (!contenidoEncontrado)
            {
                _logger.LogWarning("⚠️ No se encontró contenido de requisitos sustancial");
            }
            
            _logger.LogInformation("✅ Página lista para extracción");
        }
        catch (TimeoutException)
        {
            _logger.LogWarning("⏱️ Timeout esperando contenido real, continuando de todos modos...");
        }
    }

    /// <summary>
    /// Obtiene el nombre del país desde su código ISO3
    /// </summary>
    private string ObtenerNombrePaisDesdeIso3(string iso3)
    {
        // Mapeo básico de ISO3 a nombres de países en inglés
        var paises = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["ARG"] = "Argentina",
            ["BRA"] = "Brazil",
            ["CAN"] = "Canada",
            ["CHL"] = "Chile",
            ["COL"] = "Colombia",
            ["USA"] = "United States",
            ["MEX"] = "Mexico",
            ["GBR"] = "United Kingdom",
            ["FRA"] = "France",
            ["DEU"] = "Germany",
            ["ITA"] = "Italy",
            ["ESP"] = "Spain",
            ["AUS"] = "Australia",
            ["JPN"] = "Japan",
            ["CHN"] = "China",
            ["IND"] = "India"
        };

        return paises.TryGetValue(iso3, out var nombre) ? nombre : iso3;
    }

    /// <summary>
    /// Espera a que el contenido de la página esté cargado con retry y backoff
    /// </summary>
    private async Task EsperarCargaContenidoAsync(IPage page)
    {
        var timeout = _configuration.GetValue<int>("Scraping:WaitForSelectorTimeoutMs", 30000);
        var maxRetries = 3;
        
        // Selectores por prioridad (los más específicos primero)
        var selectors = new (string selector, int priority)[]
        {
            ("[data-testid='requirements-container']", 1),
            ("[data-testid='visa-requirements']", 1),
            ("[data-testid='passport-requirements']", 1),
            ("[data-testid='health-requirements']", 1),
            ("[data-testid='departure-tab']", 2),
            ("[data-testid='return-tab']", 2),
            (".requirements-section", 3),
            ("article[data-testid*='requirement']", 3),
            ("[class*='RequirementCard']", 4),
            ("[class*='requirement-card']", 4),
            ("[class*='Requirement']", 5),
            ("[class*='requirement']", 5),
            ("main", 6)
        };

        // Intentar con retry
        for (int attempt = 1; attempt <= maxRetries; attempt++)
        {
            try
            {
                _logger.LogDebug("Esperando contenido - Intento {Attempt}/{MaxRetries}", attempt, maxRetries);
                
                // Primero esperar que la red esté quieta
                await page.WaitForLoadStateAsync(LoadState.NetworkIdle, new PageWaitForLoadStateOptions 
                { 
                    Timeout = timeout 
                });
                
                // Luego buscar selectores específicos
                foreach (var (selector, priority) in selectors)
                {
                    try
                    {
                        var element = await page.WaitForSelectorAsync(selector, new PageWaitForSelectorOptions
                        {
                            Timeout = timeout / maxRetries,
                            State = WaitForSelectorState.Visible
                        });
                        
                        if (element != null)
                        {
                            _logger.LogInformation("✅ Selector encontrado: {Selector} (prioridad: {Priority})", 
                                selector, priority);
                            
                            // Esperar un poco más para que el contenido dinámico se cargue
                            await Task.Delay(2000);
                            return;
                        }
                    }
                    catch { /* Intentar siguiente selector */ }
                }
                
                // Si llegamos aquí, no se encontró ningún selector específico
                // Esperar a que el DOM esté estable
                await page.WaitForLoadStateAsync(LoadState.DOMContentLoaded);
                
                // Verificar si hay contenido en el body
                var bodyText = await page.EvaluateAsync<string>("() => document.body?.innerText || ''");
                if (!string.IsNullOrWhiteSpace(bodyText) && bodyText.Length > 500)
                {
                    _logger.LogInformation("✅ Contenido detectado en body ({Length} chars)", bodyText.Length);
                    await Task.Delay(2000);
                    return;
                }
                
                // Si no hay contenido suficiente, esperar y reintentar
                if (attempt < maxRetries)
                {
                    var backoffDelay = TimeSpan.FromSeconds(Math.Pow(2, attempt)); // 2s, 4s, 8s
                    _logger.LogWarning("⚠️ Contenido no detectado, esperando {Delay}s antes de reintentar...", 
                        backoffDelay.TotalSeconds);
                    await Task.Delay(backoffDelay);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error en intento {Attempt} de espera de contenido", attempt);
                
                if (attempt < maxRetries)
                {
                    var backoffDelay = TimeSpan.FromSeconds(Math.Pow(2, attempt));
                    await Task.Delay(backoffDelay);
                }
            }
        }
        
        _logger.LogWarning("⚠️ No se pudo detectar contenido específico después de {MaxRetries} intentos", maxRetries);
    }

    /// <summary>
    /// Extrae los datos de la página
    /// </summary>
    /// <summary>
    /// Extrae datos usando el método configurado (javascript, ia-vision, ia-html)
    /// </summary>
    private async Task<ResultadoScraping> ExtraerDatosAsync(IPage page, string htmlRaw, string url, string origen, string destino, string idioma, TabExtraccion tabExtraccion)
    {
        // Obtener el método de extracción configurado
        var extractionMethod = _aiService?.GetExtractionMethod() ?? "javascript";
        _logger.LogInformation("Método de extracción configurado: {Metodo}", extractionMethod);

        // ESTRATEGIA 1: JavaScript tradicional
        if (extractionMethod == "javascript" || extractionMethod == "js")
        {
            _logger.LogInformation("Usando extracción JavaScript tradicional...");
            return await ExtraerDatosTradicionalesAsync(page, htmlRaw, url);
        }

        // ESTRATEGIA 2 y 3: IA (visión o HTML)
        if (_aiService != null && _configuration.GetValue<bool>("AI:Enabled", true))
        {
            try
            {
                RequisitosViajeCompleto? extraccion = null;

                if (extractionMethod == "ia-vision" || extractionMethod == "vision")
                {
                    // MÉTODO IA VISIÓN: Requiere screenshots
                    _logger.LogInformation("Usando extracción IA Visión (con screenshots)...");
                    extraccion = await ExtraerConIaVisionAsync(page, htmlRaw, origen, destino, idioma, tabExtraccion);
                }
                else if (extractionMethod == "ia-html" || extractionMethod == "html")
                {
                    // MÉTODO IA HTML: Solo requiere HTML
                    _logger.LogInformation("Usando extracción IA HTML (solo texto)...");
                    
                    // Determinar qué método usar según el proveedor configurado
                    var provider = _configuration["Extraction:IaHtml:Provider"]?.ToLower() ?? "openrouter";
                    
                    if (provider == "openrouter")
                    {
                        extraccion = await _aiService.ExtraerConOpenRouterHtmlAsync(await ObtenerHtmlSegunTabAsync(page, htmlRaw, tabExtraccion), origen, destino, idioma);
                    }
                    else
                    {
                        extraccion = await _aiService.ExtraerConKimiHtmlAsync(await ObtenerHtmlSegunTabAsync(page, htmlRaw, tabExtraccion), origen, destino, idioma);
                    }
                }

                if (extraccion != null)
                {
                    // Serializar el objeto completo a JSON para guardar en la BD
                    var jsonDatos = System.Text.Json.JsonSerializer.Serialize(extraccion, new System.Text.Json.JsonSerializerOptions
                    {
                        WriteIndented = true,
                        PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase
                    });
                    
                    _logger.LogInformation("✅ Extracción IA exitosa - Método: {Metodo}, JSON: {JsonLength} chars, Confianza: {Confianza:F2}", 
                        extractionMethod, jsonDatos.Length, extraccion.Confianza);
                    
                    return ResultadoScraping.Exito(
                        datos: jsonDatos,
                        url: url,
                        htmlRaw: htmlRaw,
                        markdown: extraccion.Markdown,
                        tabsExtraidas: tabExtraccion.ToString()
                    );
                }

                _logger.LogWarning("⚠️ IA devolvió resultado vacío, usando fallback tradicional...");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error en extracción IA ({Metodo}), usando fallback tradicional", extractionMethod);
            }
        }

        // FALLBACK: Métodos tradicionales
        _logger.LogInformation("Usando extracción tradicional (fallback)...");
        return await ExtraerDatosTradicionalesAsync(page, htmlRaw, url);
    }

    /// <summary>
    /// Extrae datos usando IA con screenshots (método ia-vision)
    /// </summary>
    private async Task<RequisitosViajeCompleto?> ExtraerConIaVisionAsync(IPage page, string htmlRaw, string origen, string destino, string idioma, TabExtraccion tabExtraccion)
    {
        _logger.LogInformation("Capturando screenshots para IA Visión. Tab objetivo: {Tab}", tabExtraccion);

        var screenshots = new List<byte[]>();

        if (tabExtraccion == TabExtraccion.Departure || tabExtraccion == TabExtraccion.Ambos)
        {
            await ActivarTabAsync(page, "Departure");
            screenshots.Add(await page.ScreenshotAsync(new PageScreenshotOptions { Type = ScreenshotType.Png, FullPage = false }));
        }

        if (tabExtraccion == TabExtraccion.Return || tabExtraccion == TabExtraccion.Ambos)
        {
            await ActivarTabAsync(page, "Return");
            screenshots.Add(await page.ScreenshotAsync(new PageScreenshotOptions { Type = ScreenshotType.Png, FullPage = false }));
        }

        return await _aiService!.ExtraerRequisitosCompletosAsync(
            htmlRaw,
            screenshots.ToArray(),
            origen,
            destino,
            idioma);
    }

    private async Task<ResultadoScraping> ExtraerDatosTradicionalesAsync(IPage page, string htmlRaw, string url)
    {
        // Estrategia mejorada: Extraer datos estructurados usando JavaScript
        var datosEstructurados = await ExtraerDatosEstructuradosConJSAsync(page, url);
        
        if (datosEstructurados != null)
        {
            return datosEstructurados;
        }
        
        // Fallback a estrategias anteriores
        var jsonData = await ExtraerJsonDeScriptsAsync(page);
        
        var secciones = await ExtraerSeccionesEstructuradasAsync(page);
        
        if (secciones != null && secciones.Any(s => !string.IsNullOrEmpty(s.Contenido)))
        {
            var contenidoFormateado = string.Join("\n\n---\n\n", 
                secciones.Select(s => $"[{s.Titulo}]\n{s.Contenido}"));
                
            return ResultadoScraping.Exito(
                contenidoFormateado, 
                url, 
                htmlRaw,
                requisitosDestino: contenidoFormateado,
                requisitosVisado: secciones.FirstOrDefault(s => s.Tipo == "visa")?.Contenido,
                pasaportes: secciones.FirstOrDefault(s => s.Tipo == "passport")?.Contenido,
                sanitarios: secciones.FirstOrDefault(s => s.Tipo == "health")?.Contenido
            );
        }

        var contenido = await ExtraerContenidoSelectoresAsync(page);
        if (!string.IsNullOrEmpty(contenido))
        {
            _logger.LogDebug("Datos extraídos vía selectores CSS");
            return ResultadoScraping.Exito(contenido, url, htmlRaw,
                requisitosDestino: contenido,
                requisitosVisado: await ExtraerTextoSelectorAsync(page, ".visa-requirements, [class*='visa'], [data-testid*='visa']"),
                pasaportes: await ExtraerTextoSelectorAsync(page, ".passport-requirements, [class*='passport'], [data-testid*='passport']"),
                sanitarios: await ExtraerTextoSelectorAsync(page, ".health-requirements, [class*='health'], [class*='covid'], [data-testid*='health']")
            );
        }

        return ResultadoScraping.Fallo("No se pudieron extraer datos de la página", url, htmlRaw);
    }
    
    /// <summary>
    /// Extrae datos estructurados usando JavaScript específico para Sherpa
    /// </summary>
    private async Task<ResultadoScraping?> ExtraerDatosEstructuradosConJSAsync(IPage page, string url)
    {
        try
        {
            _logger.LogInformation("Intentando extracción estructurada con JavaScript...");
            
            var script = @"
                () => {
                    const data = {
                        visa: { requerido: null, tipo: null, descripcion: null },
                        pasaporte: { validez: null, descripcion: null },
                        salud: { vacunas: [], covid: null, seguro: null },
                        advertencias: [],
                        contenidoCompleto: []
                    };
                    
                    // PRIMERO: Intentar extraer del contenedor principal de Sherpa
                    const mainContainer = document.querySelector('.restrictions-result__wrapper');
                    if (mainContainer) {
                        const mainText = mainContainer.textContent?.trim() || '';
                        if (mainText.length > 100) {
                            data.contenidoCompleto.push(mainText.substring(0, 5000));
                        }
                    }
                    
                    // Buscar información de visa
                    document.querySelectorAll('[data-testid*=""visa""], [class*=""visa""], [class*=""Visa""], .restrictions-result__wrapper').forEach(el => {
                        const text = el.textContent ? el.textContent.trim() : '';
                        if (text) {
                            if (!data.contenidoCompleto.includes(text) && text.length > 20) {
                                data.contenidoCompleto.push(text.substring(0, 2000));
                            }
                            if (text.toLowerCase().includes('visa') || text.toLowerCase().includes('visado')) {
                                data.visa.descripcion = text.substring(0, 1000);
                                data.visa.requerido = !text.toLowerCase().includes('not required') && 
                                                      !text.toLowerCase().includes('no requiere') &&
                                                      !text.toLowerCase().includes('exempt');
                                if (text.toLowerCase().includes('tourist')) data.visa.tipo = 'tourist';
                                else if (text.toLowerCase().includes('business')) data.visa.tipo = 'business';
                            }
                        }
                    });
                    
                    // Buscar información de pasaporte
                    document.querySelectorAll('[data-testid*=""passport""], [class*=""passport""], [class*=""Passport""], .restrictions-result__wrapper').forEach(el => {
                        const text = el.textContent ? el.textContent.trim() : '';
                        if (text && text.toLowerCase().includes('passport')) {
                            data.pasaporte.descripcion = text.substring(0, 1000);
                            const validezMatch = text.match(/(\d+)\s*month/i);
                            if (validezMatch) data.pasaporte.validez = validezMatch[1] + ' months';
                        }
                    });
                    
                    // Buscar información de salud
                    document.querySelectorAll('[data-testid*=""health""], [class*=""health""], [class*=""covid""], .restrictions-result__wrapper').forEach(el => {
                        const text = el.textContent ? el.textContent.trim() : '';
                        if (text) {
                            if (text.toLowerCase().includes('vaccin')) data.salud.vacunas.push(text.substring(0, 500));
                            if (text.toLowerCase().includes('covid')) data.salud.covid = text.substring(0, 500);
                        }
                    });
                    
                    // Extraer todos los encabezados relevantes
                    document.querySelectorAll('h1, h2, h3, h4, section article').forEach(el => {
                        const text = el.textContent ? el.textContent.trim() : '';
                        if (text && text.length > 20 && text.length < 2000 && !data.contenidoCompleto.includes(text)) {
                            data.contenidoCompleto.push(text);
                        }
                    });
                    
                    return JSON.stringify(data);
                }
            ";
            
            var resultado = await page.EvaluateAsync<string>(script);
            
            if (!string.IsNullOrEmpty(resultado))
            {
                _logger.LogInformation("Datos estructurados extraídos con JavaScript");
                
                var datos = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(resultado);
                
                var contenidoCompleto = datos.GetProperty("contenidoCompleto").EnumerateArray()
                    .Select(x => x.GetString())
                    .Where(x => !string.IsNullOrEmpty(x))
                    .ToList();
                
                var requisitosDestino = string.Join("\n\n", contenidoCompleto.Take(10));
                
                string? visaDesc = null;
                string? pasaporteDesc = null;
                
                try { visaDesc = datos.GetProperty("visa").GetProperty("descripcion").GetString(); } catch { }
                try { pasaporteDesc = datos.GetProperty("pasaporte").GetProperty("descripcion").GetString(); } catch { }
                
                var saludVacunas = datos.GetProperty("salud").GetProperty("vacunas").EnumerateArray()
                    .Select(x => x.GetString())
                    .Where(x => !string.IsNullOrEmpty(x));
                
                var sanitarios = string.Join("\n", saludVacunas);
                
                var jsonEstructurado = new
                {
                    infoViaje = new { origen = "", destino = "" },
                    departure = new
                    {
                        visa = new { descripcion = visaDesc },
                        pasaporte = new { descripcion = pasaporteDesc },
                        salud = new { vacunas = saludVacunas.ToList() }
                    },
                    confianza = 0.5,
                    extraidoCon = "javascript_tradicional"
                };
                
                var jsonString = System.Text.Json.JsonSerializer.Serialize(jsonEstructurado, new System.Text.Json.JsonSerializerOptions
                {
                    WriteIndented = true
                });
                
                return ResultadoScraping.Exito(
                    datos: jsonString,
                    url: url,
                    htmlRaw: null,
                    requisitosDestino: requisitosDestino,
                    requisitosVisado: visaDesc,
                    pasaportes: pasaporteDesc,
                    sanitarios: sanitarios,
                    markdown: requisitosDestino
                );
            }
            
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error en extracción estructurada con JS");
            return null;
        }
    }

    /// <summary>
    /// Extrae las secciones estructuradas de requisitos
    /// </summary>
    private async Task<List<SeccionRequisito>> ExtraerSeccionesEstructuradasAsync(IPage page)
    {
        var secciones = new List<SeccionRequisito>();
        
        _logger.LogInformation("=== DEBUG: Iniciando ExtraerSeccionesEstructuradasAsync ===");
        
        try
        {
            // Estrategia simple: buscar elementos por selectores y extraer texto
            var selectores = new Dictionary<string, string[]>
            {
                ["visa"] = new[] { ".restrictions-result__wrapper", "[data-testid*='visa']", "[class*='visa']", "[id*='visa']" },
                ["passport"] = new[] { ".restrictions-result__wrapper", "[data-testid*='passport']", "[class*='passport']", "[id*='passport']" },
                ["health"] = new[] { ".restrictions-result__wrapper", "[data-testid*='health']", "[class*='health']", "[id*='health']" }
            };

            foreach (var categoria in selectores)
            {
                try
                {
                    foreach (var selector in categoria.Value)
                    {
                        var elementos = await page.QuerySelectorAllAsync(selector);
                        if (elementos != null && elementos.Count > 0)
                        {
                            _logger.LogInformation("Selector '{Selector}' encontró {Count} elementos", selector, elementos.Count);
                            
                            foreach (var el in elementos)
                            {
                                if (el == null) continue;
                                
                                try
                                {
                                    var texto = await el.TextContentAsync();
                                    if (!string.IsNullOrWhiteSpace(texto) && texto.Length > 50)
                                    {
                                        var tituloEl = await el.QuerySelectorAsync("h1, h2, h3, h4, [class*='title']");
                                        var titulo = tituloEl != null ? await tituloEl.TextContentAsync() : categoria.Key;
                                        
                                        secciones.Add(new SeccionRequisito
                                        {
                                            Tipo = categoria.Key,
                                            Titulo = titulo ?? categoria.Key,
                                            Contenido = texto.Trim()
                                        });
                                        
                                        _logger.LogInformation("Sección {Tipo} agregada con {Length} caracteres", 
                                            categoria.Key, texto.Length);
                                        break; // Solo tomar el primer elemento de esta categoría
                                    }
                                }
                                catch (Exception exEl)
                                {
                                    _logger.LogDebug(exEl, "Error procesando elemento");
                                }
                            }
                            
                            if (secciones.Any(s => s.Tipo == categoria.Key))
                                break; // Ya encontramos esta categoría
                        }
                    }
                }
                catch (Exception exCat)
                {
                    _logger.LogDebug(exCat, "Error en categoría {Categoria}", categoria.Key);
                }
            }

            // Si no encontramos nada específico, intentar extraer todo el texto del main
            if (secciones.Count == 0)
            {
                _logger.LogInformation("No se encontraron secciones específicas, intentando extraer texto general...");
                
                try
                {
                    var mainContent = await page.QuerySelectorAsync("main, [role='main'], body");
                    if (mainContent != null)
                    {
                        var texto = await mainContent.TextContentAsync();
                        if (!string.IsNullOrWhiteSpace(texto) && texto.Length > 100)
                        {
                            var textoTrimmed = texto.Trim();
                            var longitudMaxima = Math.Min(5000, textoTrimmed.Length);
                            secciones.Add(new SeccionRequisito
                            {
                                Tipo = "general",
                                Titulo = "Contenido General",
                                Contenido = textoTrimmed.Substring(0, longitudMaxima)
                            });
                            _logger.LogInformation("Texto general extraído: {Length} caracteres", textoTrimmed.Length);
                        }
                    }
                }
                catch (Exception exGeneral)
                {
                    _logger.LogWarning(exGeneral, "Error extrayendo texto general");
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error en ExtraerSeccionesEstructuradasAsync");
        }

        _logger.LogInformation("=== DEBUG: ExtraerSeccionesEstructuradasAsync completado. Secciones: {Count} ===", secciones.Count);
        
        return secciones;
    }

    /// <summary>
    /// Extrae secciones buscando por texto de encabezados conocidos
    /// </summary>
    private async Task<List<SeccionRequisito>> ExtraerSeccionesPorTextoAsync(IPage page)
    {
        var secciones = new List<SeccionRequisito>();
        
        var patrones = new Dictionary<string, string[]>
        {
            ["visa"] = new[] { "visa", "visado", "visa requirements", "travel visa", "do i need a visa", "visa exemption" },
            ["passport"] = new[] { "passport", "pasaporte", "passport and documents", "document requirements", "passport validity" },
            ["health"] = new[] { "health", "salud", "covid", "vaccination", "medical", "health requirements", "travel health" }
        };

        foreach (var patron in patrones)
        {
            try
            {
                // Construir condiciones de búsqueda
                var condiciones = string.Join(" || ", 
                    patron.Value.Select(p => $"h.innerText.toLowerCase().includes('{p}')"));
                
                // Buscar elementos que contengan el texto del patrón
                var contenido = await page.EvaluateAsync<string>($@"
                    () => {{
                        const headers = Array.from(document.querySelectorAll('h1, h2, h3, h4, h5, h6, button[role=""tab""], div[role=""button""]'));
                        const header = headers.find(h => {condiciones});
                        
                        if (header) {{
                            // Intentar encontrar el panel/contenido asociado
                            const parent = header.closest('section, article, div[class*=""card""], div[class*=""panel""]');
                            if (parent) {{
                                const textos = Array.from(parent.querySelectorAll('p, li, div:not(:has(div))'))
                                    .map(el => el.innerText.trim())
                                    .filter(text => text.length > 10 && text.length < 1000);
                                return textos.join('\n');
                            }}
                            return header.parentElement?.innerText.trim() || header.innerText.trim();
                        }}
                        return null;
                    }}
                ");

                if (!string.IsNullOrWhiteSpace(contenido) && contenido.Length > 50)
                {
                    secciones.Add(new SeccionRequisito
                    {
                        Tipo = patron.Key,
                        Titulo = patron.Key.ToUpperInvariant(),
                        Contenido = contenido
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Error al buscar sección por texto: {Patron}", patron.Key);
            }
        }

        return secciones;
    }

    /// <summary>
    /// Intenta extraer JSON de los scripts de la página
    /// </summary>
    private async Task<string?> ExtraerJsonDeScriptsAsync(IPage page)
    {
        try
        {
            var scripts = await page.QuerySelectorAllAsync("script");
            
            foreach (var script in scripts)
            {
                try
                {
                    var content = await script.TextContentAsync();
                    if (string.IsNullOrEmpty(content)) continue;

                    // Buscar __INITIAL_STATE__
                    var initialStateMatch = Regex.Match(content, @"window\.__INITIAL_STATE__\s*=\s*(\{.*?\});", RegexOptions.Singleline);
                    if (initialStateMatch.Success)
                        return initialStateMatch.Groups[1].Value;

                    // Buscar travelRestrictions
                    var restrictionsMatch = Regex.Match(content, @"travelRestrictions[""']?\s*:\s*(\{.*?\})(?:,|;|\})", RegexOptions.Singleline);
                    if (restrictionsMatch.Success)
                        return restrictionsMatch.Groups[1].Value;
                }
                catch { /* Ignorar errores individuales */ }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error al extraer JSON de scripts");
        }

        return null;
    }

    /// <summary>
    /// Extrae contenido usando selectores CSS
    /// </summary>
    private async Task<string?> ExtraerContenidoSelectoresAsync(IPage page)
    {
        var selectores = new[]
        {
            ".restrictions-result__wrapper",           // Nuevo selector verificado
            ".restrictions-result__wrapper > div",    // Contenido interno
            "[data-testid='requirements-container']",
            ".travel-requirements",
            "main article",
            ".requirements-content",
            "#main-content",
            "main"
        };

        foreach (var selector in selectores)
        {
            try
            {
                var element = await page.QuerySelectorAsync(selector);
                if (element != null)
                {
                    var text = await element.TextContentAsync();
                    if (!string.IsNullOrWhiteSpace(text) && text.Length > 100)
                        return text.Trim();
                }
            }
            catch { /* Intentar siguiente selector */ }
        }

        return null;
    }

    /// <summary>
    /// Extrae texto de un selector específico
    /// </summary>
    private async Task<string?> ExtraerTextoSelectorAsync(IPage page, string selector)
    {
        try
        {
            var selectors = selector.Split(',').Select(s => s.Trim());
            foreach (var sel in selectors)
            {
                var element = await page.QuerySelectorAsync(sel);
                if (element != null)
                {
                    var text = await element.TextContentAsync();
                    if (!string.IsNullOrWhiteSpace(text))
                        return text.Trim();
                }
            }
        }
        catch { }

        return null;
    }

    /// <summary>
    /// Método de diagnóstico para debuggear la página
    /// </summary>
    private async Task DiagnosticarPaginaAsync(IPage page)
    {
        try
        {
            // Contar elementos principales
            var divCount = await page.EvaluateAsync<int>("() => document.querySelectorAll('div').length");
            var sectionCount = await page.EvaluateAsync<int>("() => document.querySelectorAll('section').length");
            var articleCount = await page.EvaluateAsync<int>("() => document.querySelectorAll('article').length");
            
            _logger.LogInformation("Elementos en DOM: div={Div}, section={Section}, article={Article}", 
                divCount, sectionCount, articleCount);
            
            // Buscar selectores específicos
            var selectoresDebug = new[]
            {
                "[data-testid*='requirements']",
                "[data-testid*='visa']", 
                "[data-testid*='passport']",
                "[data-testid*='health']",
                "[class*='Requirement']",
                "[class*='requirement']"
            };
            
            foreach (var selector in selectoresDebug)
            {
                try
                {
                    var count = await page.EvaluateAsync<int>($"() => document.querySelectorAll('{selector.Replace("'", "\\'")}').length");
                    _logger.LogInformation("Selector '{Selector}': {Count} elementos encontrados", selector, count);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning("Error al contar selector {Selector}: {Error}", selector, ex.Message);
                }
            }
            
            // Guardar HTML para análisis
            var debugHtmlPath = $"debug_html_{DateTime.Now:yyyyMMdd_HHmmss}.html";
            var html = await page.ContentAsync();
            await File.WriteAllTextAsync(debugHtmlPath, html);
            _logger.LogInformation("HTML guardado en: {Path}", debugHtmlPath);
            
            // Intentar extraer texto de encabezados h1-h4
            var headings = await page.EvaluateAsync<string[]>(@"
                () => {
                    const h = document.querySelectorAll('h1, h2, h3, h4');
                    return Array.from(h).slice(0, 10).map(el => el.innerText.trim()).filter(t => t.length > 0);
                }
            ");
            
            if (headings != null && headings.Length > 0)
            {
                _logger.LogInformation("Primeros encabezados encontrados:");
                for (int i = 0; i < Math.Min(headings.Length, 5); i++)
                {
                    _logger.LogInformation("  H{i}: {Text}", i + 1, headings[i]?.Substring(0, Math.Min(100, headings[i]?.Length ?? 0)));
                }
            }
            else
            {
                _logger.LogWarning("No se encontraron encabezados h1-h4");
            }
            
            // Screenshot
            var screenshotPath = $"debug_screenshot_{DateTime.Now:yyyyMMdd_HHmmss}.png";
            await page.ScreenshotAsync(new PageScreenshotOptions 
            { 
                Path = screenshotPath,
                FullPage = true 
            });
            _logger.LogInformation("Screenshot guardado en: {Path}", screenshotPath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error durante diagnóstico de página");
        }
    }

    private TabExtraccion ResolverTabExtraccion(string? tipoNacionalidad)
    {
        return (tipoNacionalidad ?? "AMBOS").Trim().ToUpperInvariant() switch
        {
            "ORIGEN" => TabExtraccion.Departure,
            "DESTINO" => TabExtraccion.Return,
            _ => TabExtraccion.Ambos
        };
    }

    private async Task ActivarTabAsync(IPage page, string tabNombre)
    {
        var selectors = new[]
        {
            $"button[data-testid='{tabNombre.ToLower()}-tab']",
            $"button:has-text('{tabNombre}')",
            $"[role='tab']:has-text('{tabNombre}')",
            $"a:has-text('{tabNombre}')"
        };

        foreach (var selector in selectors)
        {
            try
            {
                if (await page.Locator(selector).CountAsync() > 0)
                {
                    await page.ClickAsync(selector);
                    await Task.Delay(1200);
                    return;
                }
            }
            catch { }
        }

        _logger.LogWarning("No se pudo activar tab {Tab}", tabNombre);
    }

    private async Task<string> ObtenerHtmlSegunTabAsync(IPage page, string htmlRaw, TabExtraccion tabExtraccion)
    {
        if (tabExtraccion == TabExtraccion.Ambos)
            return htmlRaw;

        var tab = tabExtraccion == TabExtraccion.Departure ? "Departure" : "Return";
        await ActivarTabAsync(page, tab);
        return await page.ContentAsync();
    }

    /// <summary>
    /// Libera los recursos de Playwright
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;

        try
        {
            if (_browser != null)
                await _browser.CloseAsync();
            _playwright?.Dispose();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al liberar recursos de Playwright");
        }

        _disposed = true;
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Clase auxiliar para representar una sección de requisitos
    /// </summary>
    private class SeccionRequisito
    {
        public string Tipo { get; set; } = "";
        public string Titulo { get; set; } = "";
        public string Contenido { get; set; } = "";
    }
}
