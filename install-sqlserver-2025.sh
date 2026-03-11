#!/bin/bash

# =============================================================================
# Script de instalación de SQL Server 2025 en Ubuntu
# =============================================================================
# Compatible con: Ubuntu 20.04, 22.04, 24.04
# Ejecutar como root o con sudo
# =============================================================================

set -e  # Detenerse en caso de error

# Colores para output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
NC='\033[0m' # No Color

# =============================================================================
# FUNCIONES DE UTILIDAD
# =============================================================================

log_info() {
    echo -e "${GREEN}[INFO]${NC} $1"
}

log_warn() {
    echo -e "${YELLOW}[WARN]${NC} $1"
}

log_error() {
    echo -e "${RED}[ERROR]${NC} $1"
}

check_root() {
    if [[ $EUID -ne 0 ]]; then
        log_error "Este script debe ejecutarse como root o con sudo"
        exit 1
    fi
}

detect_ubuntu_version() {
    if [ -f /etc/os-release ]; then
        . /etc/os-release
        UBUNTU_VERSION=$VERSION_ID
        UBUNTU_CODENAME=$VERSION_CODENAME
        log_info "Sistema detectado: Ubuntu $UBUNTU_VERSION ($UBUNTU_CODENAME)"
    else
        log_error "No se pudo detectar la versión de Ubuntu"
        exit 1
    fi
}

# =============================================================================
# INSTALACIÓN
# =============================================================================

install_dependencies() {
    log_info "Actualizando lista de paquetes..."
    apt-get update -y

    log_info "Instalando dependencias necesarias..."
    apt-get install -y \
        curl \
        apt-transport-https \
        ca-certificates \
        software-properties-common \
        wget \
        gnupg2 \
        lsb-release
}

add_microsoft_repo() {
    log_info "Agregando clave GPG de Microsoft..."
    curl -fsSL https://packages.microsoft.com/keys/microsoft.asc | gpg --dearmor -o /usr/share/keyrings/microsoft-prod.gpg

    log_info "Agregando repositorio de SQL Server..."
    
    # Determinar la versión del repo según Ubuntu
    case $UBUNTU_VERSION in
        "20.04")
            REPO_URL="https://packages.microsoft.com/config/ubuntu/20.04/mssql-server-2025.list"
            ;;
        "22.04")
            REPO_URL="https://packages.microsoft.com/config/ubuntu/22.04/mssql-server-2025.list"
            ;;
        "24.04")
            REPO_URL="https://packages.microsoft.com/config/ubuntu/22.04/mssql-server-2025.list"
            log_warn "Ubuntu 24.04 - usando repo de 22.04 (compatible)"
            ;;
        *)
            log_warn "Versión no probada, intentando con repo de 22.04..."
            REPO_URL="https://packages.microsoft.com/config/ubuntu/22.04/mssql-server-2025.list"
            ;;
    esac

    curl -fsSL "$REPO_URL" | tee /etc/apt/sources.list.d/mssql-server-2025.list
    
    log_info "Actualizando lista de paquetes con nuevo repositorio..."
    apt-get update -y
}

install_sql_server() {
    log_info "Instalando SQL Server 2025..."
    apt-get install -y mssql-server
}

configure_sql_server() {
    log_info "================================================================="
    log_info "CONFIGURACIÓN DE SQL SERVER"
    log_info "================================================================="
    log_info "Ejecutando configuración inicial..."
    
    # Verificar si ya está configurado
    if systemctl is-active --quiet mssql-server 2>/dev/null; then
        log_warn "SQL Server ya parece estar configurado y corriendo"
        read -p "¿Deseas reconfigurar? (s/N): " reconfig
        if [[ ! "$reconfig" =~ ^[Ss]$ ]]; then
            return 0
        fi
    fi

    # Ejecutar setup
    /opt/mssql/bin/mssql-conf setup
}

