namespace SherpaTravelScraper.Models;

/// <summary>
/// Modelo completo de requisitos de viaje (Departure + Return)
/// </summary>
public class RequisitosViajeCompleto
{
    /// <summary>
    /// Información del viaje
    /// </summary>
    public InformacionViaje InfoViaje { get; set; } = new();
    
    /// <summary>
    /// Requisitos para la ida (origen -> destino)
    /// </summary>
    public RequisitosTramo Departure { get; set; } = new();
    
    /// <summary>
    /// Requisitos para la vuelta (destino -> origen)
    /// </summary>
    public RequisitosTramo Return { get; set; } = new();
    
    /// <summary>
    /// Notas y advertencias generales
    /// </summary>
    public List<string> AdvertenciasGenerales { get; set; } = new();
    
    /// <summary>
    /// Enlaces oficiales
    /// </summary>
    public List<string> EnlacesOficiales { get; set; } = new();
    
    /// <summary>
    /// Fecha de extracción
    /// </summary>
    public DateTime FechaExtraccion { get; set; } = DateTime.Now;
    
    /// <summary>
    /// Nivel de confianza de la extracción (0.0 - 1.0)
    /// </summary>
    public double Confianza { get; set; }
    
    /// <summary>
    /// Contenido markdown/raw generado por IA
    /// </summary>
    public string? Markdown { get; set; }
    
    /// <summary>
    /// Método de extracción usado (javascript, ia-vision, ia-html)
    /// </summary>
    public string? MetodoExtraccion { get; set; }
}

/// <summary>
/// Información básica del viaje
/// </summary>
public class InformacionViaje
{
    public string Origen { get; set; } = "";
    public string Destino { get; set; } = "";
    public string OrigenNombre { get; set; } = "";
    public string DestinoNombre { get; set; } = "";
    public string CiudadDestino { get; set; } = "";
    public string Proposito { get; set; } = "";
    public DateTime? FechaSalida { get; set; }
    public DateTime? FechaRetorno { get; set; }
    public string TipoViaje { get; set; } = "roundTrip";
    public string Idioma { get; set; } = "";
}

/// <summary>
/// Requisitos para un tramo del viaje (ida o vuelta)
/// </summary>
public class RequisitosTramo
{
    /// <summary>
    /// Dirección del tramo
    /// </summary>
    public string Direccion { get; set; } = ""; // "Departure" o "Return"
    
    /// <summary>
    /// País de salida
    /// </summary>
    public string PaisSalida { get; set; } = "";
    
    /// <summary>
    /// País de llegada
    /// </summary>
    public string PaisLlegada { get; set; } = "";
    
    /// <summary>
    /// Requisitos de visa
    /// </summary>
    public RequisitoVisa Visa { get; set; } = new();
    
    /// <summary>
    /// Requisitos de pasaporte y documentos
    /// </summary>
    public RequisitoPasaporte Pasaporte { get; set; } = new();
    
    /// <summary>
    /// Requisitos sanitarios y de salud
    /// </summary>
    public RequisitoSalud Salud { get; set; } = new();
    
    /// <summary>
    /// Información adicional específica del tramo
    /// </summary>
    public List<InformacionAdicional> InformacionAdicional { get; set; } = new();
}

/// <summary>
/// Requisitos de visa detallados
/// </summary>
public class RequisitoVisa
{
    /// <summary>
    /// ¿Se requiere visa?
    /// </summary>
    public bool Requerido { get; set; }
    
    /// <summary>
    /// Tipo de visa (tourist, business, etc.)
    /// </summary>
    public string? Tipo { get; set; }
    
    /// <summary>
    /// Descripción del requisito
    /// </summary>
    public string? Descripcion { get; set; }
    
    /// <summary>
    /// Duración máxima permitida
    /// </summary>
    public string? DuracionMaxima { get; set; }
    
    /// <summary>
    /// Costo de la visa
    /// </summary>
    public string? Costo { get; set; }
    
    /// <summary>
    /// Tiempo de procesamiento
    /// </summary>
    public string? TiempoProcesamiento { get; set; }
    
    /// <summary>
    /// ¿Se puede obtener en línea?
    /// </summary>
    public bool? DisponibleOnline { get; set; }
    
    /// <summary>
    /// Enlace para obtener más información
    /// </summary>
    public string? EnlaceInfo { get; set; }
    
    /// <summary>
    /// Notas adicionales
    /// </summary>
    public string? Notas { get; set; }
}

/// <summary>
/// Requisitos de pasaporte y documentos
/// </summary>
public class RequisitoPasaporte
{
    /// <summary>
    /// Validez mínima requerida (ej: "6 meses")
    /// </summary>
    public string? ValidezMinima { get; set; }
    
    /// <summary>
    /// Páginas en blanco requeridas
    /// </summary>
    public string? PaginasBlanco { get; set; }
    
    /// <summary>
    /// ¿Pasaporte biométrico requerido?
    /// </summary>
    public bool? BiometricoRequerido { get; set; }
    
    /// <summary>
    /// Documentos adicionales requeridos
    /// </summary>
    public List<DocumentoRequerido> DocumentosAdicionales { get; set; } = new();
    
    /// <summary>
    /// Notas adicionales
    /// </summary>
    public string? Notas { get; set; }
}

/// <summary>
/// Documento requerido específico
/// </summary>
public class DocumentoRequerido
{
    public string Nombre { get; set; } = "";
    public string? Descripcion { get; set; }
    public bool Obligatorio { get; set; }
    public string? Enlace { get; set; }
}

/// <summary>
/// Requisitos sanitarios y de salud
/// </summary>
public class RequisitoSalud
{
    /// <summary>
    /// Vacunas requeridas
    /// </summary>
    public List<VacunaRequerida> Vacunas { get; set; } = new();
    
    /// <summary>
    /// Pruebas COVID requeridas
    /// </summary>
    public bool? PruebaCovidRequerida { get; set; }
    
    /// <summary>
    /// Detalle de requisitos COVID
    /// </summary>
    public string? DetalleCovid { get; set; }
    
    /// <summary>
    /// Seguro médico requerido
    /// </summary>
    public bool? SeguroMedicoRequerido { get; set; }
    
    /// <summary>
    /// Cobertura mínima del seguro
    /// </summary>
    public string? CoberturaSeguro { get; set; }
    
    /// <summary>
    /// Riesgos de salud en el destino
    /// </summary>
    public List<RiesgoSalud> Riesgos { get; set; } = new();
    
    /// <summary>
    /// Medicamentos recomendados
    /// </summary>
    public List<string> MedicamentosRecomendados { get; set; } = new();
    
    /// <summary>
    /// Notas adicionales
    /// </summary>
    public string? Notas { get; set; }
}

/// <summary>
/// Vacuna requerida específica
/// </summary>
public class VacunaRequerida
{
    public string Nombre { get; set; } = "";
    public string? Categoria { get; set; } // "required", "recommended", "optional"
    public string? Detalle { get; set; }
}

/// <summary>
/// Riesgo de salud en el destino
/// </summary>
public class RiesgoSalud
{
    public string Nombre { get; set; } = "";
    public string Nivel { get; set; } = ""; // "high", "moderate", "low"
    public string? Descripcion { get; set; }
    public string? Recomendacion { get; set; }
}

/// <summary>
/// Información adicional específica
/// </summary>
public class InformacionAdicional
{
    public string Categoria { get; set; } = "";
    public string Titulo { get; set; } = "";
    public string Contenido { get; set; } = "";
    public string? Enlace { get; set; }
    public bool RequiereAccion { get; set; }
}
