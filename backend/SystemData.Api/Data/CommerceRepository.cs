using System.Data;
using SystemData.Api.Models;
using Microsoft.Data.SqlClient;

namespace SystemData.Api.Data;

public sealed class CommerceRepository(IConfiguration configuration)
{
    private readonly string _connectionString = configuration.GetConnectionString("Commerce")
        ?? throw new InvalidOperationException("Falta configurar ConnectionStrings:Commerce.");

    public async Task<int> InsertAsync(IReadOnlyCollection<CommerceCsvRow> rows, CancellationToken ct)
    {
        var table = new DataTable();
        table.Columns.Add("ProcessDate", typeof(DateTime));
        table.Columns.Add("MerchantName", typeof(string));
        table.Columns.Add("DocumentNumber", typeof(string));
        foreach (var row in rows)
            table.Rows.Add(row.ProcessDate.ToDateTime(TimeOnly.MinValue),
                (object?)row.MerchantName ?? DBNull.Value, (object?)row.DocumentNumber ?? DBNull.Value);

        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(ct);
        await using var command = new SqlCommand("dbo.sp_create_commerce", connection)
        {
            CommandType = CommandType.StoredProcedure
        };
        command.Parameters.Add(new SqlParameter("@Rows", SqlDbType.Structured)
        {
            TypeName = "dbo.CommerceRowType", Value = table
        });
        return Convert.ToInt32(await command.ExecuteScalarAsync(ct));
    }

    public async Task<int> ProcessAsync(DateOnly date, CancellationToken ct)
    {
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(ct);
        await using var command = new SqlCommand("dbo.sp_process_commerce", connection)
        {
            CommandType = CommandType.StoredProcedure
        };
        command.Parameters.Add(new SqlParameter("@ProcessDate", SqlDbType.Date)
        {
            Value = date.ToDateTime(TimeOnly.MinValue)
        });
        return Convert.ToInt32(await command.ExecuteScalarAsync(ct));
    }

    public async Task<IReadOnlyList<QuarantinedCommerce>> GetQuarantineAsync(CancellationToken ct)
    {
        var records = new List<QuarantinedCommerce>();
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(ct);
        await using var command = new SqlCommand("dbo.sp_list_commerce_quarantine", connection)
        {
            CommandType = CommandType.StoredProcedure
        };
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            records.Add(new QuarantinedCommerce(
                reader.GetInt64(reader.GetOrdinal("id")),
                reader.IsDBNull(reader.GetOrdinal("process_date")) ? null :
                    DateOnly.FromDateTime(reader.GetDateTime(reader.GetOrdinal("process_date"))),
                reader.IsDBNull(reader.GetOrdinal("pc_nomcomred")) ? null : reader.GetString(reader.GetOrdinal("pc_nomcomred")),
                reader.IsDBNull(reader.GetOrdinal("pc_numdoc")) ? null : reader.GetString(reader.GetOrdinal("pc_numdoc")),
                reader.GetString(reader.GetOrdinal("motivo")),
                reader.GetDateTime(reader.GetOrdinal("quarantined_at"))));
        }
        return records;
    }
}
