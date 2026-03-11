#!/bin/bash

# =============================================================================
# Script DESATENDIDO de instalación de SQL Server 2025 en Ubuntu
# =============================================================================
# Uso: sudo MSSQL_SA_PASSWORD='TuP@ssw0rd' ./install-sqlserver-2025-unattended.sh
# =============================================================================

set -e

# Variables (puedes modificarlas o pasarlas como variables de entorno)
MSSQL_EDITION="${MSSQL_EDITION:-Developer}"  # Developer, Express, Standard, Enterprise, Evaluation
MSSQL_SA_PASSWORD="${MSSQL_SA_PASSWORD:-}"
ACCEPT_EULA="${ACCEPT_EULA:-Y}"
INSTALL_TOOLS="${INSTALL_TOOLS:-yes}"
CONFIGURE_FIREWALL="${CONFIGURE_FIREWALL:-yes}"

# Verificar que se proporcionó contraseña
if [ -z "$MSSQL_SA_PASSWORD" ]; then
    echo "ERROR: Debes proporcionar MSSQL_SA_PASSWORD"
    echo "Uso: sudo MSSQL_SA_PASSWORD='TuP@ssw0rd' $0"
    exit 1
fi

echo "=========================================="
echo "Instalando SQL Server 2025..."
echo "=========================================="

# Actualizar sistema
apt-get update -y

# Instalar dependencias
apt-get install -y curl apt-transport-https ca-certificates software-properties-common wget gnupg2

# Detectar versión de Ubuntu
. /etc/os-release
UBUNTU_VERSION=$VERSION_ID

echo "Ubuntu version detectada: $UBUNTU_VERSION"

# Agregar repo de Microsoft
curl -fsSL https://packages.microsoft.com/keys/microsoft.asc | gpg --dearmor -o /usr/share/keyrings/microsoft-prod.gpg

# Seleccionar repo según versión
case $UBUNTU_VERSION in
    "20.04")
        curl -fsSL https://packages.microsoft.com/config/ubuntu/20.04/mssql-server-2025.list | tee /etc/apt/sources.list.d/mssql-server-2025.list
        ;;
    "22.04"|"24.04")
        curl -fsSL https://packages.microsoft.com/config/ubuntu/22.04/mssql-server-2025.list | tee /etc/apt/sources.list.d/mssql-server-2025.list
        ;;
    *)
        echo "Usando repo de 22.04 para versión $UBUNTU_VERSION"
        curl -fsSL https://packages.microsoft.com/config/ubuntu/22.04/mssql-server-2025.list | tee /etc/apt/sources.list.d/mssql-server-2025.list
        ;;
esac

# Instalar SQL Server
apt-get update -y
apt-get install -y mssql-server

# Configurar de forma desatendida
echo "Configurando SQL Server..."
MSSQL_PID="$MSSQL_EDITION" \
    MSSQL_SA_PASSWORD="$MSSQL_SA_PASSWORD" \
    /opt/mssql/bin/mssql-conf -n setup accept-eula

# Instalar herramientas si se solicitó
if [ "$INSTALL_TOOLS" = "yes" ]; then
    echo "Instalando herramientas de línea de comandos..."
    curl -fsSL https://packages.microsoft.com/config/ubuntu/$UBUNTU_VERSION/prod.list | tee /etc/apt/sources.list.d/mssql-release.list
    apt-get update -y
    ACCEPT_EULA=Y apt-get install -y mssql-tools18 unixodbc-dev
    
    # Agregar al PATH global
    echo 'export PATH="$PATH:/opt/mssql-tools18/bin"' > /etc/profile.d/mssql-tools.sh
    chmod +x /etc/profile.d/mssql-tools.sh
fi

# Configurar firewall
if [ "$CONFIGURE_FIREWALL" = "yes" ]; then
    echo "Configurando firewall..."
    if command -v ufw &> /dev/null; then
        ufw allow 1433/tcp || true
    else
        apt-get install -y ufw
        ufw allow 1433/tcp
        ufw --force enable || true
    fi
fi

# Verificar estado
echo ""
echo "=========================================="
echo "Verificando instalación..."
echo "=========================================="

if systemctl is-active --quiet mssql-server; then
    echo "✅ SQL Server está corriendo correctamente"
    systemctl status mssql-server --no-pager
else
    echo "❌ Error: SQL Server no está corriendo"
    exit 1
fi

echo ""
echo "=========================================="
echo "INSTALACIÓN COMPLETADA"
echo "=========================================="
echo ""
echo "Conexión: sqlcmd -S localhost -U sa -P '*****'"
echo "Puerto: 1433"
echo "Datos: /var/opt/mssql/"
echo ""
