using MyClinic.Models;
using System.Text.Json;

namespace MyClinic;

public sealed class FinanceExpenseRow
{
    public int Id { get; init; }
    public DateTime Date { get; init; }
    public string Description { get; init; } = "";
    public string Category { get; init; } = "";
    public decimal Amount { get; init; }
    public string DateText => Date.ToString("dd/MM/yyyy");
    public string AmountText => FinanceMoney.Format(Amount);
}

public sealed class FinanceUpcomingRow
{
    public int Id { get; init; }
    public string Description { get; init; } = "";
    public string Category { get; init; } = "";
    public string Recipient { get; init; } = "";
    public DateTime? Due { get; init; }
    public string Status { get; init; } = "Pending";
    public decimal Amount { get; init; }
    public int? ExpenseId { get; init; }
    public DateTime? CompletedAt { get; init; }
    public bool Pending => Status == "Pending";
    public DateTime DueSort => Due ?? DateTime.MaxValue;
    public int StatusOrder => !Pending ? (Status == "Paid" ? 4 : 5) : Due == null ? 3 : Due.Value.Date < AppClock.Today ? 0 : Due.Value.Date == AppClock.Today ? 1 : 2;
    public string StatusText => StatusOrder switch { 0 => "متأخرة", 1 => "اليوم", 2 => "قريباً", 3 => "بدون موعد", 4 => "مسددة", _ => "ملغاة" };
    public string DueText => Due?.ToString("dd/MM/yyyy") ?? "بدون موعد";
    public string AmountText => FinanceMoney.Format(Amount);
}

public sealed class FinanceAuditRow
{
    public int Id { get; init; }
    public DateTime OccurredAt { get; init; }
    public string Actor { get; init; } = "";
    public string Action { get; init; } = "";
    public string Description { get; init; } = "";
    public string Reason { get; init; } = "";
    public string BeforeText { get; init; } = "";
    public string AfterText { get; init; } = "";
    public string DateText => OccurredAt.ToString("dd/MM/yyyy HH:mm");
    public static FinanceAuditRow From(FinancialAuditEntry entry) => new() { Id = entry.Id, OccurredAt = entry.OccurredAt,
        Actor = entry.Actor, Action = entry.Action, Description = entry.Description, Reason = entry.Reason,
        BeforeText = Describe(entry.BeforeJson), AfterText = Describe(entry.AfterJson) };

    private static string Describe(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return "—";
        var labels = new Dictionary<string, string> { ["ExpenseDate"] = "تاريخ المصروف", ["Description"] = "التفاصيل",
            ["Amount"] = "المبلغ", ["Recipient"] = "المستفيد", ["DueDate"] = "الاستحقاق", ["Status"] = "الحالة",
            ["CompletedAt"] = "تاريخ الإتمام", ["ExpenseId"] = "رقم المصروف", ["Balance"] = "الرصيد",
            ["Forgiveness"] = "مسامحة", ["VisitDate"] = "تاريخ العملية" };
        try
        {
            using var doc = JsonDocument.Parse(json);
            return string.Join("  •  ", doc.RootElement.EnumerateObject().Select(p =>
            {
                string value = p.Value.ToString();
                if (p.Name == "Status") value = value switch { "Pending" => "معلقة", "Paid" => "مسددة", "Cancelled" => "ملغاة", _ => value };
                else if (p.Name is "Amount" or "Balance" && p.Value.TryGetDecimal(out var number)) value = FinanceMoney.Format(number);
                else if (p.Name is "ExpenseDate" or "DueDate" or "CompletedAt" or "VisitDate" && DateTime.TryParse(value, out var date)) value = date.ToString("dd/MM/yyyy HH:mm");
                return $"{labels.GetValueOrDefault(p.Name, p.Name)}: {(value.Length == 0 ? "—" : value)}";
            }));
        }
        catch (JsonException) { return json; }
    }
}
