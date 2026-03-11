using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using SherpaTravelScraper.Models;
using SherpaTravelScraper.Utils;
using System.Data;

namespace SherpaTravelScraper.Services;

/// <summary>
/// Repositorio para operaciones con SQL Server
/// </summary>
public class TravelRepository
{
    private readonly string _connectionString;
    private readonly ILogger<TravelRepository> _logger;

    public TravelRepository(IConfiguration configuration, ILogger<TravelRepository> logger)
    {
        // Intentar obtener de configuración, si no existe usar el helper
        _connectionString = configuration.GetConnectionString("DefaultConnection") 
            ?? ConfigurationHelper.GetConnectionString();
        _logger = logger;
    }

    /// <summary>
    /// Crea una nueva ejecución y retorna su ID
    /// </summary>
    public async Task<int> CrearEjecucionAsync(int totalCombinaciones)
    {
        const string sql = @"
            INSERT INTO TXNET_REQVIAJES (reqv_fecha_inicio, reqv_estado, reqv_total_combinaciones)
            VALUES (GETDATE(), 'P', @TotalCombinaciones);
            SELECT SCOPE_IDENTITY();";

        using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();
        
        using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@TotalCombinaciones", totalCombinaciones);
        
        var result = await command.ExecuteScalarAsync();
        var id = Convert.ToInt32(result);
        
        _logger.LogInformation("Ejecución creada con ID: {Id}", id);
        return id;
    }

