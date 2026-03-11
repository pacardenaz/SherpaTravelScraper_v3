# SherpaTravelScraper_v3

Scraper automatizado para extracción de requisitos de viaje desde JoinSherpa. Extrae información de visas, pasaportes, requisitos sanitarios y más para combinaciones de países.

## 🚀 Características

- ✅ **Múltiples métodos de extracción:** JavaScript tradicional, IA con Visión (screenshots), IA con HTML
- ✅ **Extracción por tipo de nacionalidad (v3):** ORIGEN=Departure, DESTINO=Return, AMBOS=ambos tabs
- ✅ **Soporte múltiple de IA:** OpenRouter, Kimi (Moonshot), Ollama
- ✅ **Procesamiento paralelo:** Scrapea múltiples combinaciones simultáneamente
- ✅ **Resiliente:** Sistema de retry con backoff exponencial
- ✅ **Almacenamiento persistente:** Guarda resultados en SQL Server
- ✅ **Stealth mode:** Evita detección con Playwright

## 📋 Requisitos Previos

### Sistema
- .NET 10 SDK
- SQL Server (local o remoto)
- Node.js (para Playwright)

### API Keys (Opcional si usas JavaScript)
- [OpenRouter API Key](https://openrouter.ai/keys) - Para extracción con IA
- [Kimi API Key](https://platform.moonshot.cn/) - Alternativa para IA

## 🛠️ Instalación

### 1. Clonar el repositorio

```bash
git clone https://github.com/tuusuario/SherpaTravelScraper.git
cd SherpaTravelScraper
```

### 2. Configurar variables de entorno

```bash
# Ejecutar el script de setup
./scripts/init.sh

# O crear manualmente el archivo .env
cp .env.example .env
# Editar .env con tus credenciales
```

Variables requeridas:
- `DB_PASSWORD` - Contraseña de SQL Server
- `OPENROUTER_API_KEY` - API key de OpenRouter (si usas IA)

### 3. Restaurar dependencias

```bash
cd src/SherpaTravelScraper
dotnet restore
```

### 4. Instalar Playwright

```bash
dotnet tool install --global Microsoft.Playwright.CLI
playwright install chromium
```

### 5. Crear base de datos

Ejecuta el script SQL para crear las tablas necesarias:

```bash
# Usando sqlcmd o tu cliente SQL preferido
sqlcmd -S localhost -U sa -P tu_password -i scripts/database-schema.sql
```

## ⚙️ Configuración

### Métodos de Extracción

Edita `appsettings.json` o usa variables de entorno:

```json
{
  "Extraction": {
    "Method": "javascript"  // Opciones: javascript, ia-vision, ia-html
  }
}
```

**Métodos disponibles:**
- `javascript` - Extracción tradicional con selectores CSS y JavaScript
- `ia-vision` - Usa IA para analizar screenshots de la página
- `ia-html` - Envía el HTML a un modelo de IA para extracción

### Configuración de Base de Datos

```bash
# Variables de entorno
export DB_SERVER=localhost
export DB_DATABASE=TravelRequirementsDB
export DB_USER=sa
export DB_PASSWORD=tu_password
```

## ▶️ Uso

### Ejecución básica

```bash
cd src/SherpaTravelScraper
dotnet run
```

### Ejecución con parámetros

```bash
# Modo headless (sin navegador visible)
dotnet run -- --Playwright:Headless=true

# Método de extracción específico
dotnet run -- --Extraction:Method=ia-vision

# Proveedor de IA
dotnet run -- --Extraction:IaVision:Provider=openrouter
```

### Cancelar ejecución

Presiona `Ctrl+C` para detener el proceso de forma segura. Los datos ya procesados se mantienen.

## 📁 Estructura del Proyecto

```
SherpaTravelScraper/
├── src/
│   ├── SherpaTravelScraper/
│   │   ├── Services/
│   │   │   ├── SherpaScraperService.cs    # Lógica de scraping
│   │   │   ├── AiExtractionService.cs     # Extracción con IA
│   │   │   ├── TravelRepository.cs        # Acceso a base de datos
│   │   │   └── ...
│   │   ├── Models/
│   │   │   ├── RequisitosViajeCompleto.cs # Modelo de datos
│   │   │   └── ...
│   │   ├── Utils/
│   │   │   ├── EnvLoader.cs               # Carga de .env
│   │   │   └── ConfigurationHelper.cs     # Helper de config
│   │   ├── Program.cs
│   │   └── appsettings.json
│   └── SherpaTravelScraper.Tests/
├── scripts/
│   ├── init.sh                            # Script de setup
│   └── database-schema.sql                # Schema de BD
├── .env.example                           # Ejemplo de variables
└── README.md
```

## 🗄️ Estructura de Base de Datos

### Tablas Principales

**TXNET_REQVIAJES** - Registro de ejecuciones del scraper
```sql
- reqv_id (PK)
- reqv_fecha_inicio
- reqv_fecha_fin
- reqv_estado (P=Procesando, C=Completado, E=Error)
- reqv_total_combinaciones
- reqv_combinaciones_procesadas
- reqv_combinaciones_exitosas
```

**txnet_combinaciones_procesar** - Combinaciones a procesar
```sql
- comb_id (PK)
- comb_reqv_id (FK)
- comb_origen, comb_destino, comb_idioma
- comb_estado (P=Pendiente, C=Completado, E=Error)
- comb_reintentos
- comb_resultado_json
```

## 🔧 Troubleshooting

Ver [TROUBLESHOOTING.md](TROUBLESHOOTING.md) para problemas comunes.

## 🧪 Testing

```bash
cd src/SherpaTravelScraper.Tests
dotnet test
```

## 📝 Logs

Los logs se muestran en consola. Para guardar a archivo:

```bash
dotnet run 2>&1 | tee scraper.log
```

## 🤝 Contribuciones

1. Fork el repositorio
2. Crea un branch (`git checkout -b feature/nueva-funcionalidad`)
3. Commit cambios (`git commit -am 'Agrega nueva funcionalidad'`)
4. Push al branch (`git push origin feature/nueva-funcionalidad`)
5. Crea un Pull Request

## 📄 Licencia

MIT License - ver [LICENSE](LICENSE) para detalles.

## 🙏 Agradecimientos

- [Playwright](https://playwright.dev/) - Framework de automatización
- [OpenRouter](https://openrouter.ai/) - API de IA unificada
- [JoinSherpa](https://www.joinsherpa.com/) - Fuente de datos de requisitos de viaje
