#!/bin/bash
# Script de Setup Inicial para SherpaTravelScraper
# Este script configura las variables de entorno necesarias

set -e

echo "============================================"
echo "  SherpaTravelScraper - Setup Inicial"
echo "============================================"
echo ""

# Colores para output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
NC='\033[0m' # No Color

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_DIR="$(dirname "$SCRIPT_DIR")"
ENV_FILE="$PROJECT_DIR/.env"

echo "📁 Directorio del proyecto: $PROJECT_DIR"
echo ""

# Verificar si existe .env.example
if [ ! -f "$PROJECT_DIR/.env.example" ]; then
    echo -e "${RED}❌ Error: No se encontró .env.example${NC}"
    exit 1
fi

# Verificar si ya existe .env
if [ -f "$ENV_FILE" ]; then
    echo -e "${YELLOW}⚠️  El archivo .env ya existe${NC}"
    read -p "¿Deseas sobrescribirlo? (s/N): " -n 1 -r
    echo
    if [[ ! $REPLY =~ ^[Ss]$ ]]; then
        echo "Saliendo sin cambios..."
        exit 0
    fi
    cp "$ENV_FILE" "$ENV_FILE.backup.$(date +%Y%m%d_%H%M%S)"
    echo "Backup creado del .env anterior"
fi

# Copiar .env.example a .env
cp "$PROJECT_DIR/.env.example" "$ENV_FILE"

echo ""
echo "============================================"
echo "  Configuración de Variables"
echo "============================================"
echo ""
echo "Por favor, introduce los valores para las variables de entorno:"
echo "(Presiona Enter para mantener los valores por defecto)"
echo ""

# Función para actualizar variable en .env
update_env_var() {
    local var_name=$1
    local var_value=$2
    
    if [[ "$OSTYPE" == "darwin"* ]]; then
        # macOS
        sed -i '' "s|^${var_name}=.*|${var_name}=${var_value}|" "$ENV_FILE"
    else
        # Linux
        sed -i "s|^${var_name}=.*|${var_name}=${var_value}|" "$ENV_FILE"
    fi
}

# Configuración de Base de Datos
echo "📊 Configuración de Base de Datos:"
read -p "  DB_SERVER [192.168.5.112]: " db_server
db_server=${db_server:-192.168.5.112}
update_env_var "DB_SERVER" "$db_server"

read -p "  DB_DATABASE [TravelRequirementsDB]: " db_database
db_database=${db_database:-TravelRequirementsDB}
update_env_var "DB_DATABASE" "$db_database"

read -p "  DB_USER [sa]: " db_user
db_user=${db_user:-sa}
update_env_var "DB_USER" "$db_user"

read -s -p "  DB_PASSWORD: " db_password
echo
if [ -n "$db_password" ]; then
    update_env_var "DB_PASSWORD" "$db_password"
fi

echo ""
echo "🔑 Configuración de API Keys:"
read -p "  OPENROUTER_API_KEY: " openrouter_key
if [ -n "$openrouter_key" ]; then
    update_env_var "OPENROUTER_API_KEY" "$openrouter_key"
fi

read -p "  KIMI_API_KEY: " kimi_key
if [ -n "$kimi_key" ]; then
    update_env_var "KIMI_API_KEY" "$kimi_key"
fi

echo ""
echo "============================================"
echo "  Configuración Completada"
echo "============================================"
echo ""
echo -e "${GREEN}✅ Archivo .env creado en: $ENV_FILE${NC}"
echo ""
echo "Para cargar las variables de entorno, ejecuta:"
echo "  source $ENV_FILE"
echo ""
echo "O en tu archivo ~/.bashrc o ~/.zshrc agrega:"
echo "  export \$(cat $ENV_FILE | xargs)"
echo ""
echo -e "${YELLOW}⚠️  IMPORTANTE: No compartas el archivo .env${NC}"
echo -e "${YELLOW}   Este archivo contiene credenciales sensibles${NC}"
echo ""

# Verificar .gitignore
if [ -f "$PROJECT_DIR/.gitignore" ]; then
    if ! grep -q "^\.env$" "$PROJECT_DIR/.gitignore"; then
        echo ".env" >> "$PROJECT_DIR/.gitignore"
        echo -e "${GREEN}✅ .env agregado a .gitignore${NC}"
    fi
else
    echo ".env" > "$PROJECT_DIR/.gitignore"
    echo -e "${GREEN}✅ .gitignore creado con .env${NC}"
fi

echo ""
echo "Setup completado! 🚀"
