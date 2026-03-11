using System;
using System.Threading.Tasks;
using Xunit;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using SherpaTravelScraper.Services;
using SherpaTravelScraper.Models;

namespace SherpaTravelScraper.Tests;

/// <summary>
/// Tests de integracion para extraccion con IA
/// </summary>
public class AiExtractionIntegrationTests
{
    private readonly IConfiguration _configuration;
    private readonly ILoggerFactory _loggerFactory;

    public AiExtractionIntegrationTests()
    {
        _configuration = new ConfigurationBuilder()
            .AddJsonFile("appsettings.json", optional: true)
            .AddEnvironmentVariables()
            .Build();

        _loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
    }

    [Fact]
    public void Configuration_ShouldLoadFromEnvironment()
    {
        var connectionString = _configuration.GetConnectionString("DefaultConnection");
        var extractionMethod = _configuration["Extraction:Method"];

        Assert.NotNull(connectionString);
        Assert.NotNull(extractionMethod);
    }

    [Theory]
    [InlineData("ia-html")]
    [InlineData("javascript")]
    [InlineData("ia-vision")]
    public void ExtractionMethod_ShouldBeValid(string method)
    {
        var validMethods = new[] { "ia-html", "html", "javascript", "js", "ia-vision", "vision" };
        Assert.Contains(method, validMethods);
    }

    [Fact]
    public void OpenRouterConfig_ShouldHaveRequiredFields()
    {
        var config = _configuration.GetSection("Extraction:IaHtml");
        var endpoint = config["Endpoint"];
        var model = config["Model"];

        Assert.NotNull(endpoint);
        Assert.NotNull(model);
    }

    [Fact]
    public void RequisitosViajeCompleto_ShouldSerializeCorrectly()
    {
        var requisitos = new RequisitosViajeCompleto
        {
            InfoViaje = new InformacionViaje
            {
                Origen = "COL",
                Destino = "USA",
                CiudadDestino = "Miami"
            },
            Departure = new RequisitosTramo
            {
                PaisSalida = "COL",
                PaisLlegada = "USA",
                Visa = new RequisitoVisa
                {
                    Requerido = true,
                    Tipo = "ESTA",
                    Descripcion = "Visa waiver program"
                }
            },
            Confianza = 0.85
        };

        var json = System.Text.Json.JsonSerializer.Serialize(requisitos);
        var deserialized = System.Text.Json.JsonSerializer.Deserialize<RequisitosViajeCompleto>(json);

        Assert.NotNull(deserialized);
        Assert.Equal("COL", deserialized.InfoViaje.Origen);
        Assert.Equal("USA", deserialized.InfoViaje.Destino);
        Assert.True(deserialized.Departure?.Visa?.Requerido);
        Assert.Equal("ESTA", deserialized.Departure?.Visa?.Tipo);
    }

    [Fact]
    public void RequisitosViajeCompleto_ShouldHandleNullValues()
    {
        var requisitos = new RequisitosViajeCompleto
        {
            InfoViaje = new InformacionViaje
            {
                Origen = "COL",
                Destino = "USA"
            },
            Departure = null,
            Return = null
        };

        var json = System.Text.Json.JsonSerializer.Serialize(requisitos);
        var deserialized = System.Text.Json.JsonSerializer.Deserialize<RequisitosViajeCompleto>(json);

        Assert.NotNull(deserialized);
        Assert.Null(deserialized.Departure);
        Assert.Null(deserialized.Return);
    }

    [Fact]
    public void ResultadoScraping_ShouldCreateSuccessResult()
    {
        var resultado = ResultadoScraping.Exito(
            datos: "{test: data}",
            url: "https://test.com",
            htmlRaw: "html",
            requisitosDestino: "Requisitos test",
            requisitosVisado: "Visado test",
            pasaportes: "Pasaporte test",
            sanitarios: "Salud test"
        );

        Assert.True(resultado.Exitoso);
        Assert.Equal("https://test.com", resultado.UrlConsultada);
        Assert.Equal("Requisitos test", resultado.RequisitosDestino);
        Assert.Equal("Visado test", resultado.RequisitosVisado);
    }

    [Fact]
    public void ResultadoScraping_ShouldCreateFailureResult()
    {
        var resultado = ResultadoScraping.Fallo("Error de prueba", "https://test.com", "html content");

        Assert.False(resultado.Exitoso);
        Assert.Equal("Error de prueba", resultado.MensajeError);
        Assert.Equal("https://test.com", resultado.UrlConsultada);
        Assert.Equal("html content", resultado.HtmlRaw);
    }
}

/// <summary>
/// Tests para validacion de formato JSON
/// </summary>
public class JsonValidationTests
{
    [Theory]
    [InlineData("{\"confianza\": 0.9}")]
    [InlineData("{\"confianza\": 1.0, \"departure\": null}")]
    [InlineData("{\"advertenciasGenerales\": [], \"enlacesOficiales\": []}")]
    public void ValidJson_ShouldDeserialize(string json)
    {
        var result = System.Text.Json.JsonSerializer.Deserialize<RequisitosViajeCompleto>(json);
        Assert.NotNull(result);
    }

    [Theory]
    [InlineData("{ invalid json }")]
    [InlineData("not json at all")]
    public void InvalidJson_ShouldThrowException(string json)
    {
        Assert.ThrowsAny<System.Text.Json.JsonException>(() =>
        {
            System.Text.Json.JsonSerializer.Deserialize<RequisitosViajeCompleto>(json);
        });
    }

    [Theory]
    [InlineData("{\"confianza\": 1.0}", 1.0)]
    [InlineData("{\"confianza\": 0.0}", 0.0)]
    [InlineData("{\"confianza\": 0.5}", 0.5)]
    public void ConfidenceValue_ShouldBeParsedCorrectly(string json, double expectedConfidence)
    {
        var options = new System.Text.Json.JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowReadingFromString
        };
        var result = System.Text.Json.JsonSerializer.Deserialize<RequisitosViajeCompleto>(json, options);
        Assert.NotNull(result);
        Assert.Equal(expectedConfidence, result.Confianza);
    }
}
