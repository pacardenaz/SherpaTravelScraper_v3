using System.Text.Json;
using SherpaTravelScraper.Models;
using System.Text.RegularExpressions;
using System.Collections.Concurrent;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
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
    private readonly UrlBuilderService _urlBuilder;
    private IPlaywright? _playwright;
    private IBrowser? _browser;
    private bool _disposed;

    public SherpaScraperService(
        ILogger<SherpaScraperService> logger,
        StealthConfig stealthConfig,
        IConfiguration configuration,
        AiExtractionService? aiService = null,
        UrlBuilderService? urlBuilder = null)
    {
        _logger = logger;
        _stealthConfig = stealthConfig;
        _configuration = configuration;
        _aiService = aiService;
        _urlBuilder = urlBuilder ?? new UrlBuilderService(NullLogger<UrlBuilderService>.Instance);
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

            var networkCollector = new NetworkJsonCollector(_logger);
            page.Response += networkCollector.OnResponseAsync;
            networkCollector.SetSegment(TabExtraccion.Departure);

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

            // PASO 4: Click robusto en submit (See requirements)
            _logger.LogInformation("🖱️ Click en 'See Requirements'...");
            var botonClickeado = await ClickSubmitRobustoAsync(page);

            if (!botonClickeado)
            {
                _logger.LogWarning("⚠️ Submit no clickeable; enviando Enter como fallback");
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
            var resultado = await ExtraerDatosAsync(page, htmlRaw, urlBase, origenIso3, destinoIso3, idioma, tabExtraccion, networkCollector);
            
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
            if (await TryJsClickIfVisibleAsync(page, "button:has-text('Accept all cookies')", 1500))
            {
                _logger.LogInformation("✅ Cookies aceptadas");
            }

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
                if (await TryJsClickIfVisibleAsync(page, "button:has-text('Change')", 1500))
                {
                    _logger.LogInformation("✅ Botón 'Change' clickeado");
                }
                else
                {
                    _logger.LogDebug("'Change' no disponible/interactuable, continuando...");
                }
            }

            // 2) Seleccionar passport (por ahora = origen)
            var paisOrigen = ObtenerNombrePaisDesdeIso3(origenIso3);
            await SeleccionarPaisEnCampoAsync(page, "Your Passport", paisOrigen);
            _logger.LogInformation("✅ Passport seleccionado: {Passport} ({Nombre})", origenIso3, paisOrigen);

            // 3) Seleccionar origen
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

    private async Task<bool> TryJsClickIfVisibleAsync(IPage page, string selector, int timeoutMs = 1200)
    {
        try
        {
            var loc = page.Locator(selector).First;
            if (await loc.IsVisibleAsync(new() { Timeout = timeoutMs }))
            {
                // JS click evita timeouts de Playwright cuando un overlay interfiere el pointer event
                await loc.EvaluateAsync("el => (el instanceof HTMLElement) && el.click()");
                await page.WaitForTimeoutAsync(200);
                return true;
            }
        }
        catch
        {
            // no-op
        }
        return false;
    }

    private async Task SeleccionarPaisEnCampoAsync(IPage page, string fieldLabel, string countryName)
    {
        try
        {
            // 1) Abrir selector (normal o JS click si hay overlay)
            var opened = false;
            try
            {
                if (fieldLabel.Equals("Your Passport", StringComparison.OrdinalIgnoreCase))
                {
                    // Sherpa suele renderizar Passport como primer combobox/button del formulario
                    var passportSelectors = new[]
                    {
                        "[role='combobox']",
                        "button[aria-haspopup='listbox']",
                        "button:has-text('(USA)')",
                        "button:has-text('(COL)')"
                    };

                    foreach (var ps in passportSelectors)
                    {
                        try
                        {
                            var loc = page.Locator(ps).First;
                            if (await loc.IsVisibleAsync(new() { Timeout = 1200 }))
                            {
                                await loc.ClickAsync(new() { Timeout = 2500 });
                                opened = true;
                                break;
                            }
                        }
                        catch { }
                    }
                }
                else
                {
                    var btn = page.Locator($"button:has-text('{fieldLabel}')").First;
                    await btn.ClickAsync(new() { Timeout = 2500 });
                    opened = true;
                }
            }
            catch
            {
                opened = await TryJsClickIfVisibleAsync(page, $"button:has-text('{fieldLabel}')", 1500);
            }

            if (!opened)
            {
                _logger.LogWarning("⚠️ No se pudo abrir selector para campo {Field}", fieldLabel);
                return;
            }

            await page.WaitForTimeoutAsync(250);

            // 2) Buscar input visible real del overlay
            ILocator? input = null;
            var inputSelectors = new[]
            {
                ".cdk-overlay-pane input:visible",
                "input[placeholder*='Search']:visible",
                "input.mat-mdc-input-element:visible",
                "input[role='combobox']:visible"
            };

            foreach (var sel in inputSelectors)
            {
                try
                {
                    var loc = page.Locator(sel).First;
                    if (await loc.IsVisibleAsync(new() { Timeout = 1200 }))
                    {
                        input = loc;
                        break;
                    }
                }
                catch { }
            }

            if (input == null)
            {
                _logger.LogWarning("⚠️ No se encontró input visible para {Field}", fieldLabel);
                return;
            }

            // 3) Escribir país
            await input.ClickAsync(new() { Timeout = 1500 });
            await input.PressAsync("Control+A");
            await input.PressAsync("Backspace");
            await input.FillAsync(countryName, new() { Timeout = 2500 });
            await page.WaitForTimeoutAsync(400);

            // 4) Seleccionar opción (intentos)
            var options = new[]
            {
                $"[role='option']:has-text('{countryName}')",
                $".mat-mdc-option:has-text('{countryName}')",
                $".mat-mdc-list-item:has-text('{countryName}')",
                "[role='option']:visible",
                ".mat-mdc-option:visible",
                ".mat-mdc-list-item:visible"
            };

            foreach (var selector in options)
            {
                try
                {
                    var opt = page.Locator(selector).First;
                    if (await opt.IsVisibleAsync(new() { Timeout = 1200 }))
                    {
                        await opt.ClickAsync(new() { Timeout = 1800 });
                        await page.WaitForTimeoutAsync(250);
                        return;
                    }
                }
                catch { }
            }

            // 5) Fallback teclado
            await page.Keyboard.PressAsync("ArrowDown");
            await page.Keyboard.PressAsync("Enter");
            await page.WaitForTimeoutAsync(250);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "⚠️ Falló selección de país {Country} en campo {Field}", countryName, fieldLabel);
        }
    }


    private async Task<bool> ClickSubmitRobustoAsync(IPage page)
    {
        // Ordenado por confiabilidad observada en Sherpa
        var submitLocators = new[]
        {
            page.GetByRole(AriaRole.Button, new() { Name = "See requirements" }).First,
            page.GetByRole(AriaRole.Button, new() { Name = "See Requirements" }).First,
            page.Locator("button:has-text('See requirements')").First,
            page.Locator("button:has-text('See Requirements')").First,
            page.Locator("button[type='submit']").First,
            page.Locator("button.w-full.large").First
        };

        foreach (var loc in submitLocators)
        {
            try
            {
                // visible + habilitado
                if (!await loc.IsVisibleAsync())
                    continue;

                if (!await loc.IsEnabledAsync())
                {
                    _logger.LogDebug("Submit visible pero deshabilitado; esperando 500ms...");
                    await page.WaitForTimeoutAsync(500);
                    if (!await loc.IsEnabledAsync())
                        continue;
                }

                await loc.ScrollIntoViewIfNeededAsync();
                await loc.ClickAsync(new() { Timeout = 2500 });
                _logger.LogInformation("✅ Submit clickeado");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogDebug("Intento submit falló: {Error}", ex.Message);
            }
        }

        // Último recurso: JS click sobre cualquier botón candidate visible
        var jsClicked = await page.EvaluateAsync<bool>(@"
            () => {
              const candidates = Array.from(document.querySelectorAll('button'));
              const btn = candidates.find(b => {
                const t = (b.innerText || b.textContent || '').toLowerCase();
                return (t.includes('see requirements') || t.includes('see requirement')) && !b.disabled;
              });
              if (!btn) return false;
              btn.click();
              return true;
            }
        ");

        if (jsClicked)
        {
            _logger.LogInformation("✅ Submit clickeado vía JS fallback");
            return true;
        }

        return false;
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
    private async Task<ResultadoScraping> ExtraerDatosAsync(IPage page, string htmlRaw, string url, string origen, string destino, string idioma, TabExtraccion tabExtraccion, NetworkJsonCollector networkCollector)
    {
        var extractionMethod = _aiService?.GetExtractionMethod() ?? "javascript";
        _logger.LogInformation("Método de extracción configurado: {Metodo}", extractionMethod);

        if (extractionMethod == "network-json")
        {
            var networkResult = await ExtraerDatosNetworkJsonAsync(page, url, origen, destino, idioma, tabExtraccion, networkCollector, htmlRaw);
            if (networkResult != null)
            {
                return networkResult;
            }

            _logger.LogWarning("⚠️ No se capturó JSON válido de red. Ejecutando fallback javascript/ia-html...");
            return await ExtraerFallbackExistenteAsync(page, htmlRaw, url, origen, destino, idioma, tabExtraccion);
        }

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
                    _logger.LogInformation("Usando extracción IA Visión (con screenshots)...");
                    extraccion = await ExtraerConIaVisionAsync(page, htmlRaw, origen, destino, idioma, tabExtraccion);
                }
                else if (extractionMethod == "ia-html" || extractionMethod == "html")
                {
                    _logger.LogInformation("Usando extracción IA HTML (solo texto)...");
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
        // Selectores mejorados para mayor cobertura
        var nombresAlternativos = tabNombre.ToLower() switch
        {
            "departure" => new[] { "Departure", "Salida", "departure", "salida" },
            "return" => new[] { "Return", "Regreso", "return", "regreso" },
            _ => new[] { tabNombre }
        };

        var selectors = new List<string>
        {
            $"[data-testid='{tabNombre.ToLower()}-tab']",
            $"button[id*='{tabNombre.ToLower()}']",
            $"[role='tab'][id*='{tabNombre.ToLower()}']"
        };

        // Agregar selectores con textos alternativos
        foreach (var nombre in nombresAlternativos)
        {
            selectors.Add($"button:has-text('{nombre}')");
            selectors.Add($"[role='tab']:has-text('{nombre}')");
            selectors.Add($"a:has-text('{nombre}')");
        }

        foreach (var selector in selectors)
        {
            try
            {
                var element = await page.QuerySelectorAsync(selector);
                if (element != null)
                {
                    var isVisible = await element.IsVisibleAsync();
                    if (isVisible)
                    {
                        await element.ClickAsync();
                        _logger.LogDebug("Tab {Tab} clickeado con selector: {Selector}", tabNombre, selector);
                        await Task.Delay(1500); // Esperar más tiempo para que cargue
                        return;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogTrace("Error con selector {Selector}: {Error}", selector, ex.Message);
            }
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

    private async Task<ResultadoScraping?> ExtraerDatosNetworkJsonAsync(
        IPage page,
        string url,
        string origen,
        string destino,
        string idioma,
        TabExtraccion tabExtraccion,
        NetworkJsonCollector collector,
        string htmlRaw)
    {
        collector.SetSegment(TabExtraccion.Departure);
        
        // Esperar a que se cargue el JSON de Departure (con timeout)
        var departureJson = await EsperarJsonConTimeoutAsync(collector, TabExtraccion.Departure, timeoutMs: 5000);
        _logger.LogDebug("JSON Departure capturado: {Size} chars", departureJson?.Length ?? 0);

        string? returnJson = null;
        if (tabExtraccion is TabExtraccion.Return or TabExtraccion.Ambos)
        {
            // Cambiar a segmento Return ANTES de hacer click
            collector.SetSegment(TabExtraccion.Return);
            
            // Hacer click en el tab Return
            await ActivarTabAsync(page, "Return");
            
            // Esperar a que llegue el JSON de Return (con timeout más largo porque requiere nueva llamada API)
            returnJson = await EsperarJsonConTimeoutAsync(collector, TabExtraccion.Return, timeoutMs: 8000);
            _logger.LogDebug("JSON Return capturado: {Size} chars", returnJson?.Length ?? 0);
        }

        // Validar que tenemos los JSON necesarios
        var hasDeparture = !string.IsNullOrWhiteSpace(departureJson);
        var hasReturn = !string.IsNullOrWhiteSpace(returnJson);
        
        var requiereDeparture = tabExtraccion is TabExtraccion.Departure or TabExtraccion.Ambos;
        var requiereReturn = tabExtraccion is TabExtraccion.Return or TabExtraccion.Ambos;
        
        if ((requiereDeparture && !hasDeparture) || (requiereReturn && !hasReturn))
        {
            _logger.LogWarning(
                "No se capturaron todos los JSON requeridos - Departure: {HasDeparture}, Return: {HasReturn}",
                hasDeparture, hasReturn);
            return null;
        }

        var modelo = MapearNetworkJsonARequisitos(origen, destino, idioma, departureJson, returnJson, tabExtraccion);

        // JSON #1 (resumido): solo lo que representa lo visible en pantalla
        var resumenPayload = new
        {
            metodoExtraccion = "network-json",
            infoViaje = modelo.InfoViaje,
            departure = modelo.Departure,
            @return = modelo.Return,
            tabs = tabExtraccion.ToString()
        };

        // JSON #2 (crudo): payload original capturado de red (embebido como JSON, no string escapado)
        var rawPayload = new
        {
            metodoExtraccion = "network-json",
            capturedAt = DateTime.UtcNow,
            tabs = tabExtraccion.ToString(),
            raw = new
            {
                departure = ParseRawJsonForStorage(departureJson),
                @return = ParseRawJsonForStorage(returnJson)
            }
        };

        var datosJson = JsonSerializer.Serialize(resumenPayload, new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });

        var rawJson = JsonSerializer.Serialize(rawPayload, new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });

        return ResultadoScraping.Exito(
            datos: datosJson,          // reqvd_datos_json -> RESUMEN
            markdown: rawJson,         // reqvd_markdown   -> RAW JSON
            url: url,
            htmlRaw: htmlRaw,
            requisitosDestino: ConstruirResumenTramo(modelo.Departure),
            requisitosVisado: modelo.Departure?.Visa?.Descripcion ?? modelo.Return?.Visa?.Descripcion,
            pasaportes: modelo.Departure?.Pasaporte?.Notas ?? modelo.Return?.Pasaporte?.Notas,
            sanitarios: modelo.Departure?.Salud?.Notas ?? modelo.Return?.Salud?.Notas,
            tabsExtraidas: tabExtraccion.ToString());
    }

    private async Task<ResultadoScraping> ExtraerFallbackExistenteAsync(
        IPage page,
        string htmlRaw,
        string url,
        string origen,
        string destino,
        string idioma,
        TabExtraccion tabExtraccion)
    {
        if (_aiService != null && _configuration.GetValue<bool>("AI:Enabled", true) && _configuration.GetValue<bool>("Extraction:IaHtml:Enabled", false))
        {
            try
            {
                var htmlPorTab = await ObtenerHtmlSegunTabAsync(page, htmlRaw, tabExtraccion);
                var extraccion = await _aiService.ExtraerConOpenRouterHtmlAsync(htmlPorTab, origen, destino, idioma)
                    ?? await _aiService.ExtraerConKimiHtmlAsync(htmlPorTab, origen, destino, idioma);

                if (extraccion != null)
                {
                    var jsonDatos = JsonSerializer.Serialize(extraccion, new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
                    return ResultadoScraping.Exito(datos: jsonDatos, url: url, htmlRaw: htmlRaw, markdown: extraccion.Markdown, tabsExtraidas: tabExtraccion.ToString());
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Fallback IA HTML falló, usando JavaScript tradicional");
            }
        }

        return await ExtraerDatosTradicionalesAsync(page, htmlRaw, url);
    }

    private static object? ParseRawJsonForStorage(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        try
        {
            // Si viene JSON válido, devolverlo como objeto para serialización anidada limpia
            return JsonSerializer.Deserialize<object>(raw);
        }
        catch
        {
            // Si no parsea, guardar el texto original
            return raw;
        }
    }

    private RequisitosViajeCompleto MapearNetworkJsonARequisitos(
        string origen,
        string destino,
        string idioma,
        string? departureJson,
        string? returnJson,
        TabExtraccion tabExtraccion)
    {
        var resultado = new RequisitosViajeCompleto
        {
            MetodoExtraccion = "network-json",
            Confianza = 0.9,
            InfoViaje = new InformacionViaje { Origen = origen, Destino = destino, Idioma = idioma }
        };

        if (tabExtraccion is TabExtraccion.Departure or TabExtraccion.Ambos)
        {
            resultado.Departure = MapearTramoDesdeJson(departureJson, "Departure", origen, destino);
        }

        if (tabExtraccion is TabExtraccion.Return or TabExtraccion.Ambos)
        {
            resultado.Return = MapearTramoDesdeJson(returnJson, "Return", destino, origen);
        }

        return resultado;
    }

    private RequisitosTramo MapearTramoDesdeJson(string? rawJson, string direccion, string salida, string llegada)
    {
        var tramo = new RequisitosTramo
        {
            Direccion = direccion,
            PaisSalida = salida,
            PaisLlegada = llegada
        };

        if (string.IsNullOrWhiteSpace(rawJson))
        {
            return tramo;
        }

        try
        {
            using var doc = JsonDocument.Parse(rawJson);
            var rootText = doc.RootElement.ToString();
            tramo.Visa.Descripcion = ExtraerTextoPorClaves(rootText, "visa", "entry", "permit");
            tramo.Visa.Requerido = !string.IsNullOrWhiteSpace(tramo.Visa.Descripcion) &&
                                   !tramo.Visa.Descripcion.Contains("not required", StringComparison.OrdinalIgnoreCase);

            tramo.Pasaporte.Notas = ExtraerTextoPorClaves(rootText, "passport", "document", "valid");
            tramo.Salud.Notas = ExtraerTextoPorClaves(rootText, "health", "vacc", "covid", "test");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "No se pudo mapear JSON para tramo {Tramo}", direccion);
        }

        return tramo;
    }

    private static string? ExtraerTextoPorClaves(string content, params string[] keys)
    {
        if (string.IsNullOrWhiteSpace(content)) return null;

        // Intentar parsear JSON y extraer textos más limpios por nombre de propiedad
        try
        {
            using var doc = JsonDocument.Parse(content);
            var matches = new List<string>();

            void Walk(JsonElement el, string? prop)
            {
                switch (el.ValueKind)
                {
                    case JsonValueKind.Object:
                        foreach (var p in el.EnumerateObject())
                            Walk(p.Value, p.Name);
                        break;
                    case JsonValueKind.Array:
                        foreach (var item in el.EnumerateArray())
                            Walk(item, prop);
                        break;
                    case JsonValueKind.String:
                        var v = el.GetString();
                        if (string.IsNullOrWhiteSpace(v)) return;
                        var k = (prop ?? string.Empty).ToLowerInvariant();
                        var vv = v.ToLowerInvariant();
                        if (keys.Any(x => k.Contains(x, StringComparison.OrdinalIgnoreCase) || vv.Contains(x, StringComparison.OrdinalIgnoreCase)))
                        {
                            var clean = v.Trim();
                            if (clean.Length > 8 && clean.Length < 300 && !matches.Contains(clean))
                                matches.Add(clean);
                        }
                        break;
                }
            }

            Walk(doc.RootElement, null);
            if (matches.Count > 0)
                return string.Join(" | ", matches.Take(6));
        }
        catch
        {
            // fallback texto plano
        }

        var lines = content.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(l => keys.Any(k => l.Contains(k, StringComparison.OrdinalIgnoreCase)))
            .Take(8)
            .ToList();

        return lines.Count == 0 ? null : string.Join(" | ", lines);
    }

    private static string? ConstruirResumenTramo(RequisitosTramo? tramo)
    {
        if (tramo == null) return null;
        return $"[{tramo.Direccion}] Visa: {tramo.Visa.Descripcion ?? "N/D"}\nPasaporte: {tramo.Pasaporte.Notas ?? "N/D"}\nSalud: {tramo.Salud.Notas ?? "N/D"}";
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

    private sealed class NetworkJsonCollector
    {
        private readonly ILogger _logger;
        private readonly ConcurrentDictionary<TabExtraccion, string> _payloads = new();
        private TabExtraccion _currentSegment = TabExtraccion.Departure;

        public NetworkJsonCollector(ILogger logger)
        {
            _logger = logger;
        }

        public void SetSegment(TabExtraccion segment) => _currentSegment = segment;

        public void OnResponseAsync(object sender, IResponse response)
            => _ = HandleResponseAsync(response);

        private async Task HandleResponseAsync(IResponse response)
        {
            try
            {
                var url = response.Url;
                if (!url.Contains("requirements-api.joinsherpa.com", StringComparison.OrdinalIgnoreCase)) return;
                if (!url.Contains("/trips", StringComparison.OrdinalIgnoreCase)) return;
                if (!url.Contains("include=restriction,procedure", StringComparison.OrdinalIgnoreCase)) return;
                if (response.Status < 200 || response.Status >= 300) return;

                var body = await response.TextAsync();
                if (string.IsNullOrWhiteSpace(body)) return;

                try { JsonDocument.Parse(body); }
                catch { return; }

                _payloads[_currentSegment] = body;
                _logger.LogInformation("📡 Network JSON capturado | Tab={Tab} | URL={Url} | Payload={Size} bytes", _currentSegment, url, body.Length);
            }
            catch
            {
                // no-op
            }
        }

        public string? GetLatestPayload(TabExtraccion segment) => _payloads.TryGetValue(segment, out var value) ? value : null;

        public bool HasValidJsonFor(TabExtraccion tab) => tab switch
        {
            TabExtraccion.Departure => !string.IsNullOrWhiteSpace(GetLatestPayload(TabExtraccion.Departure)),
            TabExtraccion.Return => !string.IsNullOrWhiteSpace(GetLatestPayload(TabExtraccion.Return)),
            _ => !string.IsNullOrWhiteSpace(GetLatestPayload(TabExtraccion.Departure)) ||
                 !string.IsNullOrWhiteSpace(GetLatestPayload(TabExtraccion.Return))
        };
    }

    /// <summary>
    /// Espera a que llegue el JSON de un segmento específico con timeout y polling
    /// </summary>
    private async Task<string?> EsperarJsonConTimeoutAsync(
        NetworkJsonCollector collector,
        TabExtraccion segment,
        int timeoutMs = 5000,
        int pollingIntervalMs = 500)
    {
        var startTime = DateTime.UtcNow;
        
        while ((DateTime.UtcNow - startTime).TotalMilliseconds < timeoutMs)
        {
            var payload = collector.GetLatestPayload(segment);
            if (!string.IsNullOrWhiteSpace(payload))
            {
                return payload;
            }
            
            await Task.Delay(pollingIntervalMs);
        }
        
        return null;
    }

    #region REQ-SHERPA-003: Estrategia Híbrida de Scraping

    /// <summary>
    /// Realiza scraping usando únicamente navegación directa por URL con parámetros
    /// y extracción vía interceptación de red de la API trips
    /// </summary>
    public async Task<ScrapingResult> ScrapearConEstrategiaHibridaAsync(
        string origenIso3,
        string destinoIso3,
        string idioma,
        DateTime fechaBase,
        string? tipoNacionalidad = null)
    {
        if (_browser == null)
            throw new InvalidOperationException("El servicio no ha sido inicializado. Llame a InicializarAsync primero.");

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var tabExtraccion = ResolverTabExtraccion(tipoNacionalidad);
        
        _logger.LogInformation("🚀 REQ-SHERPA-003: Iniciando scraping por URL directa - {Origen} -> {Destino} (Tipo: {Tipo})",
            origenIso3, destinoIso3, tipoNacionalidad ?? "AMBOS");

        // Solo usar URL directa con interceptación de red
        var directUrlResult = await IntentarScrapeoDirectoAsync(
            origenIso3, destinoIso3, idioma, fechaBase, tabExtraccion, CancellationToken.None);
        
        if (directUrlResult != null && directUrlResult.IsSuccess)
        {
            stopwatch.Stop();
            directUrlResult.Duration = stopwatch.Elapsed;
            
            _logger.LogInformation(
                "✅ REQ-SHERPA-003: Scraping exitoso por URL directa en {Duration}ms - {Origen}->{Destino}",
                stopwatch.ElapsedMilliseconds, origenIso3, destinoIso3);
            
            return directUrlResult;
        }
        
        // Si falla, retornar error (no hay fallback a formulario)
        stopwatch.Stop();
        
        var errorMsg = directUrlResult?.ErrorMessage ?? "URL directa no devolvió contenido válido";
        _logger.LogError(
            "❌ REQ-SHERPA-003: URL directa falló - {Origen}->{Destino} - {Error}",
            origenIso3, destinoIso3, errorMsg);
        
        return ScrapingResult.Failure(
            $"URL directa falló: {errorMsg}",
            directUrlResult?.UsedMethod ?? ScrapingMethod.DirectUrl);
    }

    /// <summary>
    /// Intenta realizar scraping mediante navegación directa a URL con parámetros
    /// Retorna null si no se pudo cargar contenido válido
    /// </summary>
    private async Task<ScrapingResult?> IntentarScrapeoDirectoAsync(
        string origenIso3,
        string destinoIso3,
        string idioma,
        DateTime fechaBase,
        TabExtraccion tabExtraccion,
        CancellationToken cancellationToken)
    {
        var urlDirecta = _urlBuilder.BuildDirectUrl(
            destinoIso3, origenIso3, origenIso3, idioma, fechaBase.AddDays(1), fechaBase.AddDays(8));
        
        _logger.LogInformation("🔗 REQ-SHERPA-003: Intentando URL directa: {Url}", urlDirecta);
        
        IPage? page = null;
        
        try
        {
            // Crear contexto con configuración stealth
            var contextOptions = _stealthConfig.GetStealthContextOptions();
            await using var context = await _browser!.NewContextAsync(contextOptions);
            
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

            // Configurar collector para interceptar API trips
            var networkCollector = new NetworkJsonCollector(_logger);
            page.Response += networkCollector.OnResponseAsync;
            networkCollector.SetSegment(TabExtraccion.Departure);

            // Ejecutar scripts de stealth
            foreach (var script in StealthConfig.StealthScripts)
            {
                try { await page.AddInitScriptAsync(script); }
                catch { /* Ignorar errores de scripts */ }
            }

            // Navegar a URL directa con timeout corto
            var directUrlTimeout = _configuration.GetValue<int>("Scraping:DirectUrlTimeoutMs", 15000);
            
            _logger.LogDebug("Navegando a URL directa (timeout: {Timeout}ms)...", directUrlTimeout);
            
            var response = await page.GotoAsync(urlDirecta, new PageGotoOptions
            {
                WaitUntil = WaitUntilState.NetworkIdle,
                Timeout = directUrlTimeout
            });
            
            if (response == null || response.Status >= 400)
            {
                _logger.LogWarning("⚠️ URL directa no accesible - Status: {Status}", response?.Status ?? 0);
                return null;
            }
            
            _logger.LogInformation("✅ URL directa accesible - Status: {Status}", response.Status);
            
            // Delay anti-detección
            await Task.Delay(_stealthConfig.GetRandomDelayMs() / 2);

            // Extraer datos usando NetworkJson (captura JSON de Departure y Return)
            var htmlRaw = await page.ContentAsync();
            var resultadoNetwork = await ExtraerDatosNetworkJsonAsync(
                page, urlDirecta, origenIso3, destinoIso3, idioma, tabExtraccion, networkCollector, htmlRaw);
            
            if (resultadoNetwork != null && resultadoNetwork.Exitoso)
            {
                _logger.LogInformation("📡 JSON de API trips extraído y procesado correctamente");
                
                // Convertir ResultadoScraping a ScrapingResult
                return new ScrapingResult
                {
                    DepartureHtml = resultadoNetwork.Datos,      // JSON resumido
                    ReturnHtml = resultadoNetwork.Markdown,      // JSON crudo
                    UsedMethod = ScrapingMethod.DirectUrl,
                    UrlUsed = urlDirecta,
                    IsPartial = tabExtraccion != TabExtraccion.Ambos
                };
            }
            
            // Si no se pudo extraer via NetworkJson, continuar con verificación de contenido HTML
            _logger.LogDebug("No se pudo extraer JSON de API trips, intentando extracción HTML...");
            
            // Verificar contenido cargado
            var contentVerifierLogger = NullLogger<ContentVerifier>.Instance;
            var verifier = new ContentVerifier(page, contentVerifierLogger);
            
            var minContentLength = _configuration.GetValue<int>("Scraping:MinContentLength", 500);
            var verificationResult = await verifier.VerifyAsync(minContentLength, cancellationToken);
            
            if (!verificationResult.IsValid)
            {
                _logger.LogWarning(
                    "⚠️ Contenido de URL directa no válido - Departure: {Departure}, Return: {Return}, " +
                    "ContentLength: {ContentLength}",
                    verificationResult.HasDepartureTab,
                    verificationResult.HasReturnTab,
                    verificationResult.ActiveTabContentLength);
                
                return null;
            }
            
            _logger.LogInformation("✅ Contenido verificado - tabs presentes y contenido sustancial");
            
            // Extraer datos según tabExtraccion
            var resultado = await ExtraerDatosDesdePaginaAsync(
                page, urlDirecta, origenIso3, destinoIso3, idioma, tabExtraccion, null);
            
            if (!string.IsNullOrEmpty(resultado.MensajeError))
            {
                _logger.LogWarning("⚠️ Error extrayendo datos de URL directa: {Error}", resultado.MensajeError);
                return null;
            }
            
            if (!resultado.Exitoso)
            {
                _logger.LogWarning("⚠️ Extracción de URL directa no exitosa");
                return null;
            }
            
            return new ScrapingResult
            {
                DepartureHtml = resultado.HtmlRaw, // HTML completo de la página
                ReturnHtml = null, // Contenido completo en un solo campo
                UsedMethod = ScrapingMethod.DirectUrl,
                UrlUsed = urlDirecta,
                IsPartial = tabExtraccion != TabExtraccion.Ambos
            };
        }
        catch (TimeoutException ex)
        {
            _logger.LogWarning(ex, "⏱️ Timeout navegando a URL directa");
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Error inesperado en scraping directo");
            return null;
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
    /// Extrae datos de una página ya cargada (ya sea por URL directa o formulario)
    /// Versión simplificada del método existente
    /// </summary>
    private async Task<ResultadoScraping> ExtraerDatosDesdePaginaAsync(
        IPage page,
        string url,
        string origenIso3,
        string destinoIso3,
        string idioma,
        TabExtraccion tabExtraccion,
        NetworkJsonCollector? networkCollector)
    {
        try
        {
            string? departureHtml = null;
            string? returnHtml = null;

            // Extraer Departure si es necesario
            if (tabExtraccion == TabExtraccion.Departure || tabExtraccion == TabExtraccion.Ambos)
            {
                departureHtml = await ExtraerHtmlTabAsync(page, "Departure");
                _logger.LogDebug("Departure HTML extraído: {Length} chars", departureHtml?.Length ?? 0);
            }

            // Extraer Return si es necesario
            if (tabExtraccion == TabExtraccion.Return || tabExtraccion == TabExtraccion.Ambos)
            {
                // Click en tab Return
                var returnClicked = await ClickReturnTabAsync(page);
                if (returnClicked)
                {
                    await Task.Delay(1000); // Esperar carga
                    returnHtml = await ExtraerHtmlTabAsync(page, "Return");
                    _logger.LogDebug("Return HTML extraído: {Length} chars", returnHtml?.Length ?? 0);
                }
            }

            // Combinar HTMLs si es necesario
            var htmlCompleto = page.ContentAsync().Result;

            return new ResultadoScraping
            {
                Exitoso = true,
                UrlConsultada = url,
                HtmlRaw = htmlCompleto,
                TabsExtraidas = tabExtraccion.ToString()
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error extrayendo datos de página");
            return ResultadoScraping.Fallo($"Error extracción: {ex.Message}", url);
        }
    }

    /// <summary>
    /// Hace click en el tab Return
    /// </summary>
    private async Task<bool> ClickReturnTabAsync(IPage page)
    {
        var selectors = new[]
        {
            "[data-testid='return-tab']",
            "button:has-text('Return')",
            "button:has-text('Regreso')",
            "[role='tab']:has-text('Return')",
            "[role='tab']:has-text('Regreso')",
            "button[id*='return']"
        };
        
        foreach (var selector in selectors)
        {
            try
            {
                var element = await page.QuerySelectorAsync(selector);
                if (element != null)
                {
                    await element.ClickAsync();
                    _logger.LogDebug("Tab Return clickeado con selector: {Selector}", selector);
                    return true;
                }
            }
            catch (Exception ex)
            {
                _logger.LogTrace("Error click Return con selector {Selector}: {Error}", selector, ex.Message);
            }
        }
        
        return false;
    }

    /// <summary>
    /// Extrae el HTML de un tab específico
    /// </summary>
    private async Task<string?> ExtraerHtmlTabAsync(IPage page, string tabName)
    {
        // Intentar obtener contenido del tab activo
        var contentSelectors = new[]
        {
            ".requirements-content",
            "[data-testid='requirements-content']",
            ".tab-content",
            "[data-testid='tab-content']"
        };
        
        foreach (var selector in contentSelectors)
        {
            try
            {
                var element = await page.QuerySelectorAsync(selector);
                if (element != null)
                {
                    var html = await element.InnerHTMLAsync();
                    if (!string.IsNullOrWhiteSpace(html))
                    {
                        return html;
                    }
                }
            }
            catch { /* Ignorar y probar siguiente */ }
        }
        
        // Fallback: obtener todo el body
        try
        {
            return await page.ContentAsync();
        }
        catch
        {
            return null;
        }
    }

    #endregion

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
