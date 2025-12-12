using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using iText.Kernel.Pdf;
using iText.Layout;
using iText.Layout.Element;
using iText.Layout.Properties;
using iText.Kernel.Colors;
using iText.Kernel.Geom;
using System.Data.SqlClient;
using Dapper;
using System.IO;

[Authorize] // 🔐 Auth enabled
[ApiController]
[Route("api/[controller]")]
public class RFQController : ControllerBase
{
    private readonly IWebHostEnvironment _env;
    private readonly IConfiguration _config;
    private readonly RfqRepository _repo;

    public RFQController(IWebHostEnvironment env, IConfiguration config, RfqRepository repo)
    {
        _env = env;
        _config = config;
        _repo = repo;
    }

    // =========================================================
    // GET SINGLE RFQ
    // =========================================================
    [HttpGet("{id}")]
    public async Task<IActionResult> GetRfqData(int id)
    {
        var model = await _repo.GetRfq(id);
        if (model == null)
            return NotFound("RFQ not found");

        return Ok(model);
    }

    // =========================================================
    // GET RFQ LIST
    // =========================================================
    [HttpGet("list")]
    public async Task<IActionResult> GetAllRfqIds()
    {
        using var db = new SqlConnection(_config.GetConnectionString("DefaultConnection"));
        var ids = await db.QueryAsync<int>("SELECT Id FROM Rfq ORDER BY Id DESC");
        return Ok(ids);
    }

    // =========================================================
    // UPDATE RFQ
    // =========================================================
    [HttpPut("{id}")]
    public async Task<IActionResult> UpdateRfq(int id, [FromBody] RfqModel model)
    {
        if (id != model.Id)
            return BadRequest("RFQ ID mismatch");

        using var db = new SqlConnection(_config.GetConnectionString("DefaultConnection"));

        // Update RFQ
        string updateRfq = @"
            UPDATE Rfq SET 
                QuoteNumber = @QuoteNumber,
                RfqDate = NULLIF(@RfqDate, ''),
                QuoteDate = NULLIF(@QuoteDate, ''),
                ValidUntil = NULLIF(@ValidUntil, ''),
                Description = @Description,
                Remarks = @Remarks
            WHERE Id = @Id";

        await db.ExecuteAsync(updateRfq, model);

        // Update Customer
        string updateCust = @"
            UPDATE Customers SET
                Name = @Name,
                Address = @Address,
                Phone = @Phone,
                Email = @Email
            WHERE Id = @Id";

        await db.ExecuteAsync(updateCust, new
        {
            Id = model.Customer.Id,
            model.Customer.Name,
            model.Customer.Address,
            model.Customer.Phone,
            model.Customer.Email
        });

        // Delete OLD items
        await db.ExecuteAsync("DELETE FROM RfqItems WHERE RfqId = @Id", new { Id = id });

        // Insert items
        foreach (var item in model.Items)
        {
            await db.ExecuteAsync(
                @"INSERT INTO RfqItems (RfqId, ItemNo, Description, Qty, Rate)
                  VALUES (@RfqId, @ItemNo, @Description, @Qty, @Rate)",
                new
                {
                    RfqId = id,
                    item.ItemNo,
                    item.Description,
                    item.Qty,
                    item.Rate
                }
            );
        }

        return Ok(new { message = "RFQ updated successfully!" });
    }

    // =========================================================
    // CREATE RFQ
    // =========================================================
    [HttpPost]
    public async Task<IActionResult> CreateRfq([FromBody] RfqModel model)
    {
        using var db = new SqlConnection(_config.GetConnectionString("DefaultConnection"));

        // Insert Customer
        string insertCustomer = @"
            INSERT INTO Customers (Name, Address, Phone, Email)
            VALUES (@Name, @Address, @Phone, @Email);
            SELECT CAST(SCOPE_IDENTITY() as int);";

        int customerId = await db.ExecuteScalarAsync<int>(insertCustomer, model.Customer);

        // Insert RFQ
        string insertRfq = @"
            INSERT INTO Rfq (QuoteNumber, RfqDate, QuoteDate, ValidUntil, Description, Remarks, CustomerId)
            VALUES (@QuoteNumber, @RfqDate, @QuoteDate, @ValidUntil, @Description, @Remarks, @CustomerId);
            SELECT CAST(SCOPE_IDENTITY() as int);";

        int newRfqId = await db.ExecuteScalarAsync<int>(insertRfq, new
        {
            model.QuoteNumber,
            model.RfqDate,
            model.QuoteDate,
            model.ValidUntil,
            model.Description,
            model.Remarks,
            CustomerId = customerId
        });

        // Insert Items
        foreach (var item in model.Items)
        {
            await db.ExecuteAsync(
                @"INSERT INTO RfqItems (RfqId, ItemNo, Description, Qty, Rate)
                  VALUES (@RfqId, @ItemNo, @Description, @Qty, @Rate)",
                new
                {
                    RfqId = newRfqId,
                    item.ItemNo,
                    item.Description,
                    item.Qty,
                    item.Rate
                }
            );
        }

        return Ok(new { message = "RFQ created", id = newRfqId });
    }

