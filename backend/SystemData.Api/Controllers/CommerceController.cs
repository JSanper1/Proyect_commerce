using System.Globalization;
using System.Text;
using SystemData.Api.Data;
using SystemData.Api.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;

namespace SystemData.Api.Controllers;

[ApiController]
[Route("api/commerce")]
public sealed class CommerceController(CommerceRepository repository, ILogger<CommerceController> logger) : ControllerBase
{
    private const long MaxFileSize = 25 * 1024 * 1024;

    [HttpPost("upload")]
    [RequestSizeLimit(MaxFileSize + 64 * 1024)]
    public async Task<IActionResult> Upload([FromForm] IFormFile? file, CancellationToken cancellationToken)
    {
        if (file is null || file.Length == 0) return BadRequest(new { message = "Selecciona un archivo CSV que no esté vacío." });
        if (file.Length > MaxFileSize) return BadRequest(new { message = "El archivo supera el límite de 25 MB." });
        var match = System.Text.RegularExpressions.Regex.Match(file.FileName, "^commerce_(\\d{8})\\.csv$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (!match.Success || !DateOnly.TryParseExact(match.Groups[1].Value, "ddMMyyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
            return BadRequest(new { message = "El archivo debe llamarse commerce_DDMMYYYY.csv y contener una fecha válida." });

        try
        {
            await using var stream = file.OpenReadStream();
            using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            var content = await reader.ReadToEndAsync(cancellationToken);
            var rows = ParseCsv(content);
            if (rows.Count == 0) return BadRequest(new { message = "El CSV no contiene filas de datos." });
            var inserted = await repository.InsertAsync(rows, cancellationToken);
            return Ok(new { message = "Archivo cargado correctamente.", inserted, fileName = file.FileName });
        }
        catch (FormatException exception) { return BadRequest(new { message = exception.Message }); }
        catch (SqlException exception)
        {
            logger.LogError(exception, "No se pudo guardar el archivo de comercios.");
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { message = "No se pudo conectar con SQL Server. Verifica la base de datos y ejecuta el script de instalación." });
        }
    }

    [HttpPost("process")]
    public async Task<IActionResult> Process([FromBody] ProcessCommerceRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var quarantined = await repository.ProcessAsync(request.ProcessDate, cancellationToken);
            return Ok(new { processDate = request.ProcessDate, quarantined, message = $"Se enviaron {quarantined} registros a cuarentena." });
        }
        catch (SqlException exception)
        {
            logger.LogError(exception, "No se pudo procesar la fecha {ProcessDate}.", request.ProcessDate);
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { message = "No se pudo conectar con SQL Server. Verifica la base de datos y ejecuta el script de instalación." });
        }
    }

    [HttpGet("quarantine")]
    public async Task<IActionResult> Quarantine(CancellationToken cancellationToken)
    {
        try { return Ok(await repository.GetQuarantineAsync(cancellationToken)); }
        catch (SqlException exception)
        {
            logger.LogError(exception, "No se pudo consultar la cuarentena.");
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { message = "No se pudo conectar con SQL Server. Verifica la base de datos y ejecuta el script de instalación." });
        }
    }

    private static List<CommerceCsvRow> ParseCsv(string csv)
    {
        var records = ReadRecords(csv);
        if (records.Count < 2) return [];
        var headers = records[0].Select(value => value.Trim().TrimStart('\uFEFF')).ToArray();
        if (headers.Any(string.IsNullOrWhiteSpace)) throw new FormatException("Todos los encabezados del CSV deben tener un nombre.");
        if (headers.Distinct(StringComparer.OrdinalIgnoreCase).Count() != headers.Length)
            throw new FormatException("El CSV contiene encabezados duplicados.");
        // El reto no proporciona un CSV oficial. El ejemplo conocido tiene exactamente 3 campos.
        if (headers.Length != 3)
            throw new FormatException("El CSV de esta versión debe contener exactamente las tres columnas requeridas.");
        var processIndex = Array.FindIndex(headers, h => h.Equals("pc_processdate", StringComparison.OrdinalIgnoreCase));
        var nameIndex = Array.FindIndex(headers, h => h.Equals("pc_nomcomred", StringComparison.OrdinalIgnoreCase));
        var documentIndex = Array.FindIndex(headers, h => h.Equals("pc_numdoc", StringComparison.OrdinalIgnoreCase));
        if (processIndex < 0 || nameIndex < 0 || documentIndex < 0)
            throw new FormatException("Faltan columnas requeridas: pc_processdate, pc_nomcomred y pc_numdoc.");

        var result = new List<CommerceCsvRow>(records.Count - 1);
        for (var i = 1; i < records.Count; i++)
        {
            var values = records[i];
            if (values.All(string.IsNullOrWhiteSpace)) continue;
            if (values.Count != headers.Length) throw new FormatException($"La fila {i + 1} tiene {values.Count} valores y se esperaban {headers.Length}.");
            var rawDate = values[processIndex].Trim();
            var validDate = DateOnly.TryParse(rawDate, CultureInfo.InvariantCulture, DateTimeStyles.None, out var processDate) ||
                DateOnly.TryParse(rawDate, CultureInfo.GetCultureInfo("es-EC"), DateTimeStyles.None, out processDate) ||
                DateOnly.TryParseExact(rawDate, ["dd/MM/yyyy", "dd-MM-yyyy", "ddMMyyyy", "yyyyMMdd"], CultureInfo.InvariantCulture, DateTimeStyles.None, out processDate);
            if (!validDate)
                throw new FormatException($"La fecha pc_processdate de la fila {i + 1} no es válida: '{rawDate}'.");
            if (values[nameIndex].Length > 4000 || values[documentIndex].Length > 4000)
                throw new FormatException($"La fila {i + 1} supera el máximo de 4000 caracteres por campo.");
            result.Add(new CommerceCsvRow(processDate, values[nameIndex], values[documentIndex]));
        }
        return result;
    }

    private static List<List<string>> ReadRecords(string input)
    {
        var records = new List<List<string>>();
        var record = new List<string>();
        var field = new StringBuilder();
        var quoted = false;
        for (var i = 0; i < input.Length; i++)
        {
            var ch = input[i];
            if (quoted)
            {
                if (ch == '"' && i + 1 < input.Length && input[i + 1] == '"') { field.Append('"'); i++; }
                else if (ch == '"') quoted = false;
                else field.Append(ch);
            }
            else if (ch == '"' && field.Length == 0) quoted = true;
            else if (ch == '"') throw new FormatException("El CSV contiene comillas fuera de un campo entrecomillado.");
            else if (ch == ',') { record.Add(field.ToString()); field.Clear(); }
            else if (ch is '\r' or '\n')
            {
                if (ch == '\r' && i + 1 < input.Length && input[i + 1] == '\n') i++;
                record.Add(field.ToString()); field.Clear();
                records.Add(record); record = [];
            }
            else field.Append(ch);
        }
        if (quoted) throw new FormatException("El CSV contiene un campo entre comillas sin cerrar.");
        if (field.Length > 0 || record.Count > 0) { record.Add(field.ToString()); records.Add(record); }
        return records;
    }
}
