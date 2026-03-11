# Script de Setup Inicial para SherpaTravelScraper (Windows PowerShell)
# Este script configura las variables de entorno necesarias

$ErrorActionPreference = "Stop"

Write-Host "============================================" -ForegroundColor Cyan
Write-Host "  SherpaTravelScraper - Setup Inicial" -ForegroundColor Cyan
Write-Host "============================================" -ForegroundColor Cyan
Write-Host ""

$SCRIPT_DIR = Split-Path -Parent $MyInvocation.MyCommand.Path
$PROJECT_DIR = Split-Path -Parent $SCRIPT_DIR
$ENV_FILE = Join-Path $PROJECT_DIR ".env"

Write-Host "📁 Directorio del proyecto: $PROJECT_DIR" -ForegroundColor Gray
Write-Host ""

# Verificar si existe .env.example
$ENV_EXAMPLE = Join-Path $PROJECT_DIR ".env.example"
if (-not (Test-Path $ENV_EXAMPLE)) {
    Write-Host "❌ Error: No se encontró .env.example" -ForegroundColor Red
    exit 1
}

# Verificar si ya existe .env
if (Test-Path $ENV_FILE) {
    Write-Host "⚠️  El archivo .env ya existe" -ForegroundColor Yellow
    $response = Read-Host "¿Deseas sobrescribirlo? (s/N)"
    if ($response -notmatch '^[Ss]$') {
        Write-Host "Saliendo sin cambios..."
        exit 0
    }
    $backupName = ".env.backup.$(Get-Date -Format 'yyyyMMdd_HHmmss')"
    Copy-Item $ENV_FILE (Join-Path $PROJECT_DIR $backupName)
    Write-Host "Backup creado: $backupName" -ForegroundColor Green
}

# Copiar .env.example a .env
Copy-Item $ENV_EXAMPLE $ENV_FILE -Force

Write-Host ""
Write-Host "============================================" -ForegroundColor Cyan
Write-Host "  Configuración de Variables" -ForegroundColor Cyan
Write-Host "============================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "Por favor, introduce los valores para las variables de entorno:"
Write-Host "(Presiona Enter para mantener los valores por defecto)"
Write-Host ""

# Función para actualizar variable en .env
function Update-EnvVar {
    param($varName, $varValue)
    $content = Get-Content $ENV_FILE -Raw
    $pattern = "^$varName=.*"
    $replacement = "$varName=$varValue"
    $content = $content -replace $pattern, $replacement
    Set-Content $ENV_FILE $content -NoNewline
}

# Configuración de Base de Datos
Write-Host "📊 Configuración de Base de Datos:" -ForegroundColor Cyan
$db_server = Read-Host "  DB_SERVER [192.168.5.112]"
if ([string]::IsNullOrWhiteSpace($db_server)) { $db_server = "192.168.5.112" }
Update-EnvVar "DB_SERVER" $db_server

$db_database = Read-Host "  DB_DATABASE [TravelRequirementsDB]"
if ([string]::IsNullOrWhiteSpace($db_database)) { $db_database = "TravelRequirementsDB" }
Update-EnvVar "DB_DATABASE" $db_database

$db_user = Read-Host "  DB_USER [sa]"
if ([string]::IsNullOrWhiteSpace($db_user)) { $db_user = "sa" }
Update-EnvVar "DB_USER" $db_user

$db_password = Read-Host "  DB_PASSWORD" -AsSecureString
$BSTR = [System.Runtime.InteropServices.Marshal]::SecureStringToBSTR($db_password)
$plainPassword = [System.Runtime.InteropServices.Marshal]::PtrToStringAuto($BSTR)
if (-not [string]::IsNullOrWhiteSpace($plainPassword)) {
    Update-EnvVar "DB_PASSWORD" $plainPassword
}

Write-Host ""
Write-Host "🔑 Configuración de API Keys:" -ForegroundColor Cyan
$openrouter_key = Read-Host "  OPENROUTER_API_KEY (opcional)"
if (-not [string]::IsNullOrWhiteSpace($openrouter_key)) {
    Update-EnvVar "OPENROUTER_API_KEY" $openrouter_key
}

$kimi_key = Read-Host "  KIMI_API_KEY (opcional)"
if (-not [string]::IsNullOrWhiteSpace($kimi_key)) {
    Update-EnvVar "KIMI_API_KEY" $kimi_key
}

Write-Host ""
Write-Host "============================================" -ForegroundColor Cyan
Write-Host "  Configuración Completada" -ForegroundColor Cyan
Write-Host "============================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "✅ Archivo .env creado en: $ENV_FILE" -ForegroundColor Green
Write-Host ""
Write-Host "Para cargar las variables de entorno en PowerShell, ejecuta:" -ForegroundColor Yellow
Write-Host "  `$env:DB_SERVER='$db_server'" -ForegroundColor Gray
Write-Host "  `$env:DB_PASSWORD='$plainPassword'" -ForegroundColor Gray
Write-Host ""
Write-Host "O usa el helper de .NET que carga automáticamente el archivo .env" -ForegroundColor Yellow
Write-Host ""
Write-Host "⚠️  IMPORTANTE: No compartas el archivo .env" -ForegroundColor Yellow
Write-Host "   Este archivo contiene credenciales sensibles" -ForegroundColor Yellow
Write-Host ""

# Verificar .gitignore
$GITIGNORE = Join-Path $PROJECT_DIR ".gitignore"
if (Test-Path $GITIGNORE) {
    $gitignoreContent = Get-Content $GITIGNORE -Raw
    if ($gitignoreContent -notmatch "^\.env`$") {
        Add-Content $GITIGNORE "`n.env" -NoNewline
        Write-Host "✅ .env agregado a .gitignore" -ForegroundColor Green
    }
} else {
    ".env" | Out-File $GITIGNORE -Encoding UTF8
    Write-Host "✅ .gitignore creado con .env" -ForegroundColor Green
}

Write-Host ""
Write-Host "Setup completado! 🚀" -ForegroundColor Green
Write-Host ""
Write-Host "Para ejecutar el proyecto:" -ForegroundColor Cyan
Write-Host "  cd src\SherpaTravelScraper" -ForegroundColor Gray
Write-Host "  dotnet run" -ForegroundColor Gray
Write-Host ""

Pause
