-- ============================================================
-- DATOS DE PRUEBA - Nacionalidades
-- ============================================================

INSERT INTO txnet_renacionalidades (rena_nacionalidad, rena_tiponacionalidad, rena_nacionalidadISO2, rena_idioma_default, rena_activo)
VALUES 
    ('COL', 'AMBOS', 'CO', 'ES', 1),
    ('USA', 'AMBOS', 'US', 'EN-US', 1),
    ('MEX', 'AMBOS', 'MX', 'ES', 1),
    ('ESP', 'DESTINO', 'ES', 'ES', 1),
    ('BRA', 'ORIGEN', 'BR', 'PT-BR', 1),
    ('ARG', 'AMBOS', 'AR', 'ES', 1),
    ('CAN', 'AMBOS', 'CA', 'EN-US', 1),
    ('GBR', 'DESTINO', 'GB', 'EN-US', 1),
    ('FRA', 'DESTINO', 'FR', 'EN-US', 1),
    ('DEU', 'DESTINO', 'DE', 'EN-US', 1);
GO

PRINT 'Datos de prueba insertados exitosamente';
PRINT '';
PRINT 'Combinaciones esperadas para matriz NxN:';
PRINT '- 10 países generan 90 combinaciones geográficas (10 x 9)';
PRINT '- Con fallback EN-US: hasta 180 combinaciones totales';