install_tools() {
    log_info "================================================================="
    log_info "INSTALACIÓN DE HERRAMIENTAS DE LÍNEA DE COMANDOS (sqlcmd)"
    log_info "================================================================="
    
    read -p "¿Instalar herramientas de línea de comandos (sqlcmd, bcp)? (s/N): " install_tools
    
    if [[ "$install_tools" =~ ^[Ss]$ ]]; then
        # Agregar repo para herramientas
        curl -fsSL https://packages.microsoft.com/config/ubuntu/$UBUNTU_VERSION/prod.list | \
            tee /etc/apt/sources.list.d/mssql-release.list
        
        apt-get update -y
        
        # Aceptar licencia e instalar
        ACCEPT_EULA=Y apt-get install -y mssql-tools18 unixodbc-dev
        
        # Agregar al PATH
        if ! grep -q "/opt/mssql-tools18/bin" ~/.bashrc 2>/dev/null; then
            echo 'export PATH="$PATH:/opt/mssql-tools18/bin"' >> ~/.bashrc
            log_info "Se agregó sqlcmd al PATH en ~/.bashrc"
        fi
        
        export PATH="$PATH:/opt/mssql-tools18/bin"
        log_info "Herramientas instaladas correctamente"
    fi
}

configure_firewall() {
    log_info "================================================================="
    log_info "CONFIGURACIÓN DE FIREWALL"
    log_info "================================================================="
    
    read -p "¿Configurar UFW para permitir conexiones a SQL Server (puerto 1433)? (s/N): " config_fw
    
    if [[ "$config_fw" =~ ^[Ss]$ ]]; then
        if command -v ufw &> /dev/null; then
            ufw allow 1433/tcp
            log_info "Puerto 1433 abierto en UFW"
        else
            log_warn "UFW no está instalado. Instalando..."
            apt-get install -y ufw
            ufw allow 1433/tcp
            ufw --force enable
            log_info "UFW habilitado y puerto 1433 abierto"
        fi
    fi
}

show_status() {
    log_info "================================================================="
    log_info "ESTADO DE LA INSTALACIÓN"
    log_info "================================================================="
    
    if systemctl is-active --quiet mssql-server; then
        log_info "✅ SQL Server está corriendo"
        systemctl status mssql-server --no-pager -l
    else
        log_error "❌ SQL Server no está corriendo"
        log_info "Puedes iniciarlo con: sudo systemctl start mssql-server"
    fi
    
    echo ""
    log_info "Puertos en escucha:"
    ss -tlnp | grep sqlservr || netstat -tlnp 2>/dev/null | grep sqlservr || true
    
    echo ""
    log_info "================================================================="
    log_info "INFORMACIÓN ÚTIL"
    log_info "================================================================="
    echo ""
    echo "  📁 Datos de SQL Server: /var/opt/mssql/"
    echo "  📁 Configuración:      /var/opt/mssql/mssql.conf"
    echo "  📁 Logs:               /var/opt/mssql/log/"
    echo "  🔌 Puerto por defecto:  1433"
    echo ""
    echo "  Comandos útiles:"
    echo "    sudo systemctl status mssql-server    # Ver estado"
    echo "    sudo systemctl start mssql-server     # Iniciar"
    echo "    sudo systemctl stop mssql-server      # Detener"
    echo "    sudo systemctl restart mssql-server   # Reiniciar"
    echo "    sudo /opt/mssql/bin/mssql-conf list   # Ver configuración"
    echo ""
    echo "  Para conectarte localmente:"
    echo "    sqlcmd -S localhost -U sa -P 'tu_password'"
    echo ""
}

# =============================================================================
# MAIN
# =============================================================================

main() {
    echo "================================================================="
    echo "     INSTALADOR DE SQL SERVER 2025 PARA UBUNTU"
    echo "================================================================="
    echo ""
    
    check_root
    detect_ubuntu_version
    
    echo ""
    read -p "¿Continuar con la instalación? (s/N): " confirm
    if [[ ! "$confirm" =~ ^[Ss]$ ]]; then
        log_info "Instalación cancelada"
        exit 0
    fi
    
    install_dependencies
    add_microsoft_repo
    install_sql_server
    configure_sql_server
    install_tools
    configure_firewall
    show_status
    
    log_info "================================================================="
    log_info "¡INSTALACIÓN COMPLETADA!"
    log_info "================================================================="
}

# Ejecutar main
main "$@"
