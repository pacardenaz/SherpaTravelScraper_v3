using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SherpaTravelScraper.Services;
using SherpaTravelScraper.Utils;

// Cargar variables de entorno desde .env antes de cualquier otra cosa
EnvLoader.Load(".env");

Console.WriteLine(@"
╔══════════════════════════════════════════════════════════════╗
║        SHERPA TRAVEL SCRAPER - .NET 10                       ║
║        Extracción de Requisitos de Viaje                     ║
╚══════════════════════════════════════════════════════════════╝
");

// Configurar Host
var host = Host.CreateDefaultBuilder(args)
    .ConfigureAppConfiguration((context, config) =>
    {
        config.SetBasePath(Directory.GetCurrentDirectory());
        config.AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);
        config.AddEnvironmentVariables();
        config.AddCommandLine(args);
    })
    .ConfigureLogging((context, logging) =>
    {
        logging.ClearProviders();
        logging.AddConsole();
        logging.AddConfiguration(context.Configuration.GetSection("Logging"));
    })
    .ConfigureServices((context, services) =>
    {
        // Configuración
        services.AddSingleton(context.Configuration);
        
        // Stealth
        services.AddSingleton<StealthConfig>();
        
        // HttpClient para servicios de IA
        services.AddHttpClient();
        
        // Servicios
        services.AddScoped<TravelRepository>();
        services.AddScoped<CombinacionGenerator>();
        services.AddScoped<AiExtractionService>();
        services.AddScoped<SherpaScraperService>();
        services.AddScoped<TravelScrapingOrchestrator>();
    })
    .Build();

// Ejecutar
await using var scope = host.Services.CreateAsyncScope();
var orchestrator = scope.ServiceProvider.GetRequiredService<TravelScrapingOrchestrator>();

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (sender, e) =>
{
    e.Cancel = true;
    Console.WriteLine("\nSeñal de cancelación recibida. Deteniendo...");
    cts.Cancel();
};

try
{
    await orchestrator.EjecutarAsync(cts.Token);
    Console.WriteLine("\n✅ Proceso completado exitosamente");
    return 0;
}
catch (OperationCanceledException)
{
    Console.WriteLine("\n⚠️ Proceso cancelado por el usuario");
    return 1;
}
catch (Exception ex)
{
    Console.WriteLine($"\n❌ Error: {ex.Message}");
    Console.WriteLine(ex.StackTrace);
    return 1;
}
