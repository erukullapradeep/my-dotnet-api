using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Dapper;
using System.Data.SqlClient;
using System.Security.Cryptography;
using rfq_api.Models;

[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly IConfiguration _config;

    public AuthController(IConfiguration config)
    {
        _config = config;
    }

    // --------------------------------------------------------
    // LOGIN
    // --------------------------------------------------------
    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest model)
    {
        if (model == null)
            return BadRequest("Invalid request");

        if (string.IsNullOrWhiteSpace(model.Username) ||
            string.IsNullOrWhiteSpace(model.Password))
            return BadRequest("Username and Password are required");

        using var db = new SqlConnection(_config.GetConnectionString("DefaultConnection"));

        var user = await db.QueryFirstOrDefaultAsync<Users>(
            "SELECT * FROM Users WHERE Username = @Username",
            new { model.Username });

        if (user == null)
            return Unauthorized("Invalid username or password");

        // ❌ Block login if admin has not approved
        if (!user.IsApproved)
            return Unauthorized("Your account is pending admin approval.");

        string hashedInputPassword = Hash(model.Password);

        if (!user.PasswordHash.Equals(hashedInputPassword, StringComparison.OrdinalIgnoreCase))
            return Unauthorized("Invalid username or password");

        return Ok(new { token = GenerateJwt(model.Username) });
    }

    // --------------------------------------------------------
    // REGISTER
    // --------------------------------------------------------
    [HttpPost("register")]
    public async Task<IActionResult> Register([FromBody] RegisterRequest model)
    {
        using var db = new SqlConnection(_config.GetConnectionString("DefaultConnection"));

        var exists = await db.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM Users WHERE Email=@Email OR Username=@Username",
            new { model.Email, model.Username });

        if (exists > 0)
            return BadRequest("User already exists.");

        string hash = BitConverter.ToString(
            SHA256.Create().ComputeHash(Encoding.UTF8.GetBytes(model.Password))
        ).Replace("-", "").ToLower();

        // Insert new unapproved user
        int newUserId = await db.ExecuteScalarAsync<int>(
            @"INSERT INTO Users (Username, Email, PasswordHash, IsApproved)
          OUTPUT INSERTED.Id
          VALUES (@Username, @Email, @PasswordHash, 0)",
            new { model.Username, model.Email, PasswordHash = hash }
        );

        // Create approval link
        string approveUrl = $"http://localhost:5288/api/auth/approve-user/{newUserId}";

        // Send email
        var email = new EmailService(_config);
        email.SendEmail(
            _config["EmailSettings:AdminEmail"],
            "Approve New User",
            $@"A new user has registered:<br><br>
            <b>{model.Username}</b><br>
            Email: {model.Email}<br><br>
            Click the link to approve:<br>
            <a href='{approveUrl}'>{approveUrl}</a>"
        );

        return Ok(new { message = "Registered successfully! Waiting for admin approval." });
    }


    // --------------------------------------------------------
    // FORGOT PASSWORD
    // --------------------------------------------------------
    [HttpPost("forgot-password")]
    public async Task<IActionResult> ForgotPassword([FromBody] ForgotPasswordRequest model)
    {
        using var db = new SqlConnection(_config.GetConnectionString("DefaultConnection"));

        var user = await db.QueryFirstOrDefaultAsync<Users>(
            "SELECT * FROM Users WHERE Email=@Email",
            new { model.Email });

        if (user == null)
            return BadRequest("Email not found");

        string token = Guid.NewGuid().ToString("N");

        await db.ExecuteAsync(
            @"UPDATE Users 
          SET ResetToken=@Token, ResetTokenExpiry=DATEADD(MINUTE, 15, GETDATE()) 
          WHERE Email=@Email",
            new { Token = token, model.Email });

        string resetUrl = $"http://localhost:4200/reset-password/{token}";

        // --------------------------
        // SEND EMAIL TO USER
        // --------------------------
        var emailService = new EmailService(_config);
        emailService.SendEmail(
            user.Email,
            "Reset Password",
            $"Click the link to reset your password:<br><br><a href='{resetUrl}'>{resetUrl}</a>"
        );

        return Ok(new { message = "Reset link sent to your email." });
    }


    // --------------------------------------------------------
    // RESET PASSWORD
    // --------------------------------------------------------
    [HttpPost("reset-password")]
    public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordRequest model)
    {
        using var db = new SqlConnection(_config.GetConnectionString("DefaultConnection"));

        var user = await db.QueryFirstOrDefaultAsync<Users>(
            "SELECT * FROM Users WHERE ResetToken=@Token",
            new { model.Token });

        if (user == null)
            return BadRequest("Invalid token");

        if (user.ResetTokenExpiry < DateTime.Now)
            return BadRequest("Token expired");

        string hash = Hash(model.NewPassword);

        await db.ExecuteAsync(
            "UPDATE Users SET PasswordHash=@Hash, ResetToken=NULL, ResetTokenExpiry=NULL WHERE Id=@Id",
            new { Hash = hash, user.Id });

        return Ok(new { message = "Password updated successfully" });
    }

    // --------------------------------------------------------
    // ADMIN – APPROVE USER
    // --------------------------------------------------------
    [HttpGet("approve-user/{id}")]
    public async Task<IActionResult> ApproveUser(int id)
    {
        using var db = new SqlConnection(_config.GetConnectionString("DefaultConnection"));

        int updated = await db.ExecuteAsync(
            "UPDATE Users SET IsApproved = 1 WHERE Id = @Id",
            new { Id = id });

        if (updated == 0)
            return BadRequest("Invalid user ID");

        return Content("<h2>User Approved Successfully 🎉</h2>", "text/html");
    }


    // --------------------------------------------------------
    // PASSWORD HASH
    // --------------------------------------------------------
    private string Hash(string input)
    {
        return BitConverter.ToString(
            SHA256.Create().ComputeHash(Encoding.UTF8.GetBytes(input))
        ).Replace("-", "").ToLower();
    }

    // --------------------------------------------------------
    // JWT TOKEN
    // --------------------------------------------------------
    private string GenerateJwt(string username)
    {
        var keyStr = _config["Jwt:Key"];
        var key = Encoding.UTF8.GetBytes(keyStr);

        var creds = new SigningCredentials(new SymmetricSecurityKey(key), SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: _config["Jwt:Issuer"],
            audience: _config["Jwt:Audience"],
            claims: new[] { new Claim(ClaimTypes.Name, username) },
            expires: DateTime.Now.AddHours(8),
            signingCredentials: creds
        );

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    [HttpGet("pending-users")]
    public async Task<IActionResult> GetPendingUsers()
    {
        using var db = new SqlConnection(_config.GetConnectionString("DefaultConnection"));

        var users = await db.QueryAsync<Users>(
            "SELECT Id, Username, Email FROM Users WHERE IsApproved = 0");

        return Ok(users);
    }

  

}
