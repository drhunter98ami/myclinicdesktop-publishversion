namespace MyClinic.Models;

/// <summary>Immutable purchase/payment receipt. Original currency and rate are retained.</summary>
public sealed class ClinicOperation
{
    public int Id { get; set; }
    public string RequestKey { get; set; } = "";
    public string Kind { get; set; } = "";
    public int SourceId { get; set; }
    public string Description { get; set; } = "";
    public int Quantity { get; set; } = 1;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "SYP";
    public decimal RateSnapshot { get; set; }
    public decimal AmountSyp { get; set; }
    public DateTime OccurredAt { get; set; }
    public int? ExpenseId { get; set; }
    public string Actor { get; set; } = "";
}
