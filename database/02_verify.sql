USE SystemDataDB;
GO
-- Comprobar después de cargar/procesar el CSV de muestra, UNA sola vez.
SELECT COUNT(*) AS total_commerce FROM dbo.commerce;
SELECT COUNT(*) AS total_quarantine FROM dbo.commerce_quarantine;
SELECT pc_processdate, COUNT(*) AS total FROM dbo.commerce GROUP BY pc_processdate;
SELECT pc_processdate, pc_nomcomred, pc_numdoc, motivo FROM dbo.commerce_quarantine ORDER BY id;
SELECT name FROM sys.procedures WHERE name IN
    (N'sp_create_commerce', N'sp_process_commerce', N'sp_list_commerce_quarantine');
GO
