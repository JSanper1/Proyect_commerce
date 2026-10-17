-- SystemData: instalación inicial en una base de datos EXCLUSIVA para pruebas.
-- SQL Server 2017+; ejecutar con SSMS. No ejecutar sobre una BD con información de producción.
IF DB_ID(N'SystemDataDB') IS NULL
    CREATE DATABASE SystemDataDB;
GO
USE SystemDataDB;
GO
-- No convertir silenciosamente el esquema anterior: podría perder datos JSON.
IF COL_LENGTH(N'dbo.commerce', N'payload_json') IS NOT NULL
    THROW 50001, N'Se detectó una versión anterior. Instale en una SystemDataDB nueva/vacía; respalde sus datos antes de migrar.', 1;
GO
IF OBJECT_ID(N'dbo.commerce', N'U') IS NULL
BEGIN
    -- Estructura de datos idéntica a las tres columnas del CSV de ejemplo.
    CREATE TABLE dbo.commerce
    (
        pc_processdate date NOT NULL,
        pc_nomcomred nvarchar(4000) NULL,
        pc_numdoc nvarchar(4000) NULL
    );
    CREATE INDEX IX_commerce_process_date ON dbo.commerce(pc_processdate);
END;
GO
IF OBJECT_ID(N'dbo.commerce_quarantine', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.commerce_quarantine
    (
        id bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_commerce_quarantine PRIMARY KEY,
        pc_processdate date NOT NULL,
        pc_nomcomred nvarchar(4000) NULL,
        pc_numdoc nvarchar(4000) NULL,
        motivo nvarchar(1000) NOT NULL,
        quarantined_at datetime2(0) NOT NULL
            CONSTRAINT DF_commerce_quarantine_at DEFAULT SYSUTCDATETIME()
    );
    CREATE INDEX IX_commerce_quarantine_date ON dbo.commerce_quarantine(pc_processdate);
END;
GO
IF TYPE_ID(N'dbo.CommerceRowType') IS NULL
    EXEC(N'CREATE TYPE dbo.CommerceRowType AS TABLE
    (
        ProcessDate date NOT NULL,
        MerchantName nvarchar(4000) NULL,
        DocumentNumber nvarchar(4000) NULL
    )');
GO
CREATE OR ALTER PROCEDURE dbo.sp_create_commerce
    @Rows dbo.CommerceRowType READONLY
AS
BEGIN
    SET NOCOUNT ON;
    INSERT INTO dbo.commerce (pc_processdate, pc_nomcomred, pc_numdoc)
    SELECT ProcessDate, MerchantName, DocumentNumber FROM @Rows;
    SELECT @@ROWCOUNT AS inserted;
END;
GO
CREATE OR ALTER PROCEDURE dbo.sp_process_commerce
    @ProcessDate date
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    DECLARE @quarantined int;

    BEGIN TRANSACTION;
    -- DELETE ... OUTPUT transfiere exactamente las filas eliminadas, aun con duplicados.
    DELETE FROM dbo.commerce
    OUTPUT
        DELETED.pc_processdate,
        DELETED.pc_nomcomred,
        DELETED.pc_numdoc,
        CONCAT(
            CASE WHEN NULLIF(LTRIM(RTRIM(DELETED.pc_nomcomred)), N'') IS NULL
                THEN N'El nombre del comercio (nomcomred) se encuentra vacío' ELSE N'' END,
            CASE WHEN NULLIF(LTRIM(RTRIM(DELETED.pc_nomcomred)), N'') IS NULL AND
                (NULLIF(LTRIM(RTRIM(DELETED.pc_numdoc)), N'') IS NULL OR
                 DELETED.pc_numdoc COLLATE Latin1_General_100_BIN2 LIKE N'%[^0-9]%')
                THEN N'; ' ELSE N'' END,
            CASE WHEN NULLIF(LTRIM(RTRIM(DELETED.pc_numdoc)), N'') IS NULL
                THEN N'El número (numdoc) se encuentra vacío'
                WHEN DELETED.pc_numdoc COLLATE Latin1_General_100_BIN2 LIKE N'%[^0-9]%'
                THEN N'El número (numdoc) contiene letras o caracteres especiales'
                ELSE N'' END
        )
    INTO dbo.commerce_quarantine (pc_processdate, pc_nomcomred, pc_numdoc, motivo)
    WHERE pc_processdate = @ProcessDate
      AND (NULLIF(LTRIM(RTRIM(pc_nomcomred)), N'') IS NULL
        OR NULLIF(LTRIM(RTRIM(pc_numdoc)), N'') IS NULL
        OR pc_numdoc COLLATE Latin1_General_100_BIN2 LIKE N'%[^0-9]%');
    SET @quarantined = @@ROWCOUNT;
    COMMIT TRANSACTION;
    SELECT @quarantined AS quarantined;
END;
GO
CREATE OR ALTER PROCEDURE dbo.sp_list_commerce_quarantine
AS
BEGIN
    SET NOCOUNT ON;
    SELECT id, pc_processdate AS process_date, pc_nomcomred, pc_numdoc,
           motivo, quarantined_at
    FROM dbo.commerce_quarantine
    ORDER BY quarantined_at DESC, id DESC;
END;
GO
