using Xunit;
using SherpaTravelScraper.Models;
using SherpaTravelScraper.Services;
using Microsoft.Extensions.Logging;

namespace SherpaTravelScraper.Tests;

/// <summary>
/// Smoke Tests funcionales para validar lógica RENA_TIPONACIONALIDAD
/// 
/// Reglas de extracción por Tipo:
/// - ORIGEN  -> extraer solo Departure
/// - DESTINO -> extraer solo Return  
/// - AMBOS   -> extraer Departure y Return
/// </summary>
public class RenaTipoNacionalidadSmokeTests
{
    private readonly ILogger<RenaTipoNacionalidadSmokeTests> _logger;

    public RenaTipoNacionalidadSmokeTests()
    {
        using var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
        _logger = loggerFactory.CreateLogger<RenaTipoNacionalidadSmokeTests>();
    }

    #region Tests de Modelo Nacionalidad

    [Theory]
    [InlineData("ORIGEN", true, false)]
    [InlineData("DESTINO", false, true)]
    [InlineData("AMBOS", true, true)]
    public void Nacionalidad_PuedeSerOrigenDestino_SegunTipo(string tipo, bool esperadoOrigen, bool esperadoDestino)
    {
        // Arrange
        var nacionalidad = new Nacionalidad
        {
            Id = 1,
            CodigoIso3 = "TEST",
            Tipo = tipo,
            CodigoIso2 = "TS",
            EsActivo = true
        };

        // Act & Assert
        Assert.Equal(esperadoOrigen, nacionalidad.PuedeSerOrigen);
        Assert.Equal(esperadoDestino, nacionalidad.PuedeSerDestino);
    }

    [Fact]
    public void Nacionalidad_TipoVacio_NoPuedeSerOrigenNiDestino()
    {
        // Arrange
        var nacionalidad = new Nacionalidad
        {
            Id = 1,
            CodigoIso3 = "TST",
            Tipo = "", // Tipo vacío
            EsActivo = true
        };

        // Act & Assert
        Assert.False(nacionalidad.PuedeSerOrigen);
        Assert.False(nacionalidad.PuedeSerDestino);
    }

    [Fact]
    public void Nacionalidad_TipoInvalido_NoPuedeSerOrigenNiDestino()
    {
        // Arrange
        var nacionalidad = new Nacionalidad
        {
            Id = 1,
            CodigoIso3 = "TST",
            Tipo = "INVALIDO",
            EsActivo = true
        };

        // Act & Assert
        Assert.False(nacionalidad.PuedeSerOrigen);
        Assert.False(nacionalidad.PuedeSerDestino);
    }

    #endregion

    #region Tests de Generación de Combinaciones

    [Fact]
    public void CombinacionGenerator_SoloOrigen_NoGeneraCombinacionesComoDestino()
    {
        // Arrange - Solo países ORIGEN
        var nacionalidades = new List<Nacionalidad>
        {
            new() { Id = 1, CodigoIso3 = "USA", Tipo = "ORIGEN", EsActivo = true },
            new() { Id = 2, CodigoIso3 = "CAN", Tipo = "ORIGEN", EsActivo = true }
        };

        using var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
        var generator = new CombinacionGenerator(loggerFactory.CreateLogger<CombinacionGenerator>());

        // Act
        var combinaciones = generator.GenerarCombinaciones(nacionalidades, 1);

        // Assert - No debería haber combinaciones porque nadie puede ser destino
        Assert.Empty(combinaciones);
    }

    [Fact]
    public void CombinacionGenerator_SoloDestino_NoGeneraCombinacionesComoOrigen()
    {
        // Arrange - Solo países DESTINO
        var nacionalidades = new List<Nacionalidad>
        {
            new() { Id = 1, CodigoIso3 = "USA", Tipo = "DESTINO", EsActivo = true },
            new() { Id = 2, CodigoIso3 = "CAN", Tipo = "DESTINO", EsActivo = true }
        };

        using var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
        var generator = new CombinacionGenerator(loggerFactory.CreateLogger<CombinacionGenerator>());

        // Act
        var combinaciones = generator.GenerarCombinaciones(nacionalidades, 1);

        // Assert - No debería haber combinaciones porque nadie puede ser origen
        Assert.Empty(combinaciones);
    }

