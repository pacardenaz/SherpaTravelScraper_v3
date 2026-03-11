-- ============================================================
-- SCHEMA SQL SERVER - Sistema de Extracción de Requisitos de Viaje
-- Sherpa Travel Scraper
-- ============================================================

-- Tabla de control de ejecuciones
CREATE TABLE TXNET_REQVIAJES (
    reqv_id INT IDENTITY(1,1) PRIMARY KEY,
    reqv_fecha_inicio DATETIME2 NOT NULL DEFAULT GETDATE(),
    reqv_fecha_fin DATETIME2 NULL,
    reqv_estado CHAR(1) NOT NULL DEFAULT 'P' CHECK (reqv_estado IN ('P', 'C', 'E', 'B')),
    reqv_total_combinaciones INT NOT NULL DEFAULT 0,
    reqv_combinaciones_ok INT NOT NULL DEFAULT 0,
    reqv_combinaciones_fallidas INT NOT NULL DEFAULT 0,
    reqv_proxy_usado NVARCHAR(100) NULL
);
GO

-- Tabla de catálogo de nacionalidades
CREATE TABLE txnet_renacionalidades (
    rena_id INT IDENTITY(1,1) PRIMARY KEY,
    rena_nacionalidad NVARCHAR(3) NOT NULL UNIQUE,
    rena_tiponacionalidad NVARCHAR(10) NOT NULL CHECK (rena_tiponacionalidad IN ('ORIGEN', 'DESTINO', 'AMBOS')),
    rena_nacionalidadISO2 NVARCHAR(2) NOT NULL,
    rena_idioma_default NVARCHAR(5) NOT NULL DEFAULT 'EN-US',
    rena_activo BIT NOT NULL DEFAULT 1
);
GO

-- Tabla de cola de trabajo (combinaciones a procesar)
CREATE TABLE txnet_combinaciones_procesar (
    comb_id INT IDENTITY(1,1) PRIMARY KEY,
    comb_reqv_id INT NOT NULL,
    comb_origen NVARCHAR(3) NOT NULL,
    comb_destino NVARCHAR(3) NOT NULL,
    comb_idioma NVARCHAR(5) NOT NULL DEFAULT 'EN-US',
    comb_estado CHAR(1) NOT NULL DEFAULT 'P' CHECK (comb_estado IN ('P', 'E', 'C', 'F', 'B')),
    comb_reintentos INT NOT NULL DEFAULT 0,
    comb_fecha_procesamiento DATETIME2 NULL,
    comb_mensaje_error NVARCHAR(MAX) NULL,
    CONSTRAINT FK_combinaciones_ejecucion FOREIGN KEY (comb_reqv_id) REFERENCES TXNET_REQVIAJES(reqv_id),
    CONSTRAINT UQ_combinacion_unica UNIQUE (comb_reqv_id, comb_origen, comb_destino, comb_idioma),
    CONSTRAINT CHK_comb_origen_destino_diferentes CHECK (comb_origen != comb_destino)
);
GO

-- Tabla de resultados detallados
CREATE TABLE txnet_detrequisitos (
    reqvd_id INT IDENTITY(1,1) PRIMARY KEY,
    reqvd_reqv_id INT NOT NULL,
    reqvd_nacionalidad_origen NVARCHAR(3) NOT NULL,
    reqvd_destino NVARCHAR(3) NOT NULL,
    reqvd_requisitos_destino NVARCHAR(MAX) NULL,
    reqvd_requisitos_visado NVARCHAR(MAX) NULL,
    reqvd_pasaportes_documentos NVARCHAR(MAX) NULL,
    reqvd_sanitarios NVARCHAR(MAX) NULL,
    reqvd_idioma_consultado NVARCHAR(5) NOT NULL,
    reqvd_url_completa NVARCHAR(MAX) NOT NULL,
    reqvd_html_raw NVARCHAR(MAX) NULL,
    reqvd_fecha_consulta DATETIME2 NOT NULL DEFAULT GETDATE(),
    reqvd_exito BIT NOT NULL DEFAULT 0,
    reqvd_mensaje_error NVARCHAR(MAX) NULL,
    CONSTRAINT FK_detalles_ejecucion FOREIGN KEY (reqvd_reqv_id) REFERENCES TXNET_REQVIAJES(reqv_id)
);
GO

-- Índices para optimización
CREATE INDEX IX_combinaciones_estado_reintentos ON txnet_combinaciones_procesar(comb_estado, comb_reintentos);
GO

CREATE INDEX IX_combinaciones_pendientes ON txnet_combinaciones_procesar(comb_reqv_id, comb_estado, comb_reintentos) 
INCLUDE (comb_origen, comb_destino, comb_idioma);
GO

CREATE INDEX IX_detalles_ejecucion_consulta ON txnet_detrequisitos(reqvd_reqv_id, reqvd_nacionalidad_origen, reqvd_destino);
GO

CREATE INDEX IX_detalles_exito ON txnet_detrequisitos(reqvd_exito) WHERE reqvd_exito = 1;
GO

PRINT 'Schema creado exitosamente';
