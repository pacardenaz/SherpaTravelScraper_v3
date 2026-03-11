using Microsoft.Playwright;
using SherpaTravelScraper.Services;
using SherpaTravelScraper.Models;
using SherpaTravelScraper.Utils;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace SherpaTravelScraper.Tests;

/// <summary>
/// Prueba de integración para extracción usando método tradicional (fallback)
/// </summary>
public class AiExtractionTest
{
    public static async Task Main(string[] args)
    {
        Console.WriteLine("=== Prueba de Extracción Tradicional (Sin IA) ===\n");
        
        // Configuración - IA deshabilitada
        var config = new ConfigurationBuilder()
            .AddJsonFile("appsettings.json", optional: true)
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AI:Provider"] = "none",
                ["AI:Enabled"] = "false",
                ["Playwright:Headless"] = "true"
            })
            .Build();

        using var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
        var logger = loggerFactory.CreateLogger<AiExtractionTest>();

        // Parámetros de prueba
        var origen = args.Length > 0 ? args[0] : "USA";
        var destino = args.Length > 1 ? args[1] : "CAN";
        var idioma = args.Length > 2 ? args[2] : "EN";

        Console.WriteLine($"Probando combinación: {origen} -> {destino} (idioma: {idioma})");
        Console.WriteLine($"Proveedor IA: {config["AI:Provider"]} (deshabilitada)");
        Console.WriteLine($"Método: Extracción tradicional con JavaScript\n");

        try
        {
            // Inicializar Playwright
            Console.WriteLine("1. Inicializando Playwright...");
            using var playwright = await Playwright.CreateAsync();
            await using var browser = await playwright.Chromium.LaunchAsync(new()
            {
                Headless = true,
                Args = new[] { "--no-sandbox", "--disable-setuid-sandbox" }
            });

            // Construir URL de Sherpa
            var departureDate = DateTime.Now.AddDays(15).ToString("yyyy-MM-dd");
            var returnDate = DateTime.Now.AddDays(22).ToString("yyyy-MM-dd");
            var url = $"https://apply.joinsherpa.com/travel-restrictions/{destino}?language={idioma}&nationality={origen}&originCountry={origen}&departureDate={departureDate}&returnDate={returnDate}&travelPurposes=TOURISM&tripType=roundTrip&fullyVaccinated=true&affiliateId=sherpa";

            Console.WriteLine($"2. Navegando a: {url}");
            var page = await browser.NewPageAsync();
            
            var response = await page.GotoAsync(url, new()
            {
                WaitUntil = WaitUntilState.NetworkIdle,
                Timeout = 30000
            });

            if (response?.Status != 200)
            {
                Console.WriteLine($"   ❌ Error: HTTP {response?.Status}");
                return;
            }

            Console.WriteLine($"   ✅ Página cargada: {await page.TitleAsync()}\n");

            // Esperar carga
            Console.WriteLine("3. Esperando carga de contenido (3s)...");
            await Task.Delay(3000);

            // Obtener HTML
            Console.WriteLine("4. Obteniendo HTML...");
            var html = await page.ContentAsync();
            Console.WriteLine($"   ✅ HTML: {html.Length / 1024} KB\n");

            // Probar extracción TRADICIONAL (sin IA)
            Console.WriteLine("5. Extrayendo datos con JavaScript tradicional...");
            
            // Crear instancia del servicio de scraping
            var stealthConfig = new StealthConfig(config);
            var scraper = new SherpaScraperService(
                loggerFactory.CreateLogger<SherpaScraperService>(),
                stealthConfig,
                config,
                null  // Sin servicio de IA
            );
            
            // Usar reflexión para llamar al método privado ExtraerDatosTradicionalesAsync
            var method = typeof(SherpaScraperService).GetMethod("ExtraerDatosTradicionalesAsync", 
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            
            if (method == null)
            {
                Console.WriteLine("   ❌ No se encontró el método ExtraerDatosTradicionalesAsync");
                return;
            }
            
            var startTime = DateTime.Now;
            var resultadoTask = method.Invoke(scraper, new object[] { page, html, url }) as Task<ResultadoScraping>;
            var resultado = await resultadoTask!;
            var duration = DateTime.Now - startTime;

            Console.WriteLine($"   ✅ Extracción completada en {duration.TotalSeconds:F1}s\n");

            // Mostrar resultados
            Console.WriteLine("6. RESULTADO DE LA EXTRACCIÓN:");
            Console.WriteLine("========================================");
            
            if (!resultado.Exitoso)
            {
                Console.WriteLine($"❌ Extracción fallida: {resultado.MensajeError}");
            }
            else
            {
                Console.WriteLine($"Exitoso: {resultado.Exitoso}");
                Console.WriteLine($"\n--- REQUISITOS DESTINO (primeros 500 chars) ---");
                Console.WriteLine(resultado.RequisitosDestino?.Substring(0, Math.Min(500, resultado.RequisitosDestino?.Length ?? 0)) ?? "(vacío)");
                
                Console.WriteLine($"\n--- REQUISITOS VISADO ---");
                Console.WriteLine(resultado.RequisitosVisado?.Substring(0, Math.Min(500, resultado.RequisitosVisado?.Length ?? 0)) ?? "(vacío)");
                
                Console.WriteLine($"\n--- PASAPORTES/DOCUMENTOS ---");
                Console.WriteLine(resultado.PasaportesDocumentos?.Substring(0, Math.Min(500, resultado.PasaportesDocumentos?.Length ?? 0)) ?? "(vacío)");
                
                Console.WriteLine($"\n--- SANITARIOS ---");
                Console.WriteLine(resultado.Sanitarios?.Substring(0, Math.Min(500, resultado.Sanitarios?.Length ?? 0)) ?? "(vacío)");
                
                Console.WriteLine($"\n--- DATOS JSON (primeros 1000 chars) ---");
                Console.WriteLine(resultado.Datos?.Substring(0, Math.Min(1000, resultado.Datos?.Length ?? 0)) ?? "(vacío)");
                
                // Verificar si los datos son válidos
                bool hasValidData = !string.IsNullOrEmpty(resultado.RequisitosDestino) && resultado.RequisitosDestino.Length > 50;
                
                if (hasValidData)
                {
                    Console.WriteLine("\n✅ ÉXITO: Datos extraídos correctamente");
                }
                else
                {
                    Console.WriteLine("\n⚠️ ADVERTENCIA: Datos extraídos son muy cortos o vacíos");
                }
            }
            
            Console.WriteLine("========================================\n");
            Console.WriteLine("=== Prueba completada ===");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\n❌ Error: {ex.Message}");
            Console.WriteLine(ex.StackTrace);
        }
    }
}
