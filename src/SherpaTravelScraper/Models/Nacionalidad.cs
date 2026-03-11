namespace SherpaTravelScraper.Models;

/// <summary>
/// Representa una nacionalidad con sus propiedades para el scraping
/// </summary>
public class Nacionalidad
{
    public int Id { get; set; }
    public string CodigoIso3 { get; set; } = string.Empty;
    public string Tipo { get; set; } = string.Empty; // ORIGEN, DESTINO, AMBOS
    public string CodigoIso2 { get; set; } = string.Empty;
    public string IdiomaDefault { get; set; } = "EN-US";
    public bool EsActivo { get; set; } = true;

    public bool PuedeSerOrigen => Tipo == "ORIGEN" || Tipo == "AMBOS";
    public bool PuedeSerDestino => Tipo == "DESTINO" || Tipo == "AMBOS";
}
