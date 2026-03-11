namespace SherpaTravelScraper.Models;

/// <summary>
/// Representa una combinación origen-destino a procesar
/// </summary>
public class Combinacion
{
    public int Id { get; set; }
    public int EjecucionId { get; set; }
    public string Origen { get; set; } = string.Empty;
    public string Destino { get; set; } = string.Empty;
    public string Idioma { get; set; } = "EN-US";
    public string TipoNacionalidad { get; set; } = "AMBOS"; // ORIGEN, DESTINO, AMBOS
    public string Estado { get; set; } = "P"; // P=Pendiente, E=EnProceso, C=Completada, F=Fallida, B=Bloqueada
    public int Reintentos { get; set; } = 0;
    public DateTime? FechaProcesamiento { get; set; }
    public string? MensajeError { get; set; }

    public string ClaveUnica => $"{EjecucionId}|{Origen}|{Destino}|{Idioma}";
}
