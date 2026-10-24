using MyClinic.Models;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace MyClinic;

public enum FinancePeriod { Day, Month, Year, Custom }

public readonly record struct FinanceDateRange(DateTime Start, DateTime End)
{
    public bool Contains(DateTime date) => date.Date >= Start && date.Date <= End;
    public string Label => $"{Start:dd/MM/yyyy} — {End:dd/MM/yyyy}";
    public FinanceDateRange Previous => new(Start.AddDays(-(End - Start).Days - 1), Start.AddDays(-1));
    public static FinanceDateRange For(FinancePeriod period, DateTime date, DateTime? end = null) => period switch
    {
        FinancePeriod.Day => new(date.Date, date.Date),
        FinancePeriod.Month => new(new(date.Year, date.Month, 1), new DateTime(date.Year, date.Month, 1).AddMonths(1).AddDays(-1)),
        FinancePeriod.Year => new(new(date.Year, 1, 1), new(date.Year, 12, 31)),
        _ => new(date.Date, (end ?? date).Date)
    };
}

public static class FinanceMoney
{
    public static decimal From(double value) => double.IsFinite(value) ? decimal.Round((decimal)value, 2, MidpointRounding.AwayFromZero) : 0;
    public static string Format(decimal value) => value.ToString("#,0.##", CultureInfo.InvariantCulture) + " ل.س";
    public static bool TryParse(string? input, out decimal value)
    {
        var text = new StringBuilder();
        foreach (char c in (input ?? "").Trim())
        {
            if (c is >= '٠' and <= '٩') text.Append((char)('0' + c - '٠'));
            else if (c is >= '۰' and <= '۹') text.Append((char)('0' + c - '۰'));
            else if (c is '٬' or ',' or ' ' or '\u00a0') continue;
            else text.Append(c == '٫' ? '.' : c);
        }
        return decimal.TryParse(text.ToString(), NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign,
            CultureInfo.InvariantCulture, out value) && value == decimal.Round(value, 2) && Math.Abs(value) <= 1_000_000_000_000m;
    }
    public static void RequirePositive(decimal amount)
    {
        if (amount <= 0 || amount > 1_000_000_000_000m || amount != decimal.Round(amount, 2))
            throw new ArgumentException("أدخل مبلغاً موجباً بمنزلتين عشريتين كحد أقصى.");
    }
}

public sealed class FinanceTreatmentLine
{
    public string TreatmentName { get; init; } = "";
    public decimal Cost { get; init; }
    public decimal Forgiven { get; init; }
    public decimal Paid { get; init; }
    public decimal CreditApplied { get; init; }
    public decimal Remaining { get; init; }
    public string CostText => FinanceMoney.Format(Cost);
    public string ForgivenText => FinanceMoney.Format(Forgiven);
    public string PaidText => FinanceMoney.Format(Paid);
    public string CreditAppliedText => FinanceMoney.Format(CreditApplied);
    public string RemainingText => FinanceMoney.Format(Remaining);
}

public sealed class FinanceVisitRow
{
    public int Id { get; init; }
    public int PatientId { get; init; }
    public DateTime Date { get; init; }
    public string PatientName { get; init; } = "";
    public string Phone { get; init; } = "";
    public bool FinancialOnly { get; init; }
    public string Kind => FinancialOnly ? (Paid > 0 ? "دفعة مستقلة" : "مسامحة") : "زيارة علاجية";
    public decimal Cost { get; init; }
    public decimal Forgiven { get; init; }
    public decimal Paid { get; init; }
    public decimal Balance { get; init; }
    public string Note { get; init; } = "";
    public string DateText => Date.ToString("dd/MM/yyyy");
    public string TimeText => Date.ToString("HH:mm");
    public string CostText => FinanceMoney.Format(Cost);
    public string ForgivenText => FinanceMoney.Format(Forgiven);
    public string PaidText => FinanceMoney.Format(Paid);
    public string BalanceText => Balance < 0 ? $"رصيد دائن {FinanceMoney.Format(-Balance)}" : FinanceMoney.Format(Balance);
    public IReadOnlyList<FinanceTreatmentLine> Treatments { get; init; } = Array.Empty<FinanceTreatmentLine>();
}

public sealed class FinanceAccount
{
    public int PatientId { get; init; }
    public string PatientName { get; init; } = "";
    public string Phone { get; init; } = "";
    public decimal Cost { get; init; }
    public decimal Paid { get; init; }
    public decimal Forgiven { get; init; }
    public decimal Balance => Cost - Paid - Forgiven;
    public DateTime? FirstDebtDate { get; init; }
    public string BalanceText => FinanceMoney.Format(Balance);
    public string FirstDebtDateText => FirstDebtDate?.ToString("dd/MM/yyyy") ?? "—";
    public int AgeDays => FirstDebtDate.HasValue ? Math.Max(0, (AppClock.Today - FirstDebtDate.Value.Date).Days) : 0;
    public string AgeText => $"{AgeDays} يوم";
    public IReadOnlyList<FinanceVisitRow> Rows { get; init; } = Array.Empty<FinanceVisitRow>();
}

public static class FinancialLedger
{
    private sealed class Obligation
    {
        public string Name = "";
        public decimal Cost, Paid, Forgiven;
        public decimal Remaining => Math.Max(0, Cost - Paid - Forgiven);
    }

