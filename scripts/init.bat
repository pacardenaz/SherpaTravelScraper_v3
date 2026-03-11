@echo off
chcp 65001 >nul
REM Script de Setup Inicial para SherpaTravelScraper (Windows CMD/Batch)
REM Este script configura las variables de entorno necesarias

echo ============================================
echo   SherpaTravelScraper - Setup Inicial
echo ============================================
echo.

set "SCRIPT_DIR=%~dp0"
set "PROJECT_DIR=%SCRIPT_DIR%.."
set "ENV_FILE=%PROJECT_DIR%\.env"
set "ENV_EXAMPLE=%PROJECT_DIR%\.env.example"

echo 📁 Directorio del proyecto: %PROJECT_DIR%
echo.

REM Verificar si existe .env.example
if not exist "%ENV_EXAMPLE%" (
    echo ❌ Error: No se encontró .env.example
    pause
    exit /b 1
)

REM Verificar si ya existe .env
if exist "%ENV_FILE%" (
    echo ⚠️  El archivo .env ya existe
    set /p "RESPONSE=¿Deseas sobrescribirlo? (s/N): "
    if /i not "%RESPONSE%"=="s" (
        echo Saliendo sin cambios...
        pause
        exit /b 0
    )
    for /f "tokens=2-4 delims=/ " %%a in ('date /t') do (set mydate=%%c%%a%%b)
    for /f "tokens=1-2 delims=/:" %%a in ('time /t') do (set mytime=%%a%%b)
    copy "%ENV_FILE%" "%PROJECT_DIR%\.env.backup.%mydate%_%mytime%" >nul
    echo Backup creado del .env anterior
)

REM Copiar .env.example a .env
copy /Y "%ENV_EXAMPLE%" "%ENV_FILE%" >nul

echo.
echo ============================================
echo   Configuración de Variables
echo ============================================
echo.
echo Por favor, introduce los valores para las variables de entorno:
echo (Presiona Enter para mantener los valores por defecto)
echo.

REM Función para actualizar variable en .env usando PowerShell
setlocal EnableDelayedExpansion

REM Configuración de Base de Datos
echo 📊 Configuración de Base de Datos:
set /p "DB_SERVER=  DB_SERVER [192.168.5.112]: "
if "!DB_SERVER!"=="" set "DB_SERVER=192.168.5.112"
powershell -Command "(Get-Content '%ENV_FILE%') -replace '^DB_SERVER=.*', 'DB_SERVER=!DB_SERVER!' | Set-Content '%ENV_FILE%'"

set /p "DB_DATABASE=  DB_DATABASE [TravelRequirementsDB]: "
if "!DB_DATABASE!"=="" set "DB_DATABASE=TravelRequirementsDB"
powershell -Command "(Get-Content '%ENV_FILE%') -replace '^DB_DATABASE=.*', 'DB_DATABASE=!DB_DATABASE!' | Set-Content '%ENV_FILE%'"

set /p "DB_USER=  DB_USER [sa]: "
if "!DB_USER!"=="" set "DB_USER=sa"
powershell -Command "(Get-Content '%ENV_FILE%') -replace '^DB_USER=.*', 'DB_USER=!DB_USER!' | Set-Content '%ENV_FILE%'"

set /p "DB_PASSWORD=  DB_PASSWORD: "
if not "!DB_PASSWORD!"=="" (
    powershell -Command "(Get-Content '%ENV_FILE%') -replace '^DB_PASSWORD=.*', 'DB_PASSWORD=!DB_PASSWORD!' | Set-Content '%ENV_FILE%'"
)

echo.
echo 🔑 Configuración de API Keys:
set /p "OPENROUTER_KEY=  OPENROUTER_API_KEY (opcional): "
if not "!OPENROUTER_KEY!"=="" (
    powershell -Command "(Get-Content '%ENV_FILE%') -replace '^OPENROUTER_API_KEY=.*', 'OPENROUTER_API_KEY=!OPENROUTER_KEY!' | Set-Content '%ENV_FILE%'"
)

set /p "KIMI_KEY=  KIMI_API_KEY (opcional): "
if not "!KIMI_KEY!"=="" (
    powershell -Command "(Get-Content '%ENV_FILE%') -replace '^KIMI_API_KEY=.*', 'KIMI_API_KEY=!KIMI_KEY!' | Set-Content '%ENV_FILE%'"
)

echo.
echo ============================================
echo   Configuración Completada
echo ============================================
echo.
echo ✅ Archivo .env creado en: %ENV_FILE%
echo.
echo ⚠️  IMPORTANTE: No compartas el archivo .env
echo    Este archivo contiene credenciales sensibles
echo.

REM Verificar .gitignore
set "GITIGNORE=%PROJECT_DIR%\.gitignore"
if exist "%GITIGNORE%" (
    findstr /B /C:".env" "%GITIGNORE%" >nul
    if errorlevel 1 (
        echo. >> "%GITIGNORE%"
        echo .env >> "%GITIGNORE%"
        echo ✅ .env agregado a .gitignore
    )
) else (
    echo .env > "%GITIGNORE%"
    echo ✅ .gitignore creado con .env
)

echo.
echo Setup completado! 🚀
echo.
echo Para ejecutar el proyecto:
echo   cd src\SherpaTravelScraper
echo   dotnet run
echo.

pause
