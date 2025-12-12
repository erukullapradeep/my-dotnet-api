public class RfqModel
{
    public int Id { get; set; }
    public string? QuoteNumber { get; set; }
    public string? RfqDate { get; set; }     // <-- make nullable
    public string? QuoteDate { get; set; }   // <-- make nullable
    public string? ValidUntil { get; set; }  // <-- nullable
    public string? Description { get; set; }
    public string? Remarks { get; set; }

    public CustomerModel Customer { get; set; } = new();
    public List<RfqItemModel> Items { get; set; } = new();
}