    // =========================================================
    // PDF GENERATION
    // =========================================================
    [HttpGet("{id}/pdf")]
    public async Task<IActionResult> GeneratePdf(int id)
    {
        var model = await _repo.GetRfq(id);
        if (model == null)
            return NotFound("RFQ not found");

        string outputDir = System.IO.Path.Combine(_env.ContentRootPath, "PdfOutputs");
        Directory.CreateDirectory(outputDir);

        string filePath = System.IO.Path.Combine(outputDir, $"RFQ-{id}.pdf");

        using var writer = new PdfWriter(filePath);
        using var pdf = new PdfDocument(writer);
        var doc = new Document(pdf, PageSize.A4);
        doc.SetMargins(40, 40, 40, 40);

        // Title
        doc.Add(new Paragraph("Quotation")
            .SetFontSize(22)
            .SetBold()
            .SetTextAlignment(TextAlignment.CENTER));

        doc.Add(new Paragraph("\n"));

        // Header
        doc.Add(new Paragraph($"RFQ Date: {model.RfqDate}").SetBold());
        doc.Add(new Paragraph($"Quote #: {model.QuoteNumber}"));
        doc.Add(new Paragraph($"Quote Date: {model.QuoteDate}"));
        doc.Add(new Paragraph("\n"));

        // Customer Info
        doc.Add(new Paragraph("Customer Information")
            .SetBold()
            .SetBackgroundColor(new DeviceRgb(221, 155, 68))
            .SetPadding(5));

        doc.Add(new Paragraph(model.Customer.Name));
        doc.Add(new Paragraph(model.Customer.Address));
        doc.Add(new Paragraph($"{model.Customer.Phone} | {model.Customer.Email}"));
        doc.Add(new Paragraph("\n"));

        // Description
        doc.Add(new Paragraph("Description")
            .SetBold()
            .SetBackgroundColor(new DeviceRgb(221, 155, 68))
            .SetPadding(5));

        doc.Add(new Paragraph(model.Description));
        doc.Add(new Paragraph("\n"));

        // Table
        var table = new Table(UnitValue.CreatePercentArray(new float[] { 1, 4, 1, 1, 1, 1 }))
            .UseAllAvailableWidth();

        table.AddHeaderCell("Item No");
        table.AddHeaderCell("Description");
        table.AddHeaderCell("Qty");
        table.AddHeaderCell("Rate");
        table.AddHeaderCell("Ext Cost");
        table.AddHeaderCell("Remarks");

        decimal total = 0;

        foreach (var item in model.Items)
        {
            decimal ext = item.Qty * item.Rate;
            total += ext;

            table.AddCell(item.ItemNo);
            table.AddCell(item.Description);
            table.AddCell(item.Qty.ToString());
            table.AddCell(item.Rate.ToString("0.00"));
            table.AddCell(ext.ToString("0.00"));
            table.AddCell("");
        }

        table.AddCell(new Cell(1, 4).Add(new Paragraph("TOTAL").SetBold()).SetTextAlignment(TextAlignment.RIGHT));
        table.AddCell(total.ToString("0.00"));
        table.AddCell("");

        doc.Add(table);

        doc.Add(new Paragraph("\nRemarks")
            .SetBold()
            .SetBackgroundColor(new DeviceRgb(221, 155, 68))
            .SetPadding(5));

        doc.Add(new Paragraph(model.Remarks));

        doc.Close();

        var bytes = System.IO.File.ReadAllBytes(filePath);
        return File(bytes, "application/pdf", $"RFQ-{id}.pdf");
    }
}
