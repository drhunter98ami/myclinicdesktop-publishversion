using System;

namespace MyClinic.Models;

/// <summary>Append-only history of financial workspace changes, saved with the changed record.</summary>
public class FinancialAuditEntry
{
    public int Id { get; set; }
    public DateTime OccurredAt { get; set; } = AppClock.Now;
    public string Actor { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public string EntityType { get; set; } = string.Empty;
    public int EntityId { get; set; }
    public string Description { get; set; } = string.Empty;
    public string BeforeJson { get; set; } = string.Empty;
    public string AfterJson { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
}
