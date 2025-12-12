using Microsoft.AspNetCore.Mvc;
using Dapper;
using System.Data.SqlClient;

[Route("api/admin")]
[ApiController]
public class AdminController : ControllerBase
{
    private readonly IConfiguration _config;

    public AdminController(IConfiguration config)
    {
        _config = config;
    }

    [HttpPost("approve-user/{id}")]
    public async Task<IActionResult> ApproveUser(int id)
    {
        using var db = new SqlConnection(_config.GetConnectionString("DefaultConnection"));

        int rows = await db.ExecuteAsync(
            "UPDATE Users SET IsApproved = 1 WHERE Id = @Id",
            new { Id = id });

        if (rows == 0)
            return BadRequest("User not found");

        return Ok(new { message = "User approved successfully" });
    }
}
