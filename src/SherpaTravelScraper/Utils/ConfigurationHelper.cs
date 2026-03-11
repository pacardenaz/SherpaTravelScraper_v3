using Microsoft.Extensions.Configuration;

namespace SherpaTravelScraper.Utils;

/// <summary>
/// Helper para configurar la aplicación usando variables de entorno
/// </summary>
public static class ConfigurationHelper
{
    /// <summary>
    /// Aplica variables de entorno a la configuración
    /// Las variables de entorno sobreescriben los valores de appsettings.json
    /// </summary>
    public static void ApplyEnvironmentVariables(IConfiguration configuration)
    {
        var connectionString = EnvLoader.Get("DB_CONNECTION_STRING");
        if (!string.IsNullOrEmpty(connectionString))
        {
            // La variable de entorno ya está configurada por EnvLoader
            // El AddEnvironmentVariables en Program.cs la usará automáticamente
        }
        
        // Las variables de entorno con prefijo apropiado se mapean automáticamente
        // Ejemplo: ConnectionStrings__DefaultConnection mapea a ConnectionStrings:DefaultConnection
    }
    
    /// <summary>
    /// Obtiene la connection string desde variables de entorno o configuración
    /// </summary>
    public static string GetConnectionString()
    {
        // Prioridad 1: Variable de entorno completa
        var fullConnString = EnvLoader.Get("ConnectionStrings__DefaultConnection");
        if (!string.IsNullOrEmpty(fullConnString))
            return fullConnString;
        
        // Prioridad 2: Construir desde componentes
        var server = EnvLoader.Get("DB_SERVER", "localhost");
        var database = EnvLoader.Get("DB_DATABASE", "TravelRequirementsDB");
        var user = EnvLoader.Get("DB_USER", "sa");
        var password = EnvLoader.Get("DB_PASSWORD", "");
        var trustCert = EnvLoader.Get("DB_TRUST_CERTIFICATE", "True");
        
        if (string.IsNullOrEmpty(password))
        {
            throw new InvalidOperationException(
                "DB_PASSWORD no está configurado. " +
                "Por favor configura las variables de entorno usando scripts/init.sh o el archivo .env");
        }
        
        return $"Server={server};Database={database};User Id={user};Password={password};TrustServerCertificate={trustCert};";
    }
    
    /// <summary>
    /// Obtiene la API key de OpenRouter
    /// </summary>
    public static string GetOpenRouterApiKey()
    {
        var key = EnvLoader.Get("OPENROUTER_API_KEY");
        if (string.IsNullOrEmpty(key))
        {
            throw new InvalidOperationException(
                "OPENROUTER_API_KEY no está configurado. " +
                "Por favor configura las variables de entorno usando scripts/init.sh o el archivo .env");
        }
        return key;
    }
    
    /// <summary>
    /// Obtiene la API key de Kimi
    /// </summary>
    public static string GetKimiApiKey()
    {
        return EnvLoader.Get("KIMI_API_KEY", "");
    }
}
