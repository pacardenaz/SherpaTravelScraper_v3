namespace SherpaTravelScraper.Models;

/// <summary>
/// Resultado del proceso de scraping para una combinación
/// </summary>
public class ResultadoScraping
{
    public bool Exitoso { get; set; }
    public string? Datos { get; set; }
    public string UrlConsultada { get; set; } = string.Empty;
    public string? HtmlRaw { get; set; }
    public string? MensajeError { get; set; }
    public string? RequisitosDestino { get; set; }
    public string? RequisitosVisado { get; set; }
    public string? PasaportesDocumentos { get; set; }
    public string? Sanitarios { get; set; }
    public string? TabsExtraidas { get; set; }
    
    /// <summary>
    /// Contenido markdown/raw generado por IA (alternativa a Datos JSON)
    /// </summary>
    public string? Markdown { get; set; }

    public static ResultadoScraping Exito(string datos, string url, string? htmlRaw = null, 
        string? requisitosDestino = null, string? requisitosVisado = null,
        string? pasaportes = null, string? sanitarios = null, string? markdown = null, string? tabsExtraidas = null) => new()
    {
        Exitoso = true,
        Datos = datos,
        UrlConsultada = url,
        HtmlRaw = htmlRaw,
        RequisitosDestino = requisitosDestino,
        RequisitosVisado = requisitosVisado,
        PasaportesDocumentos = pasaportes,
        Sanitarios = sanitarios,
        Markdown = markdown,
        TabsExtraidas = tabsExtraidas
    };

    public static ResultadoScraping Fallo(string error, string url, string? htmlRaw = null) => new()
    {
        Exitoso = false,
        MensajeError = error,
        UrlConsultada = url,
        HtmlRaw = htmlRaw
    };
}