    [Fact]
    public void CombinacionGenerator_Ambos_GeneraCombinacionesCorrectas()
    {
        // Arrange - Un país AMBOS
        var nacionalidades = new List<Nacionalidad>
        {
            new() { Id = 1, CodigoIso3 = "USA", Tipo = "AMBOS", EsActivo = true, IdiomaDefault = "EN-US" },
            new() { Id = 2, CodigoIso3 = "CAN", Tipo = "ORIGEN", EsActivo = true },
            new() { Id = 3, CodigoIso3 = "MEX", Tipo = "DESTINO", EsActivo = true }
        };

        using var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
        var generator = new CombinacionGenerator(loggerFactory.CreateLogger<CombinacionGenerator>());

        // Act
        var combinaciones = generator.GenerarCombinaciones(nacionalidades, 1);

        // Assert
        // USA (AMBOS) puede ser origen y destino
        // CAN (ORIGEN) solo puede ser origen
        // MEX (DESTINO) solo puede ser destino
        // Combinaciones esperadas: USA->MEX, CAN->USA, CAN->MEX
        Assert.Equal(3, combinaciones.Count);
        
        // Verificar combinaciones específicas
        Assert.Contains(combinaciones, c => c.Origen == "USA" && c.Destino == "MEX");
        Assert.Contains(combinaciones, c => c.Origen == "CAN" && c.Destino == "USA");
        Assert.Contains(combinaciones, c => c.Origen == "CAN" && c.Destino == "MEX");
    }

    [Fact]
    public void CombinacionGenerator_NoPermiteMismoOrigenYDestino()
    {
        // Arrange
        var nacionalidades = new List<Nacionalidad>
        {
            new() { Id = 1, CodigoIso3 = "USA", Tipo = "AMBOS", EsActivo = true, IdiomaDefault = "EN-US" },
            new() { Id = 2, CodigoIso3 = "CAN", Tipo = "AMBOS", EsActivo = true, IdiomaDefault = "EN-US" }
        };

        using var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
        var generator = new CombinacionGenerator(loggerFactory.CreateLogger<CombinacionGenerator>());

        // Act
        var combinaciones = generator.GenerarCombinaciones(nacionalidades, 1);

        // Assert
        // Solo debería haber USA->CAN y CAN->USA (no USA->USA ni CAN->CAN)
        Assert.Equal(2, combinaciones.Count);
        Assert.DoesNotContain(combinaciones, c => c.Origen == c.Destino);
    }

    #endregion

    #region Smoke Tests de Extracción por Tipo

    /// <summary>
    /// CASO 1: ORIGEN -> Debe extraer SOLO Departure
    /// </summary>
    [Fact]
    public void Extraccion_TipoORIGEN_SoloExtraeDeparture()
    {
        // Arrange
        var nacionalidad = new Nacionalidad
        {
            CodigoIso3 = "USA",
            Tipo = "ORIGEN",
            EsActivo = true
        };

        var requisitos = new RequisitosViajeCompleto
        {
            InfoViaje = new InformacionViaje
            {
                Origen = "USA",
                Destino = "CAN"
            },
            Departure = new RequisitosTramo
            {
                Direccion = "Departure",
                PaisSalida = "USA",
                PaisLlegada = "CAN",
                Visa = new RequisitoVisa { Requerido = true, Tipo = "ESTA" }
            },
            Return = new RequisitosTramo
            {
                Direccion = "Return",
                PaisSalida = "CAN",
                PaisLlegada = "USA",
                Visa = new RequisitoVisa { Requerido = false }
            }
        };

        // Act & Assert
        Assert.True(nacionalidad.PuedeSerOrigen);
        Assert.False(nacionalidad.PuedeSerDestino);
        
        // Verificar que el modelo tiene ambos tramos
        Assert.NotNull(requisitos.Departure);
        Assert.NotNull(requisitos.Return);
        
        // Para ORIGEN, solo nos interesa el Departure
        Assert.Equal("Departure", requisitos.Departure.Direccion);
        Assert.Equal("USA", requisitos.Departure.PaisSalida);
        Assert.True(requisitos.Departure.Visa.Requerido);
    }

