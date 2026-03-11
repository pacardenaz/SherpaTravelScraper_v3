using Microsoft.Data.SqlClient;

Console.WriteLine("📊 Verificando detalles de requisitos...");

var connectionString = "Server=192.168.5.112;Database=TravelRequirementsDB;User Id=sa;Password=Isami06cz%;TrustServerCertificate=True;";

try
{
    using var connection = new SqlConnection(connectionString);
    await connection.OpenAsync();

    // Ver registros con datos
    Console.WriteLine("📋 Últimos registros:");
    using (var cmd = new SqlCommand(@"
        SELECT TOP 10 
            reqvd_id,
            reqvd_nacionalidad_origen,
            reqvd_destino,
            reqvd_requisitos_visado,
            reqvd_pasaportes_documentos,
            reqvd_sanitarios,
            reqvd_exito
        FROM txnet_detrequisitos
        ORDER BY reqvd_id DESC", connection))
    {
        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var id = reader.GetInt32(0);
            var origen = reader.GetString(1);
            var destino = reader.GetString(2);
            var visa = reader.IsDBNull(3) ? "NULL" : reader.GetString(3)?.Substring(0, Math.Min(50, reader.GetString(3).Length)) + "...";
            var pasaporte = reader.IsDBNull(4) ? "NULL" : reader.GetString(4)?.Substring(0, Math.Min(50, reader.GetString(4).Length)) + "...";
            var salud = reader.IsDBNull(5) ? "NULL" : reader.GetString(5)?.Substring(0, Math.Min(50, reader.GetString(5).Length)) + "...";
            var exito = reader.GetBoolean(6);

            Console.WriteLine($"\n  ID: {id} | {origen} -> {destino} | Éxito: {exito}");
            Console.WriteLine($"    Visa: {visa}");
            Console.WriteLine($"    Pasaporte: {pasaporte}");
            Console.WriteLine($"    Salud: {salud}");
        }
    }

    // Contar registros con datos reales
    Console.WriteLine("\n📊 Estadísticas:");
    using (var cmd = new SqlCommand(@"
        SELECT 
            COUNT(*) as total,
            SUM(CASE WHEN reqvd_requisitos_visado IS NOT NULL AND LEN(reqvd_requisitos_visado) > 10 THEN 1 ELSE 0 END) as con_visa,
            SUM(CASE WHEN reqvd_pasaportes_documentos IS NOT NULL AND LEN(reqvd_pasaportes_documentos) > 10 THEN 1 ELSE 0 END) as con_pasaporte,
            SUM(CASE WHEN reqvd_sanitarios IS NOT NULL AND LEN(reqvd_sanitarios) > 10 THEN 1 ELSE 0 END) as con_salud,
            SUM(CASE WHEN reqvd_exito = 1 THEN 1 ELSE 0 END) as exitosos
        FROM txnet_detrequisitos", connection))
    {
        using var reader = await cmd.ExecuteReaderAsync();
        if (await reader.ReadAsync())
        {
            Console.WriteLine($"  Total registros: {reader.GetInt32(0)}");
            Console.WriteLine($"  Con datos de Visa: {reader.GetInt32(1)}");
            Console.WriteLine($"  Con datos de Pasaporte: {reader.GetInt32(2)}");
            Console.WriteLine($"  Con datos de Salud: {reader.GetInt32(3)}");
            Console.WriteLine($"  Exitosos: {reader.GetInt32(4)}");
        }
    }

    // Ver campos JSON
    Console.WriteLine("\n📋 Registros con datos JSON:");
    using (var cmd = new SqlCommand(@"
        SELECT TOP 5 
            reqvd_id,
            reqvd_nacionalidad_origen,
            reqvd_destino,
            LEN(ISNULL(reqvd_datos_json, '')) as json_len,
            LEN(ISNULL(reqvd_markdown, '')) as md_len
        FROM txnet_detrequisitos
        ORDER BY reqvd_id DESC", connection))
    {
        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            Console.WriteLine($"  ID {reader.GetInt32(0)} ({reader.GetString(1)}->{reader.GetString(2)}): JSON={reader.GetInt32(3)} chars, MD={reader.GetInt32(4)} chars");
        }
    }

    Console.WriteLine("\n✅ Verificación completada");
}
catch (Exception ex)
{
    Console.WriteLine($"❌ Error: {ex.Message}");
    Environment.Exit(1);
}
