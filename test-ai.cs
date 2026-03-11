using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using SherpaTravelScraper.Services;

// Test simple de AiExtractionService
var config = new ConfigurationBuilder()
    .AddJsonFile("appsettings.json")
    .Build();

using var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
var logger = loggerFactory.CreateLogger<AiExtractionService>();

var httpClient = new HttpClient();
var aiService = new AiExtractionService(config, logger, httpClient);

// Crear imagen de prueba (1x1 pixel PNG transparente)
var testImage = new byte[] {
    0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x0D,
    0x49, 0x48, 0x44, 0x52, 0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01,
    0x08, 0x06, 0x00, 0x00, 0x00, 0x1F, 0x15, 0xC4, 0x89, 0x00, 0x00, 0x00,
    0x0A, 0x49, 0x44, 0x41, 0x54, 0x78, 0x9C, 0x63, 0x60, 0x00, 0x00, 0x00,
    0x02, 0x00, 0x01, 0xE2, 0x21, 0xBC, 0x33, 0x00, 0x00, 0x00, 0x00, 0x49,
    0x45, 0x4E, 0x44, 0xAE, 0x42, 0x60, 0x82
};

Console.WriteLine("Testing AI extraction...");
Console.WriteLine($"AI Enabled: {config.GetValue<bool>(\"AI:Enabled\", false)}");
Console.WriteLine($"Provider: {config[\"AI:Provider\"]}");
Console.WriteLine($"Ollama Endpoint: {config[\"AI:Ollama:VisionEndpoint\"]}");

try
{
    var result = await aiService.ExtraerRequisitosCompletosAsync(
        "<html><body>Test</body></html>",
        new[] { testImage, testImage },
        "ARG",
        "CAN",
        "ES"
    );
    
    if (result != null)
    {
        Console.WriteLine($"Success! Confidence: {result.Confianza}");
        Console.WriteLine($"Departure Visa Required: {result.Departure?.Visa?.Requerido}");
    }
    else
    {
        Console.WriteLine("Result is null - AI extraction failed");
    }
}
catch (Exception ex)
{
    Console.WriteLine($"Error: {ex.Message}");
    Console.WriteLine($"Stack: {ex.StackTrace}");
}
