using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

using SherpaTravelScraper.Models;
using SherpaTravelScraper.Utils;

namespace SherpaTravelScraper.Services;

/// <summary>
/// Servicio unificado de extracción con IA
/// Soporta: JavaScript (clásico), IA Visión (screenshots), IA HTML (texto)
/// </summary>
public class AiExtractionService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<AiExtractionService> _logger;
    private readonly IConfiguration _config;
    private readonly IConfiguration _extractionConfig;
    private readonly string _extractionMethod;

    public AiExtractionService(
        IConfiguration config,
        ILogger<AiExtractionService> logger,
        HttpClient? httpClient = null)
    {
        _config = config;
        _extractionConfig = config.GetSection("Extraction");
        _logger = logger;
        _httpClient = httpClient ?? new HttpClient();
        _extractionMethod = _extractionConfig["Method"]?.ToLower() ?? "javascript";
        
        // Cargar API keys desde variables de entorno si no están configuradas
        LoadApiKeysFromEnvironment();
        
        ConfigureHttpClient();
    }

    /// <summary>
    /// Carga las API keys desde variables de entorno si no están configuradas en appsettings
    /// </summary>
    private void LoadApiKeysFromEnvironment()
    {
        // Intentar cargar OpenRouter API Key desde variable de entorno
        var openRouterKey = EnvLoader.Get("OPENROUTER_API_KEY");
        if (!string.IsNullOrEmpty(openRouterKey))
        {
            // Actualizar la configuración en memoria
            var visionSection = _extractionConfig.GetSection("IaVision");
            var htmlSection = _extractionConfig.GetSection("IaHtml");
            
            if (string.IsNullOrEmpty(visionSection["ApiKey"]))
            {
                visionSection["ApiKey"] = openRouterKey;
            }
            if (string.IsNullOrEmpty(htmlSection["ApiKey"]))
            {
                htmlSection["ApiKey"] = openRouterKey;
            }
        }
        
        // Intentar cargar Kimi API Key desde variable de entorno
        var kimiKey = EnvLoader.Get("KIMI_API_KEY");
        if (!string.IsNullOrEmpty(kimiKey))
        {
            var aiSection = _config.GetSection("AI");
            if (string.IsNullOrEmpty(aiSection["Kimi:ApiKey"]))
            {
                aiSection["Kimi:ApiKey"] = kimiKey;
            }
        }
    }

    private void ConfigureHttpClient()
    {
        var timeout = TimeSpan.FromSeconds(120);
        _httpClient.Timeout = timeout;
    }

    /// <summary>
    /// Obtiene el método de extracción configurado
    /// </summary>
    public string GetExtractionMethod() => _extractionMethod;

    /// <summary>
    /// Extrae requisitos completos de viaje usando el método configurado
    /// </summary>
    public async Task<RequisitosViajeCompleto?> ExtraerRequisitosCompletosAsync(
        string htmlContent,
        byte[][] screenshotsBytes,
        string origen,
        string destino,
        string idioma,
        CancellationToken ct = default)
    {
        _logger.LogInformation("Extrayendo requisitos: {Origen} -> {Destino} usando método: {Metodo}", 
            origen, destino, _extractionMethod);

        try
        {
            return _extractionMethod switch
            {
                "javascript" or "js" => await ExtraerConJavaScriptAsync(htmlContent, screenshotsBytes, origen, destino, idioma, ct),
                "ia-vision" or "vision" => await ExtraerConIaVisionAsync(htmlContent, screenshotsBytes, origen, destino, idioma, ct),
                "ia-html" or "html" => await ExtraerConIaHtmlAsync(htmlContent, origen, destino, idioma, ct),
                _ => throw new NotSupportedException($"Método de extracción no soportado: {_extractionMethod}")
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error extrayendo requisitos con método {Metodo}", _extractionMethod);
            return null;
        }
    }

    #region MÉTODO 1: JAVASCRIPT (CLÁSICO)

    /// <summary>
    /// Extrae requisitos usando JavaScript tradicional (sin IA)
    /// </summary>
    private async Task<RequisitosViajeCompleto?> ExtraerConJavaScriptAsync(
        string html, byte[][] screenshotsBytes, string origen, string destino, string idioma, CancellationToken ct)
    {
        _logger.LogInformation("Usando extracción JavaScript tradicional");
        
        // Este método retorna null porque la extracción JS se hace en SherpaScraperService
        // Solo indica que no se usará IA
        return new RequisitosViajeCompleto
        {
            InfoViaje = new InformacionViaje { Origen = origen, Destino = destino },
            MetodoExtraccion = "javascript",
            Confianza = 0.5,
            Markdown = "Extracción mediante JavaScript tradicional - datos se procesan en SherpaScraperService"
        };
    }

    #endregion

    #region MÉTODO 2: IA VISIÓN (SCREENSHOTS)

    /// <summary>
    /// Extrae requisitos usando IA con modelos de visión (screenshots)
    /// </summary>
    private async Task<RequisitosViajeCompleto?> ExtraerConIaVisionAsync(
        string html, byte[][] screenshotsBytes, string origen, string destino, string idioma, CancellationToken ct)
    {
        var screenshotsBase64 = screenshotsBytes.Select(Convert.ToBase64String).ToArray();
        
        _logger.LogInformation("Usando extracción IA Visión con {Count} screenshots", screenshotsBytes.Length);

        var provider = _extractionConfig["IaVision:Provider"]?.ToLower() ?? "kimi";
        
        try
        {
            var resultado = provider switch
            {
                "kimi" => await ExtraerConKimiVisionAsync(html, screenshotsBase64, origen, destino, idioma, ct),
                "ollama" => await ExtraerConOllamaVisionAsync(html, screenshotsBase64, origen, destino, idioma, ct),
                "openrouter" => await ExtraerConOpenRouterVisionAsync(html, screenshotsBase64, origen, destino, idioma, ct),
                _ => throw new NotSupportedException($"Proveedor de visión no soportado: {provider}")
            };

            if (resultado != null)
            {
                resultado.MetodoExtraccion = "ia-vision";
            }

            return resultado;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error en extracción IA Visión");
            return null;
        }
    }

    private async Task<RequisitosViajeCompleto?> ExtraerConKimiVisionAsync(
        string html, string[] screenshotsB64, string origen, string destino, string idioma, CancellationToken ct)
    {
        var endpoint = _extractionConfig["IaVision:Endpoint"]!;
        var apiKey = _extractionConfig["IaVision:ApiKey"]!;
        var model = _extractionConfig["IaVision:Model"]!;

        _logger.LogInformation("🤖 Llamando a Kimi Vision API - Modelo: {Model}", model);
        _logger.LogInformation("📸 Enviando {Count} imágenes", screenshotsB64.Length);

        var prompt = ConstruirPromptVision(origen, destino, idioma);

        // Construir contenido con imágenes para Kimi
        var contentList = new List<object>
        {
            new { type = "text", text = prompt }
        };

        // Agregar ambas imágenes
        foreach (var screenshotB64 in screenshotsB64)
        {
            contentList.Add(new { type = "image_url", image_url = new { url = $"data:image/png;base64,{screenshotB64}" } });
        }

        // Agregar HTML truncado como texto adicional
        var maxHtmlLength = _extractionConfig.GetValue<int>("IaVision:MaxHtmlLength", 4000);
        var htmlTruncado = html.Length > maxHtmlLength ? html[..maxHtmlLength] : html;
        contentList.Add(new { type = "text", text = $"HTML adicional (truncado):\n{htmlTruncado}" });

        var request = new KimiRequest
        {
            Model = model,
            Messages = new[]
            {
                new KimiMessage
                {
                    Role = "system",
                    Content = "Eres un experto en extracción de requisitos de viaje. Analiza las imágenes proporcionadas y responde SOLO con JSON válido."
                },
                new KimiMessage
                {
                    Role = "user",
                    Content = contentList.ToArray()
                }
            },
            ResponseFormat = new { type = "json_object" },
            Temperature = _extractionConfig.GetValue<double>("IaVision:Temperature", 0.1),
            MaxTokens = _extractionConfig.GetValue<int>("IaVision:MaxTokens", 4096)
        };

        _httpClient.DefaultRequestHeaders.Authorization = 
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);

        try
        {
            var response = await _httpClient.PostAsJsonAsync(endpoint, request, ct);
            _logger.LogInformation("📡 Kimi Vision respondió con status: {Status}", response.StatusCode);

            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync();
                _logger.LogError("❌ Kimi Vision error body: {Error}", errorBody);
            }

            response.EnsureSuccessStatusCode();

            var result = await response.Content.ReadFromJsonAsync<KimiResponse>(ct);
            var content = result?.Choices?.FirstOrDefault()?.Message?.Content as string;

            _logger.LogInformation("📝 Respuesta cruda de Kimi Vision ({Length} chars)", content?.Length ?? 0);

            await GuardarRespuestaCrudaAsync("kimi_vision", origen, destino, content);
            
            // Intentar parsear el JSON de la respuesta
            var resultado = ParsearRespuestaCompleta(content, origen, destino);
            
            if (resultado != null)
            {
                resultado.Markdown = content;
                resultado.MetodoExtraccion = "ia-vision";
                _logger.LogInformation("✅ JSON parseado exitosamente desde respuesta Kimi Vision");
                return resultado;
            }
            
            _logger.LogWarning("⚠️ No se pudo parsear JSON, devolviendo markdown sin estructura");
            return new RequisitosViajeCompleto
            {
                InfoViaje = new InformacionViaje { Origen = origen, Destino = destino },
                Markdown = content ?? "",
                MetodoExtraccion = "ia-vision",
                Confianza = 0.3
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Error llamando a Kimi Vision API: {Message}", ex.Message);
            throw;
        }
    }

    private async Task<RequisitosViajeCompleto?> ExtraerConOllamaVisionAsync(
        string html, string[] screenshotsB64, string origen, string destino, string idioma, CancellationToken ct)
    {
        var endpoint = _config["AI:Ollama:VisionEndpoint"]!;
        var model = _config["AI:Ollama:Model"]!;

        _logger.LogInformation("🤖 Llamando a Ollama Vision API - Modelo: {Model}", model);

        var prompt = ConstruirPromptVision(origen, destino, idioma);

        var request = new OllamaChatRequest
        {
            Model = model,
            Messages = new[]
            {
                new OllamaMessage
                {
                    Role = "system",
                    Content = "Eres un experto en extracción de requisitos de viaje. Analiza la imagen y responde SOLO con JSON válido."
                },
                new OllamaMessage
                {
                    Role = "user",
                    Content = prompt,
                    Images = new[] { screenshotsB64[0] }
                }
            },
            Stream = false,
            Options = new OllamaOptions
            {
                Temperature = _config.GetValue<double>("AI:Ollama:Temperature", 0.1),
                NumPredict = _config.GetValue<int>("AI:Ollama:MaxTokens", 4096)
            }
        };

        try
        {
            var response = await _httpClient.PostAsJsonAsync(endpoint, request, ct);
            response.EnsureSuccessStatusCode();

            var result = await response.Content.ReadFromJsonAsync<OllamaChatResponse>(ct);
            var content = result?.Message?.Content;

            await GuardarRespuestaCrudaAsync("ollama_vision", origen, destino, content);
            
            var resultado = ParsearRespuestaCompleta(content, origen, destino);
            
            if (resultado != null)
            {
                resultado.Markdown = content;
                resultado.MetodoExtraccion = "ia-vision";
                return resultado;
            }
            
            return new RequisitosViajeCompleto
            {
                InfoViaje = new InformacionViaje { Origen = origen, Destino = destino },
                Markdown = content ?? "",
                MetodoExtraccion = "ia-vision",
                Confianza = 0.3
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error en Ollama Vision");
            throw;
        }
    }

    private string ConstruirPromptVision(string origen, string destino, string idioma)
    {
        return $@"Analiza estas DOS imágenes de requisitos de viaje de Sherpa.

CONTEXTO DEL VIAJE:
- Viajero de: {origen}
- Destino: {destino}
- Idioma: {idioma}

IMÁGENES:
- IMAGEN 1 (primera): Tab DEPARTURE - Viaje de IDA ({origen} → {destino})
- IMAGEN 2 (segunda): Tab RETURN - Viaje de VUELTA ({destino} → {origen})

EXTRAER INFORMACIÓN COMPLETA DE AMBAS IMÁGENES EN JSON:

{{
  ""infoViaje"": {{
    ""origen"": ""{origen}"",
    ""destino"": ""{destino}"",
    ""ciudadDestino"": ""nombre de la ciudad""
  }},
  ""departure"": {{
    ""paisSalida"": ""{origen}"",
    ""paisLlegada"": ""{destino}"",
    ""visa"": {{
      ""requerido"": true/false,
      ""tipo"": ""tipo de visa o null"",
      ""descripcion"": ""descripción del requisito"",
      ""duracionMaxima"": ""ej: 180 días"",
      ""costo"": ""costo si aplica"",
      ""tiempoProcesamiento"": ""tiempo estimado"",
      ""disponibleOnline"": true/false,
      ""notas"": ""notas adicionales""
    }},
    ""pasaporte"": {{
      ""validezMinima"": ""ej: 6 meses"",
      ""paginasBlanco"": ""ej: 2 páginas"",
      ""documentosAdicionales"": [
        {{""nombre"": ""nombre documento"", ""obligatorio"": true/false, ""descripcion"": """"}}
      ],
      ""notas"": """"
    }},
    ""salud"": {{
      ""vacunas"": [{{""nombre"": ""nombre vacuna"", ""categoria"": ""required/recommended"", ""detalle"": """"}}],
      ""pruebaCovidRequerida"": true/false,
      ""detalleCovid"": """",
      ""seguroMedicoRequerido"": true/false,
      ""coberturaSeguro"": """",
      ""notas"": """"
    }},
    ""informacionAdicional"": [
      {{""categoria"": ""visa/passport/health/other"", ""titulo"": """", ""contenido"": """", ""requiereAccion"": true/false}}
    ]
  }},
  ""return"": {{
    ""paisSalida"": ""{destino}"",
    ""paisLlegada"": ""{origen}"",
    ""visa"": {{ ...misma estructura que departure... }},
    ""pasaporte"": {{ ...misma estructura... }},
    ""salud"": {{ ...misma estructura... }},
    ""informacionAdicional"": []
  }},
  ""advertenciasGenerales"": [""lista de advertencias importantes""],
  ""enlacesOficiales"": [""URLs de referencia""],
  ""confianza"": 0.0-1.0
}}

INSTRUCCIONES IMPORTANTES:
- Analiza AMBAS imágenes por separado
- IMAGEN 1 = DEPARTURE (ida), IMAGEN 2 = RETURN (vuelta)
- La información puede ser DIFERENTE en cada imagen
- Si una sección no existe en una imagen, usa null o array vacío
- NO inventes datos que no estén visibles
- El campo confianza indica qué tan seguro estás (0.0-1.0)";
    }

    /// <summary>
    /// Extrae requisitos usando OpenRouter API con modelos de visión (screenshots)
    /// </summary>
    private async Task<RequisitosViajeCompleto?> ExtraerConOpenRouterVisionAsync(
        string html, string[] screenshotsB64, string origen, string destino, string idioma, CancellationToken ct)
    {
        var endpoint = _extractionConfig["IaVision:Endpoint"]!;
        var apiKey = _extractionConfig["IaVision:ApiKey"]!;
        var model = _extractionConfig["IaVision:Model"]!;

        _logger.LogInformation("🤖 Llamando a OpenRouter Vision API - Modelo: {Model}", model);
        _logger.LogInformation("📸 Enviando {Count} imágenes", screenshotsB64.Length);

        var prompt = ConstruirPromptVision(origen, destino, idioma);

        // Construir contenido con imágenes para OpenRouter (formato OpenAI compatible)
        var contentList = new List<OpenRouterContentItem>
        {
            new() { Type = "text", Text = prompt }
        };

        // Agregar ambas imágenes
        foreach (var screenshotB64 in screenshotsB64)
        {
            contentList.Add(new OpenRouterContentItem 
            { 
                Type = "image_url", 
                ImageUrl = new OpenRouterImageUrl { Url = $"data:image/png;base64,{screenshotB64}" }
            });
        }

        var request = new OpenRouterRequest
        {
            Model = model,
            Messages = new[]
            {
                new OpenRouterMessage
                {
                    Role = "system",
                    Content = "Eres un experto en extracción de requisitos de viaje. Analiza las imágenes proporcionadas y extrae toda la información relevante. Responde SOLO con JSON válido."
                },
                new OpenRouterMessage
                {
                    Role = "user",
                    Content = contentList.ToArray()  // Array de contenido (texto + imágenes)
                }
            },
            Temperature = _extractionConfig.GetValue<double>("IaVision:Temperature", 0.1),
            MaxTokens = _extractionConfig.GetValue<int>("IaVision:MaxTokens", 4096)
        };

        _httpClient.DefaultRequestHeaders.Authorization = 
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);
        _httpClient.DefaultRequestHeaders.Remove("HTTP-Referer");
        _httpClient.DefaultRequestHeaders.Remove("X-Title");
        _httpClient.DefaultRequestHeaders.TryAddWithoutValidation("HTTP-Referer", "https://sherpascraper.local");
        _httpClient.DefaultRequestHeaders.TryAddWithoutValidation("X-Title", "Sherpa Travel Scraper");

        try
        {
            var response = await _httpClient.PostAsJsonAsync(endpoint, request, ct);
            _logger.LogInformation("📡 OpenRouter Vision respondió con status: {Status}", response.StatusCode);

            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync();
                _logger.LogError("❌ OpenRouter Vision error body: {Error}", errorBody);
            }

            response.EnsureSuccessStatusCode();

            var result = await response.Content.ReadFromJsonAsync<OpenRouterResponse>(ct);
            var contentObj = result?.Choices?.FirstOrDefault()?.Message?.Content;
            var content = contentObj?.ToString() ?? "";

            _logger.LogInformation("📝 Respuesta cruda de OpenRouter Vision ({Length} chars)", content.Length);

            await GuardarRespuestaCrudaAsync("openrouter_vision", origen, destino, content);
            
            // Intentar parsear el JSON de la respuesta
            var resultado = ParsearRespuestaCompleta(content, origen, destino);
            
            if (resultado != null)
            {
                resultado.Markdown = content;
                resultado.MetodoExtraccion = "ia-vision";
                _logger.LogInformation("✅ JSON parseado exitosamente desde respuesta OpenRouter Vision");
                return resultado;
            }
            
            _logger.LogWarning("⚠️ No se pudo parsear JSON, devolviendo markdown sin estructura");
            return new RequisitosViajeCompleto
            {
                InfoViaje = new InformacionViaje { Origen = origen, Destino = destino },
                Markdown = content ?? "",
                MetodoExtraccion = "ia-vision",
                Confianza = 0.3
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Error llamando a OpenRouter Vision API: {Message}", ex.Message);
            throw;
        }
    }

    #endregion

    #region MÉTODO 3: IA HTML (TEXTO)

    /// <summary>
    /// Extrae requisitos enviando el HTML a un modelo de IA de texto
    /// </summary>
    private async Task<RequisitosViajeCompleto?> ExtraerConIaHtmlAsync(
        string html, string origen, string destino, string idioma, CancellationToken ct)
    {
        _logger.LogInformation("Usando extracción IA HTML (texto)");

        var provider = _extractionConfig["IaHtml:Provider"]?.ToLower() ?? "openrouter";
        
        try
        {
            var resultado = provider switch
            {
                "kimi" => await ExtraerConKimiHtmlAsync(html, origen, destino, idioma, ct),
                "openrouter" => await ExtraerConOpenRouterHtmlAsync(html, origen, destino, idioma, ct),
                _ => throw new NotSupportedException($"Proveedor IA HTML no soportado: {provider}")
            };

            if (resultado != null)
            {
                resultado.MetodoExtraccion = "ia-html";
            }

            return resultado;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error en extracción IA HTML");
            return null;
        }
    }

    /// <summary>
    /// Extrae requisitos usando Kimi con HTML como entrada de texto
    /// </summary>
    public async Task<RequisitosViajeCompleto?> ExtraerConKimiHtmlAsync(
        string html, string origen, string destino, string idioma, CancellationToken ct = default)
    {
        var endpoint = _extractionConfig["IaHtml:Endpoint"]!;
        var apiKey = _extractionConfig["IaHtml:ApiKey"]!;
        var model = _extractionConfig["IaHtml:Model"]!;

        _logger.LogInformation("🤖 Llamando a Kimi HTML API - Modelo: {Model}", model);

        // Truncar HTML si es necesario
        var maxHtmlLength = _extractionConfig.GetValue<int>("IaHtml:MaxHtmlLength", 8000);
        var htmlTruncado = html.Length > maxHtmlLength ? html[..maxHtmlLength] : html;
        
        _logger.LogInformation("📄 Enviando HTML: {Length} caracteres (truncado de {Original})", 
            htmlTruncado.Length, html.Length);

        var prompt = ConstruirPromptHtml(origen, destino, idioma, htmlTruncado);

        var request = new KimiRequest
        {
            Model = model,
            Messages = new[]
            {
                new KimiMessage
                {
                    Role = "system",
                    Content = "Eres un experto en extracción de requisitos de viaje. Analiza el HTML proporcionado y extrae toda la información relevante. Responde SOLO con JSON válido."
                },
                new KimiMessage
                {
                    Role = "user",
                    Content = prompt
                }
            },
            ResponseFormat = new { type = "json_object" },
            Temperature = _extractionConfig.GetValue<double>("IaHtml:Temperature", 0.1),
            MaxTokens = _extractionConfig.GetValue<int>("IaHtml:MaxTokens", 4096)
        };

        _httpClient.DefaultRequestHeaders.Authorization = 
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);

        try
        {
            var response = await _httpClient.PostAsJsonAsync(endpoint, request, ct);
            _logger.LogInformation("📡 Kimi HTML respondió con status: {Status}", response.StatusCode);

            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync();
                _logger.LogError("❌ Kimi HTML error body: {Error}", errorBody);
            }

            response.EnsureSuccessStatusCode();

            var result = await response.Content.ReadFromJsonAsync<KimiResponse>(ct);
            var content = result?.Choices?.FirstOrDefault()?.Message?.Content as string;

            _logger.LogInformation("📝 Respuesta cruda de Kimi HTML ({Length} chars)", content?.Length ?? 0);

            await GuardarRespuestaCrudaAsync("kimi_html", origen, destino, content);
            
            // Intentar parsear el JSON de la respuesta
            var resultado = ParsearRespuestaCompleta(content, origen, destino);
            
            if (resultado != null)
            {
                resultado.Markdown = content;
                resultado.MetodoExtraccion = "ia-html";
                _logger.LogInformation("✅ JSON parseado exitosamente desde respuesta Kimi HTML - Confianza: {Confianza:F2}", 
                    resultado.Confianza);
                return resultado;
            }
            
            _logger.LogWarning("⚠️ No se pudo parsear JSON, devolviendo markdown sin estructura");
            return new RequisitosViajeCompleto
            {
                InfoViaje = new InformacionViaje { Origen = origen, Destino = destino },
                Markdown = content ?? "",
                MetodoExtraccion = "ia-html",
                Confianza = 0.3
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Error llamando a Kimi HTML API: {Message}", ex.Message);
            throw;
        }
    }

    private string ConstruirPromptHtml(string origen, string destino, string idioma, string html)
    {
        return $@"Analiza el siguiente HTML de una página de requisitos de viaje de Sherpa.

CONTEXTO DEL VIAJE:
- Viajero de: {origen}
- Destino: {destino}
- Idioma: {idioma}

HTML DE LA PÁGINA:
```html
{html}
```

EXTRAER INFORMACIÓN COMPLETA EN FORMATO JSON:

{{
  ""infoViaje"": {{
    ""origen"": ""{origen}"",
    ""destino"": ""{destino}"",
    ""ciudadDestino"": ""nombre de la ciudad si está disponible""
  }},
  ""departure"": {{
    ""paisSalida"": ""{origen}"",
    ""paisLlegada"": ""{destino}"",
    ""visa"": {{
      ""requerido"": true/false,
      ""tipo"": ""tipo de visa o null"",
      ""descripcion"": ""descripción detallada del requisito de visa"",
      ""duracionMaxima"": ""ej: 180 días, 90 días, etc."",
      ""costo"": ""costo aproximado si aplica"",
      ""tiempoProcesamiento"": ""tiempo estimado de procesamiento"",
      ""disponibleOnline"": true/false/null,
      ""notas"": ""notas adicionales sobre visa""
    }},
    ""pasaporte"": {{
      ""validezMinima"": ""ej: 6 meses, 3 meses, etc."",
      ""paginasBlanco"": ""número de páginas requeridas"",
      ""biometricoRequerido"": true/false/null,
      ""documentosAdicionales"": [
        {{""nombre"": ""nombre del documento"", ""descripcion"": """", ""obligatorio"": true/false}}
      ],
      ""notas"": ""notas adicionales sobre pasaporte""
    }},
    ""salud"": {{
      ""vacunas"": [
        {{""nombre"": ""nombre de la vacuna"", ""categoria"": ""required/recommended/optional"", ""detalle"": """"}}
      ],
      ""pruebaCovidRequerida"": true/false/null,
      ""detalleCovid"": ""detalles sobre requisitos COVID"",
      ""seguroMedicoRequerido"": true/false/null,
      ""coberturaSeguro"": ""cobertura mínima requerida"",
      ""riesgos"": [
        {{""nombre"": ""nombre del riesgo"", ""nivel"": ""high/moderate/low"", ""descripcion"": """", ""recomendacion"": """"}}
      ],
      ""notas"": ""notas adicionales sobre salud""
    }},
    ""informacionAdicional"": [
      {{""categoria"": ""visa/passport/health/other"", ""titulo"": """", ""contenido"": """", ""requiereAccion"": true/false, ""enlace"": """"}}
    ]
  }},
  ""return"": {{
    ""paisSalida"": ""{destino}"",
    ""paisLlegada"": ""{origen}"",
    ""visa"": {{ ...misma estructura que departure... }},
    ""pasaporte"": {{ ...misma estructura... }},
    ""salud"": {{ ...misma estructura... }},
    ""informacionAdicional"": []
  }},
  ""advertenciasGenerales"": [""lista de advertencias importantes""],
  ""enlacesOficiales"": [""URLs de referencia encontradas en el HTML""],
  ""confianza"": 0.0-1.0
}}

INSTRUCCIONES IMPORTANTES:
1. Analiza TODO el HTML proporcionado
2. Busca información tanto para DEPARTURE (ida) como RETURN (vuelta)
3. Extrae información específica de visa, pasaporte y salud
4. Si una sección no existe, usa null o array vacío []
5. NO inventes datos que no estén en el HTML
6. El campo 'confianza' indica qué tan seguro estás de la extracción (0.0-1.0)
7. Presta atención a atributos data-testid, clases CSS y estructura del DOM
8. Extrae texto visible y contenido de meta tags si es relevante";
    }

    /// <summary>
    /// Extrae requisitos usando OpenRouter API con HTML como entrada
    /// </summary>
    public async Task<RequisitosViajeCompleto?> ExtraerConOpenRouterHtmlAsync(
        string html, string origen, string destino, string idioma, CancellationToken ct = default)
    {
        var endpoint = _extractionConfig["IaHtml:Endpoint"]!;
        var apiKey = _extractionConfig["IaHtml:ApiKey"]!;
        var model = _extractionConfig["IaHtml:Model"]!;

        _logger.LogInformation("🤖 Llamando a OpenRouter API - Modelo: {Model}", model);

        // Truncar HTML si es necesario
        var maxHtmlLength = _extractionConfig.GetValue<int>("IaHtml:MaxHtmlLength", 8000);
        var htmlTruncado = html.Length > maxHtmlLength ? html[..maxHtmlLength] : html;
        
        _logger.LogInformation("📄 Enviando HTML: {Length} caracteres (truncado de {Original})", 
            htmlTruncado.Length, html.Length);

        var prompt = ConstruirPromptHtml(origen, destino, idioma, htmlTruncado);

        var request = new OpenRouterRequest
        {
            Model = model,
            Messages = new[]
            {
                new OpenRouterMessage
                {
                    Role = "system",
                    Content = "Eres un experto en extracción de requisitos de viaje. Analiza el HTML proporcionado y extrae toda la información relevante. Responde SOLO con JSON válido."
                },
                new OpenRouterMessage
                {
                    Role = "user",
                    Content = prompt
                }
            },
            Temperature = _extractionConfig.GetValue<double>("IaHtml:Temperature", 0.1),
            MaxTokens = _extractionConfig.GetValue<int>("IaHtml:MaxTokens", 4096)
        };

        _httpClient.DefaultRequestHeaders.Authorization = 
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);
        _httpClient.DefaultRequestHeaders.Remove("HTTP-Referer");
        _httpClient.DefaultRequestHeaders.Remove("X-Title");
        _httpClient.DefaultRequestHeaders.TryAddWithoutValidation("HTTP-Referer", "https://sherpascraper.local");
        _httpClient.DefaultRequestHeaders.TryAddWithoutValidation("X-Title", "Sherpa Travel Scraper");

        try
        {
            var response = await _httpClient.PostAsJsonAsync(endpoint, request, ct);
            _logger.LogInformation("📡 OpenRouter respondió con status: {Status}", response.StatusCode);

            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync();
                _logger.LogError("❌ OpenRouter error body: {Error}", errorBody);
            }

            response.EnsureSuccessStatusCode();

            var result = await response.Content.ReadFromJsonAsync<OpenRouterResponse>(ct);
            var content = result?.Choices?.FirstOrDefault()?.Message?.Content as string;

            _logger.LogInformation("📝 Respuesta cruda de OpenRouter ({Length} chars)", content?.Length ?? 0);

            await GuardarRespuestaCrudaAsync("openrouter_html", origen, destino, content);
            
            // Intentar parsear el JSON de la respuesta
            var resultado = ParsearRespuestaCompleta(content, origen, destino);
            
            if (resultado != null)
            {
                resultado.Markdown = content ?? "";
                resultado.MetodoExtraccion = "ia-html";
                _logger.LogInformation("✅ JSON parseado exitosamente desde respuesta OpenRouter - Confianza: {Confianza:F2}", 
                    resultado.Confianza);
                return resultado;
            }
            
            _logger.LogWarning("⚠️ No se pudo parsear JSON, devolviendo markdown sin estructura");
            return new RequisitosViajeCompleto
            {
                InfoViaje = new InformacionViaje { Origen = origen, Destino = destino },
                Markdown = content ?? "",
                MetodoExtraccion = "ia-html",
                Confianza = 0.3
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Error llamando a OpenRouter API: {Message}", ex.Message);
            throw;
        }
    }

    #endregion

    #region UTILIDADES

    /// <summary>
    /// Extrae JSON de la respuesta, ya sea directo o envuelto en markdown
    /// </summary>
    private string? ExtraerJsonDeRespuesta(string? content)
    {
        if (string.IsNullOrEmpty(content))
            return null;

        // Intentar 1: Buscar JSON en bloque markdown ```json
        var jsonMatch = System.Text.RegularExpressions.Regex.Match(
            content, @"```json\s*(.*?)\s*```", System.Text.RegularExpressions.RegexOptions.Singleline);
        if (jsonMatch.Success)
            return jsonMatch.Groups[1].Value.Trim();

        // Intentar 2: Buscar JSON en bloque ``` cualquiera
        var codeMatch = System.Text.RegularExpressions.Regex.Match(
            content, @"```\s*(.*?)\s*```", System.Text.RegularExpressions.RegexOptions.Singleline);
        if (codeMatch.Success)
            return codeMatch.Groups[1].Value.Trim();

        // Intentar 3: Buscar primer { y último }
        var startIdx = content.IndexOf('{');
        var endIdx = content.LastIndexOf('}');
        if (startIdx >= 0 && endIdx > startIdx)
            return content.Substring(startIdx, endIdx - startIdx + 1);

        // Intentar 4: Limpiar prefijos comunes
        var cleaned = content.Trim();
        if (cleaned.StartsWith("json"))
            cleaned = cleaned[4..].Trim();

        return cleaned;
    }

    private RequisitosViajeCompleto? ParsearRespuestaCompleta(string? content, string origen, string destino)
    {
        if (string.IsNullOrEmpty(content))
            return null;

        try
        {
            var json = ExtraerJsonDeRespuesta(content);
            
            if (string.IsNullOrEmpty(json))
            {
                _logger.LogWarning("No se pudo extraer JSON de la respuesta");
                _logger.LogWarning("Contenido original: {Content}", content[..Math.Min(200, content.Length)]);
                return null;
            }

            // Limpiar caracteres BOM si existen
            if (json.Length > 0 && json[0] == '\uFEFF')
                json = json[1..];
            
            // Asegurar UTF-8 válido - reemplazar caracteres inválidos
            var utf8Bytes = System.Text.Encoding.UTF8.GetBytes(json);
            json = System.Text.Encoding.UTF8.GetString(utf8Bytes);

            _logger.LogDebug("JSON extraído para parseo: {Json}", json[..Math.Min(200, json.Length)]);

            var resultado = JsonSerializer.Deserialize<RequisitosViajeCompleto>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                AllowTrailingCommas = true,
                ReadCommentHandling = JsonCommentHandling.Skip
            });

            if (resultado != null)
            {
                resultado.InfoViaje ??= new InformacionViaje();
                resultado.InfoViaje.Origen = origen;
                resultado.InfoViaje.Destino = destino;
                resultado.FechaExtraccion = DateTime.Now;
                _logger.LogInformation("✅ JSON parseado exitosamente - Confianza: {Confianza:F2}", resultado.Confianza);
            }

            return resultado;
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Error JSON parseando respuesta. Path: {Path}, LineNumber: {Line}", 
                ex.Path, ex.LineNumber);
            _logger.LogWarning("JSON problemático: {Json}", content?[..Math.Min(500, content?.Length ?? 0)]);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error general parseando respuesta: {Message}", ex.Message);
            _logger.LogWarning("Contenido: {Content}", content?[..Math.Min(200, content?.Length ?? 0)]);
            return null;
        }
    }

    private async Task GuardarRespuestaCrudaAsync(string provider, string origen, string destino, string? content)
    {
        if (!_config.GetValue<bool>("AI:SaveRawResponses", false))
            return;

        try
        {
            var path = _config["AI:RawResponsesPath"] ?? "./ai-responses/";
            Directory.CreateDirectory(path);
            
            var filename = $"{provider}_{origen}_{destino}_{DateTime.Now:yyyyMMdd_HHmmss}.json";
            var filepath = Path.Combine(path, filename);
            
            await File.WriteAllTextAsync(filepath, content ?? "(null)");
            _logger.LogDebug("Respuesta IA guardada: {File}", filename);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error guardando respuesta cruda");
        }
    }

    #endregion

    #region MODELOS DE REQUEST/RESPONSE

    // Ollama Models
    private class OllamaChatRequest
    {
        [JsonPropertyName("model")]
        public string Model { get; set; } = "";
        
        [JsonPropertyName("messages")]
        public OllamaMessage[] Messages { get; set; } = Array.Empty<OllamaMessage>();
        
        [JsonPropertyName("stream")]
        public bool Stream { get; set; }
        
        [JsonPropertyName("options")]
        public OllamaOptions Options { get; set; } = new();
    }

    private class OllamaMessage
    {
        [JsonPropertyName("role")]
        public string Role { get; set; } = "";
        
        [JsonPropertyName("content")]
        public string Content { get; set; } = "";
        
        [JsonPropertyName("images")]
        public string[]? Images { get; set; }
    }

    private class OllamaOptions
    {
        [JsonPropertyName("temperature")]
        public double Temperature { get; set; }
        
        [JsonPropertyName("num_predict")]
        public int NumPredict { get; set; }
    }

    private class OllamaChatResponse
    {
        [JsonPropertyName("message")]
        public OllamaMessage? Message { get; set; }
    }

    // Kimi Models
    private class KimiRequest
    {
        [JsonPropertyName("model")]
        public string Model { get; set; } = "";
        
        [JsonPropertyName("messages")]
        public KimiMessage[] Messages { get; set; } = Array.Empty<KimiMessage>();
        
        [JsonPropertyName("response_format")]
        public object? ResponseFormat { get; set; }
        
        [JsonPropertyName("temperature")]
        public double Temperature { get; set; }
        
        [JsonPropertyName("max_tokens")]
        public int MaxTokens { get; set; }
    }

    private class KimiMessage
    {
        [JsonPropertyName("role")]
        public string Role { get; set; } = "";
        
        [JsonPropertyName("content")]
        public object Content { get; set; } = "";
    }

    private class KimiResponse
    {
        [JsonPropertyName("choices")]
        public KimiChoice[]? Choices { get; set; }
    }

    private class KimiChoice
    {
        [JsonPropertyName("message")]
        public KimiMessage? Message { get; set; }
    }

    // OpenRouter Models
    private class OpenRouterRequest
    {
        [JsonPropertyName("model")]
        public string Model { get; set; } = "";
        
        [JsonPropertyName("messages")]
        public OpenRouterMessage[] Messages { get; set; } = Array.Empty<OpenRouterMessage>();
        
        [JsonPropertyName("temperature")]
        public double Temperature { get; set; }
        
        [JsonPropertyName("max_tokens")]
        public int MaxTokens { get; set; }
    }

    private class OpenRouterMessage
    {
        [JsonPropertyName("role")]
        public string Role { get; set; } = "";
        
        [JsonPropertyName("content")]
        public object Content { get; set; } = "";  // Puede ser string o array de contenido
    }

    private class OpenRouterContentItem
    {
        [JsonPropertyName("type")]
        public string Type { get; set; } = "";  // "text" o "image_url"
        
        [JsonPropertyName("text")]
        public string? Text { get; set; }  // Para type = "text"
        
        [JsonPropertyName("image_url")]
        public OpenRouterImageUrl? ImageUrl { get; set; }  // Para type = "image_url"
    }

    private class OpenRouterImageUrl
    {
        [JsonPropertyName("url")]
        public string Url { get; set; } = "";
    }

    private class OpenRouterResponse
    {
        [JsonPropertyName("choices")]
        public OpenRouterChoice[]? Choices { get; set; }
    }

    private class OpenRouterChoice
    {
        [JsonPropertyName("message")]
        public OpenRouterMessage? Message { get; set; }
    }

    #endregion
}

/// <summary>
/// Modelo de resultado de extracción (legacy)
/// </summary>
public class RequisitosExtraccion
{
    public string RequisitosDestino { get; set; } = "";
    public RequisitoVisado RequisitosVisado { get; set; } = new();
    public RequisitoPasaporte Pasaportes { get; set; } = new();
    public RequisitoSanitario Sanitarios { get; set; } = new();
    public List<string> DocumentosAdicionales { get; set; } = new();
    public List<string> Advertencias { get; set; } = new();
    public List<string> EnlacesOficiales { get; set; } = new();
    public string Notas { get; set; } = "";
    public double Confianza { get; set; }
}

public class RequisitoVisado
{
    public bool? Requerido { get; set; }
    public string? Tipo { get; set; }
    public string? Duracion { get; set; }
    public string? Proceso { get; set; }
    public string? Costo { get; set; }
    public string? TiempoProcesamiento { get; set; }
}

public class RequisitoPasaporteLegacy
{
    public string? ValidezMinima { get; set; }
    public string? PaginasBlanco { get; set; }
    public List<string> OtrosRequisitos { get; set; } = new();
}

public class RequisitoSanitario
{
    public List<string> VacunasRequeridas { get; set; } = new();
    public bool? PruebasCovid { get; set; }
    public bool? SeguroMedico { get; set; }
    public string? Otros { get; set; }
}
