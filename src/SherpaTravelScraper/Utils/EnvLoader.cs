using System;
using System.IO;
using System.Linq;

namespace SherpaTravelScraper.Utils;

/// <summary>
/// Helper para cargar variables de entorno desde archivo .env
/// </summary>
public static class EnvLoader
{
    /// <summary>
    /// Carga variables de entorno desde un archivo .env
    /// </summary>
    public static void Load(string filePath = ".env")
    {
        if (!File.Exists(filePath))
        {
            Console.WriteLine($"⚠️  Archivo {filePath} no encontrado. Usando variables de entorno del sistema.");
            return;
        }

        Console.WriteLine($"📁 Cargando variables de entorno desde {filePath}");
        
        var lines = File.ReadAllLines(filePath);
        var count = 0;
        
        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            
            // Ignorar líneas vacías y comentarios
            if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith("#")) 
                continue;
            
            // Parsear KEY=VALUE
            var separatorIndex = trimmed.IndexOf('=');
            if (separatorIndex <= 0) 
                continue;
            
            var key = trimmed.Substring(0, separatorIndex).Trim();
            var value = trimmed.Substring(separatorIndex + 1).Trim();
            
            // Remover comillas si existen
            if ((value.StartsWith("\"") && value.EndsWith("\"")) ||
                (value.StartsWith("'") && value.EndsWith("'")))
            {
                value = value.Substring(1, value.Length - 2);
            }
            
            Environment.SetEnvironmentVariable(key, value);
            count++;
        }
        
        Console.WriteLine($"✅ {count} variables cargadas");
    }
    
    /// <summary>
    /// Obtiene una variable de entorno con un valor por defecto
    /// </summary>
    public static string Get(string key, string defaultValue = "")
    {
        return Environment.GetEnvironmentVariable(key) ?? defaultValue;
    }
    
    /// <summary>
    /// Verifica si una variable de entorno está configurada
    /// </summary>
    public static bool Has(string key)
    {
        return !string.IsNullOrEmpty(Environment.GetEnvironmentVariable(key));
    }
}
