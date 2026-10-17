# SystemData — Carga y validación de comercios

Aplicación de prueba técnica que permite **previsualizar y registrar archivos CSV**, **validar los registros de una fecha** y **consultar los registros rechazados con su motivo**. Se entrega el código fuente, scripts SQL y pruebas básicas para desplegarlo en un entorno de evaluación independiente.

## 1. Tecnologías y arquitectura

- **Frontend:** Angular 20, TypeScript y CSS.
- **Backend:** ASP.NET Core Web API (.NET 10 / C#).
- **Base de datos:** SQL Server 2017 o posterior (Express es suficiente), con procedimientos almacenados.
- **Administración:** SSMS o herramienta SQL equivalente.
- **Entorno de referencia:** Windows, Visual Studio Community / Visual Studio Code y PowerShell.

```text
Angular (http://localhost:4200)
  → HTTP REST (http://localhost:5111/api/commerce)
    → ASP.NET Core Controller → CommerceRepository
      → Procedimientos almacenados → SQL Server (SystemDataDB)
```

## 2. Estructura del repositorio

```text
backend/SystemData.Api/             API REST y acceso a datos
  Controllers/CommerceController.cs
  Data/CommerceRepository.cs
  Models/CommerceCsvRow.cs
  Program.cs
  appsettings.json
  Properties/launchSettings.json
frontend/systemdata-web/           Aplicación Angular
  src/app/app.ts                 Estado y navegación
  src/app/app.html               Vistas
  src/app/app.css                Estilos
  src/app/services/commerce-api.service.ts  Acceso HTTP
  src/environments/environment.ts          URL de la API
database/01_create_database.sql Creación de BD, tablas, tipo tabla y SP
database/02_verify.sql          Consultas de verificación
examples/commerce_16102026.csv CSV de prueba (12 filas)
tests/smoke.ps1                 Pruebas de integración HTTP
.gitignore
README.md
```

No se incluyen `node_modules`, `bin`, `obj`, `.vs` ni archivos compilados: se regeneran al instalar y compilar. **No se incluyen contraseñas.**

> **Importante:** esta versión utiliza el nombre de base de datos `SystemDataDB` y una identidad nueva de secretos de usuario .NET. Al ejecutar desde otra carpeta o equipo se debe configurar nuevamente `ConnectionStrings:Commerce` y crear la base de datos con el script incluido. Los cambios de nombre no modifican las rutas REST (`/api/commerce`) ni el formato requerido del CSV (`commerce_DDMMYYYY.csv`).

## 3. Requisitos previos

1. Instalar **.NET SDK 10** (`dotnet --version`).
2. Instalar **Node.js 20.19+ o 22.12+ o 24+** y npm (`node --version`, `npm --version`). Para este proyecto se recomienda Node.js 24 LTS.
3. Instalar **SQL Server** local (por ejemplo, Express) y **SSMS**.
4. Git es opcional para ejecutar el proyecto.

> **Usar una base nueva y sin datos reales.** No ejecutar `01_create_database.sql` sobre una instalación anterior de SystemData sin hacer copia de seguridad y migrar: la estructura de las tablas cambió respecto de las versiones preliminares que incluían `payload_json`.

## 4. Instalación de SQL Server

1. Iniciar SQL Server e ingresar a SSMS con una cuenta autorizada para crear bases.
2. Abrir `database/01_create_database.sql` y ejecutar los lotes en una **instalación nueva de `SystemDataDB`**.
3. El script crea:
   - `SystemDataDB`.
   - `dbo.commerce` con las **tres columnas del CSV de ejemplo**: `pc_processdate`, `pc_nomcomred`, `pc_numdoc`.
   - `dbo.commerce_quarantine` con esas columnas y, además, `id`, `motivo` y `quarantined_at`.
   - Tipo de tabla `dbo.CommerceRowType`.
   - `dbo.sp_create_commerce`, `dbo.sp_process_commerce` y `dbo.sp_list_commerce_quarantine`.
4. Ejecutar `database/02_verify.sql` para verificar tablas y procedimientos.

### 4.1. Configurar la conexión sin publicar contraseñas

La API lee `ConnectionStrings:Commerce`. Por defecto, `backend/SystemData.Api/appsettings.json` tiene una **conexión local de ejemplo mediante autenticación integrada**:

```text
Server=localhost\SQLEXPRESS;Database=SystemDataDB;Integrated Security=True;Encrypt=True;TrustServerCertificate=True;
```

Si se utiliza instancia predeterminada, sustituir `localhost\SQLEXPRESS` por `localhost`. Si la autenticación integrada no dispone de permisos, utilizar un usuario SQL **específico para esta base** y guardar su contraseña fuera del repositorio. **No utilizar `sa` para ejecutar la aplicación.**

En la carpeta `backend/SystemData.Api`:

```powershell
dotnet user-secrets set "ConnectionStrings:Commerce" "Server=localhost\SQLEXPRESS;Database=SystemDataDB;User ID=SystemDataApp;Password=REEMPLAZAR_EN_MI_PC;Integrated Security=False;Encrypt=True;TrustServerCertificate=True;"
```

Reemplazar usuario, contraseña e instancia por los del evaluador en su equipo. No copiar esa cadena en Git ni agregarla a `appsettings.json`. En `Development`, User Secrets tiene prioridad sobre `appsettings.json`.

Para configurar permisos mínimos, el administrador de SQL Server puede crear su propio login/user de aplicación, y luego ejecutar los siguientes permisos (adaptar el nombre):

```sql
USE SystemDataDB;
GO
GRANT EXECUTE ON OBJECT::dbo.sp_create_commerce TO SystemDataApp;
GRANT EXECUTE ON OBJECT::dbo.sp_process_commerce TO SystemDataApp;
GRANT EXECUTE ON OBJECT::dbo.sp_list_commerce_quarantine TO SystemDataApp;
GRANT EXECUTE ON TYPE::dbo.CommerceRowType TO SystemDataApp;
GRANT REFERENCES ON TYPE::dbo.CommerceRowType TO SystemDataApp;
```

En SQL Server, `SystemDataApp` debe existir previamente como usuario de la base. No es necesario conceder `db_owner`, `sysadmin` ni permisos directos de modificación de tablas a la API. Para entornos productivos debe configurarse TLS con certificados confiables y quitar `TrustServerCertificate=True`.

## 5. Ejecutar el backend (.NET)

Desde la raíz del repositorio:

```powershell
cd .\backend\SystemData.Api
dotnet restore
dotnet run --launch-profile http
```

Dirección de desarrollo: **http://localhost:5111**. Mantener esta terminal abierta. El esquema OpenAPI se publica en desarrollo (`/openapi/v1.json`); no se incluye una interfaz Swagger UI.

## 6. Ejecutar el frontend (Angular)

En **otra** terminal, desde la raíz del repositorio:

```powershell
cd .\frontend\systemdata-web
npm.cmd ci
npm.cmd start
```

Abrir **http://localhost:4200**. La URL de backend está en `src/environments/environment.ts`. Si se modifica el puerto, actualizarla y ajustar `Cors:AllowedOrigins` del backend al origen del frontend. `npm ci` utiliza las versiones del archivo `package-lock.json`.

## 7. Endpoints REST

Ruta base: `http://localhost:5111/api/commerce`.

| Método | Ruta | Entrada | Respuesta correcta |
|---|---|---|---|
| POST | `/upload` | `multipart/form-data` con campo `file` y nombre `commerce_DDMMYYYY.csv` | HTTP 200: `{ "message": "Archivo cargado correctamente.", "inserted": 12, "fileName": "commerce_16102026.csv" }` |
| POST | `/process` | JSON `{ "processDate": "2026-10-16" }` | HTTP 200: `{ "processDate": "2026-10-16", "quarantined": 5, "message": "Se enviaron 5 registros a cuarentena." }` |
| GET | `/quarantine` | Sin parámetros | HTTP 200: arreglo JSON con `id`, `processDate`, `merchantName`, `documentNumber`, `reason`, `quarantinedAt` |

El POST `/upload` valida que el archivo no esté vacío (retorna **HTTP 400 Bad Request**), que tenga nombre y fecha correctos, que no supere **25 MB**, que incluya exactamente las tres columnas del ejemplo (en cualquier orden) y que las fechas de `pc_processdate` sean válidas. No rechaza por nombre o documento vacío durante la importación: esas reglas pertenecen al procesamiento. Las fechas se almacenan como `date` y `pc_numdoc` como **texto**, para conservar ceros iniciales.

`sp_process_commerce` valida únicamente las filas cuya `pc_processdate` coincide con la fecha recibida. Rechaza nombres vacíos, documentos vacíos y documentos que no contengan exclusivamente dígitos `0–9`. Una operación transaccional mueve las filas a `commerce_quarantine` y conserva uno o ambos motivos en `motivo`; retorna el número de filas trasladadas. Si se vuelve a procesar la misma fecha sin nuevas importaciones, devuelve `0`.

### 7.1. Prueba manual del CSV de ejemplo

1. Desde Angular, seleccionar `examples/commerce_16102026.csv`. Previsualizar las filas y pulsar **Registrar archivo** una sola vez.
2. El formulario se limpia y aparece una notificación breve con **12 filas registradas**.
3. Procesar `2026-10-16`: de las **9 filas** de ese día, **5** van a cuarentena y **4** permanecen en `commerce`.
4. Procesar `2026-10-15`: de las **3 filas** de ese día, **1** va a cuarentena y **2** permanecen en `commerce`.
5. Al finalizar, si era una BD vacía y se cargó **una vez** el CSV: **6 registros en `commerce` y 6 en `commerce_quarantine`**.
6. Comprobar mediante `database/02_verify.sql`.

Para probar de forma reproducible directamente contra la API (solo con una base nueva y vacía):

```powershell
powershell -ExecutionPolicy Bypass -File .\tests\smoke.ps1
```

El script comprueba archivo vacío (HTTP 400), carga (HTTP 200), resultado de proceso (4 rechazados en la fecha de prueba) y listado GET. Deben mantenerse ejecutándose API y SQL Server.

## 8. Compilación y despliegue en ambiente de evaluación

**Backend** (desde la raíz):

```powershell
dotnet publish .\backend\SystemData.Api\SystemData.Api.csproj -c Release -o .\publish\api
```

Para ejecutarlo: `dotnet .\publish\api\SystemData.Api.dll` con la cadena de conexión configurada por variable de entorno `ConnectionStrings__Commerce` o por los mecanismos de configuración de ASP.NET Core. La variable debe definirse en la máquina del evaluador, no en Git. En un servidor real, ejecutar detrás de un proxy inverso/IIS con HTTPS, TLS válido y un usuario de base con mínimos privilegios.

**Frontend**:

```powershell
cd .\frontend\systemdata-web
npm.cmd ci
npm.cmd run build
```

Publicar el contenido generado en `dist/` mediante un servidor web estático (no usar `ng serve` como servidor de producción). Configurar el servidor web para devolver `index.html` en rutas del frontend y cambiar la URL de API para que sea accesible desde el navegador del entorno destino; habilitar solo su origen en CORS. El frontend se compila por separado.

### Seguridad y alcance del reto

- **No publicar secretos ni claves** en `README.md`, `appsettings.json` ni Git.
- Los endpoints están diseñados para **demostración local**, sin autenticación (el inicio de sesión es opcional según el enunciado). **No exponer públicamente sin implementar autenticación, autorización, HTTPS, protección contra abuso y monitoreo**.
- CORS controla el acceso desde navegadores, **no** sustituye autenticación.
- Cada carga es independiente: si se vuelve a subir expresamente un CSV, los registros se insertan de nuevo. No se implementó deduplicación porque el reto no la exige.
- Los campos comerciales se presentan como texto en Angular; evitar registrar datos personales o usar información productiva.

## 9. Limitación por falta del CSV oficial

El enunciado describe explícitamente **solo tres columnas**. Por eso esta entrega está implementada contra el archivo de ejemplo incluido, y `commerce` contiene **exactamente esas tres columnas**. Si la empresa utiliza un `commerce_DDMMYYYY.csv` real con columnas adicionales, deberán incorporarse explícitamente al modelo C#, al tipo tabla y a los procedimientos SQL antes de usar ese archivo. No se afirma compatibilidad con un esquema que no se proporcionó.

## 10. Verificación previa a subir a Git

```powershell
git status
# Ejecutar desde frontend/systemdata-web antes de la entrega:
npm.cmd audit
npm.cmd run build
# Ejecutar desde backend/SystemData.Api:
dotnet build -c Release
```

No incluir carpetas autogeneradas, User Secrets, contraseñas ni archivos del servidor institucional. En particular, ejecutar el audit de npm con acceso al registro: una auditoría sin base de vulnerabilidades actualizada **no demuestra ausencia de vulnerabilidades**.
