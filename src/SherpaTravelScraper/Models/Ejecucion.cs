namespace SherpaTravelScraper.Models;

/// <summary>
/// Representa una ejecución del proceso de scraping
/// </summary>
public class Ejecucion
{
    public int Id { get; set; }
    public DateTime FechaInicio { get; set; }
    public DateTime? FechaFin { get; set; }
    public string Estado { get; set; } = "P"; // P=Procesando, C=Completado, E=Error, B=Bloqueado
    public int TotalCombinaciones { get; set; }
    public int CombinacionesOk { get; set; }
    public int CombinacionesFallidas { get; set; }
    public string? ProxyUsado { get; set; }

    public double PorcentajeCompletado => TotalCombinaciones > 0 
        ? (CombinacionesOk + CombinacionesFallidas) * 100.0 / TotalCombinaciones 
        : 0;
}