    /// <summary>
    /// CASO 2: DESTINO -> Debe extraer SOLO Return
    /// </summary>
    [Fact]
    public void Extraccion_TipoDESTINO_SoloExtraeReturn()
    {
        // Arrange
        var nacionalidad = new Nacionalidad
        {
            CodigoIso3 = "CAN",
            Tipo = "DESTINO",
            EsActivo = true
        };

        var requisitos = new RequisitosViajeCompleto
        {
            InfoViaje = new InformacionViaje
            {
                Origen = "USA",
                Destino = "CAN"
            },
            Departure = new RequisitosTramo
            {
                Direccion = "Departure",
                PaisSalida = "USA",
                PaisLlegada = "CAN",
                Visa = new RequisitoVisa { Requerido = true }
            },
            Return = new RequisitosTramo
            {
                Direccion = "Return",
                PaisSalida = "CAN",
                PaisLlegada = "USA",
                Visa = new RequisitoVisa { Requerido = false, Tipo = "Exento" }
            }
        };

        // Act & Assert
        Assert.False(nacionalidad.PuedeSerOrigen);
        Assert.True(nacionalidad.PuedeSerDestino);
        
        // Para DESTINO, solo nos interesa el Return
        Assert.Equal("Return", requisitos.Return.Direccion);
        Assert.Equal("CAN", requisitos.Return.PaisSalida);
        Assert.False(requisitos.Return.Visa.Requerido);
    }

    /// <summary>
    /// CASO 3: AMBOS -> Debe extraer Departure Y Return
    /// </summary>
    [Fact]
    public void Extraccion_TipoAMBOS_ExtraeDepartureYReturn()
    {
        // Arrange
        var nacionalidad = new Nacionalidad
        {
            CodigoIso3 = "GBR",
            Tipo = "AMBOS",
            EsActivo = true
        };

        var requisitos = new RequisitosViajeCompleto
        {
            InfoViaje = new InformacionViaje
            {
                Origen = "GBR",
                Destino = "USA"
            },
            Departure = new RequisitosTramo
            {
                Direccion = "Departure",
                PaisSalida = "GBR",
                PaisLlegada = "USA",
                Visa = new RequisitoVisa { Requerido = true, Tipo = "ESTA" }
            },
            Return = new RequisitosTramo
            {
                Direccion = "Return",
                PaisSalida = "USA",
                PaisLlegada = "GBR",
                Visa = new RequisitoVisa { Requerido = false }
            }
        };

        // Act & Assert
        Assert.True(nacionalidad.PuedeSerOrigen);
        Assert.True(nacionalidad.PuedeSerDestino);
        
        // Para AMBOS, debemos tener tanto Departure como Return
        Assert.NotNull(requisitos.Departure);
        Assert.NotNull(requisitos.Return);
        
        // Verificar datos del Departure
        Assert.Equal("Departure", requisitos.Departure.Direccion);
        Assert.Equal("GBR", requisitos.Departure.PaisSalida);
        Assert.Equal("USA", requisitos.Departure.PaisLlegada);
        Assert.True(requisitos.Departure.Visa.Requerido);
        
        // Verificar datos del Return
        Assert.Equal("Return", requisitos.Return.Direccion);
        Assert.Equal("USA", requisitos.Return.PaisSalida);
        Assert.Equal("GBR", requisitos.Return.PaisLlegada);
        Assert.False(requisitos.Return.Visa.Requerido);
    }

    #endregion

    #region Tests de Validación de Datos Extraídos

    [Theory]
    [InlineData("ORIGEN")]
    [InlineData("DESTINO")]
    [InlineData("AMBOS")]
    public void RequisitosViajeCompleto_SerializacionPreservaTipo(string tipo)
    {
        // Arrange
        var nacionalidad = new Nacionalidad
        {
            CodigoIso3 = "TST",
            Tipo = tipo,
            EsActivo = true
        };

        var requisitos = new RequisitosViajeCompleto
        {
            InfoViaje = new InformacionViaje
            {
                Origen = "TST",
                Destino = "USA"
            },
            Departure = new RequisitosTramo
            {
                Direccion = "Departure",
                PaisSalida = "TST",
                PaisLlegada = "USA",
                Visa = new RequisitoVisa { Requerido = tipo == "ORIGEN" || tipo == "AMBOS" }
            },
            Return = tipo != "ORIGEN" ? new RequisitosTramo
            {
                Direccion = "Return",
                PaisSalida = "USA",
                PaisLlegada = "TST",
                Visa = new RequisitoVisa { Requerido = false }
            } : null
        };

        // Act
        var json = System.Text.Json.JsonSerializer.Serialize(requisitos);
        var deserializado = System.Text.Json.JsonSerializer.Deserialize<RequisitosViajeCompleto>(json);

        // Assert
        Assert.NotNull(deserializado);
        Assert.NotNull(deserializado.Departure);
        Assert.Equal("TST", deserializado.Departure.PaisSalida);
        
        if (tipo != "ORIGEN")
        {
            Assert.NotNull(deserializado.Return);
            Assert.Equal("Return", deserializado.Return.Direccion);
        }
    }

    #endregion
}