    // Legacy standalone payments have no clinical fields. Explicit flags disambiguate new entries.
    public static bool IsFinancialOnly(Visit visit) => visit.IsFinancialOnly ||
        (visit.CurrentCost <= 0 && (visit.TodayPaid > 0 || visit.CurrentCost < 0) &&
         string.IsNullOrWhiteSpace(visit.SelectedTreatmentsJson) && string.IsNullOrWhiteSpace(visit.Symptoms) &&
         string.IsNullOrWhiteSpace(visit.Diagnosis) && string.IsNullOrWhiteSpace(visit.FinalTreatment) &&
         string.IsNullOrWhiteSpace(visit.TreatmentPlanNotes) && string.IsNullOrWhiteSpace(visit.PrescriptionJson));

    public static IReadOnlyList<SelectedTreatment> ParseTreatments(string? json)
    {
        try { return JsonSerializer.Deserialize<List<SelectedTreatment>>(json ?? "[]") ?? new(); }
        catch (JsonException) { return Array.Empty<SelectedTreatment>(); }
    }

    public static FinanceAccount BuildAccount(Patient patient, IEnumerable<Visit> visits)
    {
        var queue = new List<Obligation>();
        var rows = new List<FinanceVisitRow>();
        decimal balance = 0, totalCost = 0, totalPaid = 0, totalForgiven = 0, advanceCredit = 0;
        DateTime? firstDebt = null;
        foreach (var visit in visits.OrderBy(v => v.VisitDate).ThenBy(v => v.Id))
        {
            var added = ParseTreatments(visit.SelectedTreatmentsJson).Select(t => new Obligation
            {
                Name = t.TreatmentName,
                Cost = decimal.Round(t.Cost * Math.Max(1, t.Quantity) * (t.Currency == "USD" ? FinanceMoney.From(visit.UsdToSypRateSnapshot > 0 ? visit.UsdToSypRateSnapshot : 15000) : 1), 2)
            }).Where(t => t.Cost > 0).ToList();
            decimal automatic = added.Sum(t => t.Cost), current = FinanceMoney.From(visit.CurrentCost);
            decimal gross = Math.Max(automatic, Math.Max(0, current));
            decimal forgiveness = automatic > 0 ? Math.Max(0, automatic - current) : Math.Max(0, -current);
            if (gross > automatic) added.Add(new() { Name = automatic > 0 ? "تعديل تكلفة الزيارة" : "تكلفة الزيارة", Cost = gross - automatic });
            queue.AddRange(added);
            var touched = new Dictionary<Obligation, (decimal Paid, decimal Forgiven, decimal Credit)>();
            foreach (var item in added) touched[item] = (0, 0, 0);
            decimal discountLeft = forgiveness;
            foreach (var item in current >= 0 ? added : queue)
            {
                decimal applied = Math.Min(item.Remaining, discountLeft);
                if (applied <= 0) continue;
                item.Forgiven += applied; discountLeft -= applied;
                var old = touched.GetValueOrDefault(item);
                touched[item] = (old.Paid, old.Forgiven + applied, old.Credit);
            }
            decimal creditLeft = advanceCredit + discountLeft;
            foreach (var item in queue)
            {
                decimal applied = Math.Min(item.Remaining, creditLeft);
                if (applied <= 0) continue;
                item.Paid += applied; creditLeft -= applied;
                var old = touched.GetValueOrDefault(item);
                touched[item] = (old.Paid, old.Forgiven, old.Credit + applied);
            }
            decimal paid = FinanceMoney.From(visit.TodayPaid), paymentLeft = paid;
            foreach (var item in queue)
            {
                decimal applied = Math.Min(item.Remaining, paymentLeft);
                if (applied <= 0) continue;
                item.Paid += applied; paymentLeft -= applied;
                var old = touched.GetValueOrDefault(item);
                touched[item] = (old.Paid + applied, old.Forgiven, old.Credit);
            }
            advanceCredit = paymentLeft + creditLeft;
            totalCost += gross; totalForgiven += forgiveness; totalPaid += paid;
            decimal previous = balance;
            balance = totalCost - totalPaid - totalForgiven;
            if (balance <= 0) firstDebt = null;
            else if (previous <= 0 || firstDebt == null) firstDebt = visit.VisitDate.Date;
            rows.Add(new()
            {
                Id = visit.Id, PatientId = patient.Id, PatientName = patient.FullName ?? "مريض بدون اسم", Phone = patient.PhoneNumber,
                Date = visit.VisitDate, Cost = gross, Paid = paid, Forgiven = forgiveness, Balance = balance,
                FinancialOnly = IsFinancialOnly(visit), Note = visit.FinancialNote ?? "",
                Treatments = touched.Select(pair => new FinanceTreatmentLine { TreatmentName = pair.Key.Name, Cost = pair.Key.Cost,
                    Forgiven = pair.Value.Forgiven, Paid = pair.Value.Paid, CreditApplied = pair.Value.Credit, Remaining = pair.Key.Remaining }).ToList()
            });
        }
        return new() { PatientId = patient.Id, PatientName = patient.FullName ?? "مريض بدون اسم", Phone = patient.PhoneNumber,
            Cost = totalCost, Paid = totalPaid, Forgiven = totalForgiven, FirstDebtDate = firstDebt, Rows = rows.OrderByDescending(r => r.Date).ThenByDescending(r => r.Id).ToList() };
    }
}