    /// <summary>
    /// Guarda todas las combinaciones pendientes en batch
    /// </summary>
    public async Task GuardarCombinacionesPendientesAsync(int ejecucionId, List<Combinacion> combinaciones)
    {
        const string sql = @"
            INSERT INTO txnet_combinaciones_procesar 
                (comb_reqv_id, comb_origen, comb_destino, comb_idioma, comb_estado, comb_reintentos)
            VALUES 
                (@EjecucionId, @Origen, @Destino, @Idioma, 'P', 0)";

        using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();
        
        using var transaction = connection.BeginTransaction();
        
        try
        {
            var batchSize = 100;
            var batches = (int)Math.Ceiling(combinaciones.Count / (double)batchSize);
            
            for (int b = 0; b < batches; b++)
            {
                var batch = combinaciones.Skip(b * batchSize).Take(batchSize);
                
                foreach (var c in batch)
                {
                    using var command = new SqlCommand(sql, connection, transaction);
                    command.Parameters.AddWithValue("@EjecucionId", ejecucionId);
                    command.Parameters.AddWithValue("@Origen", c.Origen);
                    command.Parameters.AddWithValue("@Destino", c.Destino);
                    command.Parameters.AddWithValue("@Idioma", c.Idioma);
                    await command.ExecuteNonQueryAsync();
                }
            }
            
            await transaction.CommitAsync();
            _logger.LogInformation("{Count} combinaciones guardadas", combinaciones.Count);
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    /// <summary>
    /// Obtiene las combinaciones pendientes con reintentos < max
    /// </summary>
    public async Task<List<Combinacion>> ObtenerPendientesAsync(int ejecucionId, int maxReintentos)
    {
        const string sql = @"
            SELECT c.comb_id, c.comb_reqv_id, c.comb_origen, c.comb_destino, c.comb_idioma, 
                   c.comb_estado, c.comb_reintentos, c.comb_fecha_procesamiento, c.comb_mensaje_error,
                   rena.rena_tiponacionalidad AS comb_tipo_nacionalidad
            FROM txnet_combinaciones_procesar c
            LEFT JOIN txnet_renacionalidades rena ON rena.rena_nacionalidad = c.comb_origen
            WHERE c.comb_reqv_id = @EjecucionId 
              AND c.comb_estado IN ('P', 'E', 'B')
              AND c.comb_reintentos < @MaxReintentos
            ORDER BY c.comb_id";

        var combinaciones = new List<Combinacion>();

        using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();
        
        using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@EjecucionId", ejecucionId);
        command.Parameters.AddWithValue("@MaxReintentos", maxReintentos);

        using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            combinaciones.Add(MapCombinacion(reader));
        }

        return combinaciones;
    }

    /// <summary>
    /// Obtiene todas las nacionalidades activas
    /// </summary>
    public async Task<List<Nacionalidad>> ObtenerNacionalidadesAsync()
    {
        const string sql = @"
            SELECT rena_id, rena_nacionalidad, rena_tiponacionalidad, 
                   rena_nacionalidadISO2, rena_idioma_default, rena_activo
            FROM txnet_renacionalidades
            WHERE rena_activo = 1
            ORDER BY rena_nacionalidad";

        var nacionalidades = new List<Nacionalidad>();

        using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();
        
        using var command = new SqlCommand(sql, connection);
        using var reader = await command.ExecuteReaderAsync();
        
        while (await reader.ReadAsync())
        {
            nacionalidades.Add(MapNacionalidad(reader));
        }

        return nacionalidades;
    }

    /// <summary>
    /// Marca una combinación como en proceso
    /// </summary>
    public async Task MarcarEnProcesoAsync(int combinacionId)
    {
        const string sql = @"
            UPDATE txnet_combinaciones_procesar 
            SET comb_estado = 'E', comb_fecha_procesamiento = GETDATE()
            WHERE comb_id = @Id";

        using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();
        
        using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@Id", combinacionId);
        await command.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// Guarda un requisito exitoso
    /// </summary>
    public async Task GuardarRequisitoAsync(
        int ejecucionId,
        string origen,
        string destino,
        string idioma,
        string url,
        string? htmlRaw,
        ResultadoScraping resultado)
    {
        const string sql = @"
            INSERT INTO txnet_detrequisitos 
                (reqvd_reqv_id, reqvd_nacionalidad_origen, reqvd_destino, reqvd_requisitos_destino,
                 reqvd_requisitos_visado, reqvd_pasaportes_documentos, reqvd_sanitarios,
                 reqvd_idioma_consultado, reqvd_url_completa, reqvd_html_raw, reqvd_datos_json, reqvd_markdown, reqvd_exito, reqvd_mensaje_error)
            VALUES 
                (@EjecucionId, @Origen, @Destino, @RequisitosDestino, @RequisitosVisado,
                 @Pasaportes, @Sanitarios, @Idioma, @Url, @HtmlRaw, @DatosJson, @Markdown, 1, NULL)";

        using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();

        using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@EjecucionId", ejecucionId);
        command.Parameters.AddWithValue("@Origen", origen);
        command.Parameters.AddWithValue("@Destino", destino);
        command.Parameters.AddWithValue("@RequisitosDestino", (object?)resultado.RequisitosDestino ?? DBNull.Value);
        command.Parameters.AddWithValue("@RequisitosVisado", (object?)resultado.RequisitosVisado ?? DBNull.Value);
        command.Parameters.AddWithValue("@Pasaportes", (object?)resultado.PasaportesDocumentos ?? DBNull.Value);
        command.Parameters.AddWithValue("@Sanitarios", (object?)resultado.Sanitarios ?? DBNull.Value);
        command.Parameters.AddWithValue("@Idioma", idioma);
        command.Parameters.AddWithValue("@Url", url);
        command.Parameters.AddWithValue("@HtmlRaw", (object?)htmlRaw ?? DBNull.Value);
        // Guardar el JSON completo de la IA (resultado.Datos)
        command.Parameters.AddWithValue("@DatosJson", (object?)resultado.Datos ?? DBNull.Value);
        // Guardar el markdown de la IA (resultado.Markdown)
        command.Parameters.AddWithValue("@Markdown", (object?)resultado.Markdown ?? DBNull.Value);
        
        await command.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// Marca una combinación como completada
    /// </summary>
    public async Task MarcarCompletadaAsync(int combinacionId)
    {
        const string sql = @"
            UPDATE txnet_combinaciones_procesar 
            SET comb_estado = 'C', comb_fecha_procesamiento = GETDATE(), comb_mensaje_error = NULL
            WHERE comb_id = @Id";

        using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();
        
        using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@Id", combinacionId);
        await command.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// Marca una combinación como fallida
    /// </summary>
    public async Task MarcarFallidaAsync(int combinacionId, string? mensajeError, bool esBloqueo = false)
    {
        const string sql = @"
            UPDATE txnet_combinaciones_procesar 
            SET comb_reintentos = comb_reintentos + 1,
                comb_estado = CASE 
                    WHEN @EsBloqueo = 1 THEN 'B'
                    WHEN comb_reintentos + 1 >= 3 THEN 'F'
                    ELSE 'P'
                END,
                comb_mensaje_error = @MensajeError,
                comb_fecha_procesamiento = GETDATE()
            WHERE comb_id = @Id";

        using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();
        
        using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@Id", combinacionId);
        command.Parameters.AddWithValue("@MensajeError", (object?)mensajeError ?? DBNull.Value);
        command.Parameters.AddWithValue("@EsBloqueo", esBloqueo);
        
        await command.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// Actualiza los contadores de progreso en la ejecución
    /// </summary>
    public async Task ActualizarProgresoAsync(int ejecucionId)
    {
        const string sql = @"
            UPDATE TXNET_REQVIAJES 
            SET reqv_combinaciones_ok = (
                SELECT COUNT(*) FROM txnet_combinaciones_procesar 
                WHERE comb_reqv_id = @EjecucionId AND comb_estado = 'C'
            ),
            reqv_combinaciones_fallidas = (
                SELECT COUNT(*) FROM txnet_combinaciones_procesar 
                WHERE comb_reqv_id = @EjecucionId AND comb_estado IN ('F', 'B')
            )
            WHERE reqv_id = @EjecucionId";

        using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();
        
        using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@EjecucionId", ejecucionId);
        await command.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// Finaliza una ejecución
    /// </summary>
    public async Task FinalizarEjecucionAsync(int ejecucionId, bool exitosa)
    {
        const string sql = @"
            UPDATE TXNET_REQVIAJES 
            SET reqv_fecha_fin = GETDATE(),
                reqv_estado = CASE WHEN @Exitosa THEN 'C' ELSE 'E' END
            WHERE reqv_id = @EjecucionId";

        using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();
        
        using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@EjecucionId", ejecucionId);
        command.Parameters.AddWithValue("@Exitosa", exitosa);
        await command.ExecuteNonQueryAsync();
        
        _logger.LogInformation("Ejecución {Id} finalizada con estado: {Estado}", 
            ejecucionId, exitosa ? "Completada" : "Error");
    }

    // Helper methods
    private static Combinacion MapCombinacion(SqlDataReader reader) => new()
    {
        Id = reader.GetInt32(0),
        EjecucionId = reader.GetInt32(1),
        Origen = reader.GetString(2),
        Destino = reader.GetString(3),
        Idioma = reader.GetString(4),
        Estado = reader.GetString(5),
        Reintentos = reader.GetInt32(6),
        FechaProcesamiento = reader.IsDBNull(7) ? null : reader.GetDateTime(7),
        MensajeError = reader.IsDBNull(8) ? null : reader.GetString(8),
        TipoNacionalidad = reader.IsDBNull(9) ? "AMBOS" : reader.GetString(9)
    };

    private static Nacionalidad MapNacionalidad(SqlDataReader reader) => new()
    {
        Id = reader.GetInt32(0),
        CodigoIso3 = reader.GetString(1),
        Tipo = reader.GetString(2),
        CodigoIso2 = reader.GetString(3),
        IdiomaDefault = reader.GetString(4),
        EsActivo = reader.GetBoolean(5)
    };
}
