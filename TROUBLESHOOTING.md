# TROUBLESHOOTING - Guía de Problemas Comunes

Esta guía ayuda a resolver los problemas más comunes al ejecutar SherpaTravelScraper.

## 🔴 Problemas Críticos

### 1. Error: "Connection string no configurada"

**Síntoma:**
```
System.InvalidOperationException: DB_PASSWORD no está configurado
```

**Solución:**
1. Ejecutar el script de setup:
   ```bash
   ./scripts/init.sh
   ```
2. O crear manualmente el archivo `.env`:
   ```bash
   echo "DB_PASSWORD=tu_password" > .env
   ```
3. Verificar que el archivo `.env` esté en la raíz del proyecto

### 2. Error: "OPENROUTER_API_KEY no está configurado"

**Síntoma:**
```
System.InvalidOperationException: OPENROUTER_API_KEY no está configurado
```

**Solución:**
1. Obtener API key en https://openrouter.ai/keys
2. Agregar al archivo `.env`:
   ```
   OPENROUTER_API_KEY=sk-or-v1-tu-api-key
   ```
3. Si usas método `javascript`, no necesitas API key de IA

### 3. Error de compilación: "The type or namespace name 'Xunit' could not be found"

**Síntoma:**
Tests no compilan

**Solución:**
```bash
cd src/SherpaTravelScraper.Tests
dotnet restore
dotnet build
```

---

## 🟡 Problemas de Scraping

### 4. "BLOQUEO: Acceso prohibido (403)"

**Síntoma:**
El sitio bloquea las peticiones del scraper.

**Causas:**
- Rate limiting
- Detección de bot

**Soluciones:**
1. Aumentar delays entre requests (en `appsettings.json`):
   ```json
   {
     "Scraping": {
       "DelayMinSegundos": 5,
       "DelayMaxSegundos": 10
     }
   }
   ```

2. Usar modo headless=false (navegador visible):
   ```bash
   dotnet run -- --Playwright:Headless=false
   ```

3. Cambiar User-Agent en `appsettings.json`

4. Esperar algunos minutos antes de reintentar

### 5. Timeout al cargar página

**Síntoma:**
```
TimeoutException: Timeout 20000ms exceeded
```

**Solución:**
Aumentar timeouts en configuración:
```json
{
  "Scraping": {
    "NavigationTimeoutMs": 30000,
    "WaitForSelectorTimeoutMs": 25000
  }
}
```

### 6. "No se encontraron selectores" / Contenido vacío

**Síntoma:**
El scraper no encuentra información en la página.

**Causas:**
- Cambios en el sitio web de Sherpa
- Contenido dinámico no cargado

**Soluciones:**
1. Activar screenshots de debug:
   ```json
   {
     "Scraping": {
       "DebugScreenshots": true
     }
   }
   ```

2. Revisar los screenshots generados en la carpeta del proyecto

3. Cambiar a método `ia-vision` para usar IA en lugar de selectores:
   ```bash
   dotnet run -- --Extraction:Method=ia-vision
   ```

---

## 🟢 Problemas de IA

### 7. Error: "Error llamando a OpenRouter Vision API"

**Síntoma:**
La IA no responde o devuelve error.

**Soluciones:**
1. Verificar API key:
   ```bash
   echo $OPENROUTER_API_KEY
   ```

2. Verificar créditos en https://openrouter.ai/settings/credits

3. Probar con modelo diferente:
   ```json
   {
     "Extraction": {
       "IaVision": {
         "Model": "anthropic/claude-3-haiku"
       }
     }
   }
   ```

4. Cambiar a método `javascript` si los servicios de IA fallan

### 8. JSON inválido de respuesta de IA

**Síntoma:**
La IA responde pero el JSON no se puede parsear.

**Solución:**
1. Reducir temperatura para respuestas más deterministas:
   ```json
   {
     "Extraction": {
       "IaVision": {
         "Temperature": 0.0
       }
     }
   }
   ```

2. Cambiar a modelo más capaz (ej: `gpt-4o`)

---

## 🔵 Problemas de Base de Datos

### 9. Error de conexión a SQL Server

**Síntoma:**
```
SqlException: A network-related or instance-specific error occurred
```

**Soluciones:**
1. Verificar servidor está accesible:
   ```bash
   telnet tu-servidor 1433
   ```

2. Verificar credenciales en `.env`

3. Para SQL Server local, usar:
   ```
   DB_SERVER=localhost
   # o
   DB_SERVER=127.0.0.1
   ```

4. Habilitar TCP/IP en SQL Server Configuration Manager

### 10. Tablas no existen

**Síntoma:**
```
SqlException: Invalid object name 'TXNET_REQVIAJES'
```

**Solución:**
Ejecutar script de creación de tablas:
```bash
sqlcmd -S localhost -U sa -P tu_password -i scripts/database-schema.sql
```

---

## ⚙️ Problemas de Configuración

### 11. Variables de entorno no se cargan

**Síntoma:**
La aplicación no lee el archivo `.env`

**Solución:**
1. Verificar que `.env` esté en la raíz del proyecto (no en src/)
2. Verificar formato del archivo (sin espacios alrededor de =):
   ```
   DB_PASSWORD=mi_password
   OPENROUTER_API_KEY=sk-or-v1-xxx
   ```
3. Cargar manualmente:
   ```bash
   export $(cat .env | xargs)
   ```

### 12. Playwright no está instalado

**Síntoma:**
```
Microsoft.Playwright.PlaywrightException: Executable not found
```

**Solución:**
```bash
dotnet tool install --global Microsoft.Playwright.CLI
playwright install chromium
```

---

## 📊 Problemas de Rendimiento

### 13. Procesamiento muy lento

**Soluciones:**
1. Reducir delays si el sitio no está bloqueando:
   ```json
   {
     "Scraping": {
       "DelayMinSegundos": 1,
       "DelayMaxSegundos": 3
     }
   }
   ```

2. Usar modo headless=true (más rápido)

3. Desactivar screenshots de debug

4. Usar método `ia-html` en lugar de `ia-vision` (más rápido)

### 14. Consumo alto de memoria

**Solución:**
El servicio libera recursos automáticamente. Para forzar:
```bash
# Reiniciar periódicamente si se procesan muchas combinaciones
# O usar checkpoint para continuar desde donde se quedó
```

---

## 🆘 Contacto y Soporte

Si el problema persiste:

1. Revisar logs completos: `dotnet run 2>&1 | tee debug.log`
2. Crear un issue con:
   - Descripción del problema
   - Mensaje de error completo
   - Pasos para reproducir
   - Configuración usada (sin API keys)

## 📚 Recursos Adicionales

- [Documentación de Playwright](https://playwright.dev/dotnet/)
- [API de OpenRouter](https://openrouter.ai/docs)
- [.NET Configuration](https://docs.microsoft.com/en-us/dotnet/core/extensions/configuration)
