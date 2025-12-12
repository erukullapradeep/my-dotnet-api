using Dapper;
using System.Data;
using System.Data.SqlClient;

public class RfqRepository
{
    private readonly IConfiguration _config;
    public RfqRepository(IConfiguration config) => _config = config;

    private IDbConnection Connection =>
        new SqlConnection(_config.GetConnectionString("DefaultConnection"));

    public async Task<RfqModel> GetRfq(int id)
    {
        using var db = Connection;

        string sql = @"
            SELECT r.*, 
                   c.Id, c.Name, c.Address, c.Phone, c.Email
            FROM Rfq r
            JOIN Customers c ON c.Id = r.CustomerId
            WHERE r.Id = @Id";

        var result = await db.QueryAsync<RfqModel, CustomerModel, RfqModel>(
            sql,
            (r, c) => { r.Customer = c; return r; },
            new { Id = id },
            splitOn: "Id"
        );

        var rfq = result.FirstOrDefault();
        if (rfq == null) return null;

        var items = await db.QueryAsync<RfqItemModel>(
            "SELECT * FROM RfqItems WHERE RfqId = @Id",
            new { Id = id }
        );

        rfq.Items = items.ToList();
        return rfq;
    }
}
