using Microsoft.EntityFrameworkCore;
using MyClinic.Models;
using System.Text.Json;

namespace MyClinic;

/// <summary>Source mutation, immutable receipt, expense and audit commit together.</summary>
public sealed class ClinicTransactions(AppDbContext db)
{
    static void Require(bool valid, string message) { if (!valid) throw new InvalidOperationException(message); }
    static void Money(decimal value) => Require(value > 0 && value <= 1_000_000_000_000m, "أدخل قيمة موجبة ضمن الحد المسموح.");
    static void Date(DateTime date) => Require(date.Date <= AppClock.Today, "تاريخ العملية لا يتجاوز اليوم.");
    static void Currency(string currency) => Require(currency is "USD" or "SYP", "اختر العملة.");
    public static decimal ConvertLabAmount(decimal amount, string from, string to, decimal rate)
    {
        Currency(from); Currency(to); Money(rate);
        return from == to ? amount : decimal.Round(from == "USD" ? amount * rate : amount / rate, 4);
    }
    public async Task<int> SaveLabDetailsAsync(int? id, int expectedRevision, LabWorkDetails details)
    {
        Money(details.Cost); Currency(details.Currency); Money(details.ConversionRate);
        Require(!string.IsNullOrWhiteSpace(details.PatientName) && !string.IsNullOrWhiteSpace(details.WorkType), "أدخل اسم المريض ونوع العمل.");
        Require(!id.HasValue || !string.IsNullOrWhiteSpace(details.Reason), "أدخل سبب التصحيح.");
        using var tx = await db.Database.BeginTransactionAsync();
        var work = id.HasValue ? await db.LabWorks.AsNoTracking().SingleOrDefaultAsync(w => w.Id == id) ?? throw new InvalidOperationException("لم يعد العمل موجوداً.") : new LabWork();
        var lab = await db.LabNames.AsNoTracking().SingleOrDefaultAsync(l => l.Id == details.LabId) ?? throw new InvalidOperationException("لم يعد المخبر موجوداً.");
        Require(!lab.IsArchived || (id.HasValue && (work.LabNameId == lab.Id || work.LabName == lab.Name)), "اختر مخبراً نشطاً للأعمال الجديدة.");
        if (details.PatientId.HasValue) Require(await db.Patients.AnyAsync(p => p.Id == details.PatientId), "لم يعد المريض المحدد موجوداً؛ أعد البحث عنه.");
        bool currencyChanged = id.HasValue && work.Currency != details.Currency;
        decimal paid = ConvertLabAmount(work.AmountPaid, work.Currency, details.Currency, details.ConversionRate);
        Require(details.Cost >= paid, "التكلفة بالعملة الجديدة لا تقل عن المدفوع المحوّل.");
        string before = JsonSerializer.Serialize(work);
        string previousSummary = $"{work.PatientName} · {work.LabName} · {work.WorkType} · {work.Cost:N4} {work.Currency} · مدفوع {work.AmountPaid:N4} {work.Currency}";
        if (id.HasValue)
        {
            Require(work.Revision == expectedRevision, "تغير العمل؛ أعد فتح التفاصيل.");
            int claimed = await db.LabWorks.Where(w => w.Id == id && w.Revision == expectedRevision)
                .ExecuteUpdateAsync(s => s.SetProperty(w => w.Revision, expectedRevision + 1));
            Require(claimed == 1, "تغير العمل في نافذة أخرى."); work.Revision++;
        }
        work.PatientName = details.PatientName.Trim(); work.PatientId = details.PatientId; work.LabNameId = lab.Id; work.LabName = lab.Name;
        work.WorkType = details.WorkType.Trim(); work.Cost = details.Cost; work.Currency = details.Currency; work.AmountPaid = paid;
        work.Teeth = details.Teeth; work.Notes = details.Notes.Trim(); work.ExpectedDelivery = details.ExpectedDelivery;
        if (!id.HasValue || currencyChanged) work.RateSnapshot = details.ConversionRate;
        work.Status = work.AmountPaid >= work.Cost ? work.DateReceived.HasValue ? "تم الدفع" : "تم الدفع ولم يتم الاستلام" : work.WorkStatus;
        if (id.HasValue) db.LabWorks.Update(work); else db.LabWorks.Add(work);
        await db.SaveChangesAsync();
        if (id.HasValue)
        {
            string description = $"تصحيح: {details.Reason.Trim()}\nقبل: {previousSummary}\nبعد: {work.PatientName} · {lab.Name} · {work.WorkType} · {work.Cost:N4} {work.Currency} · مدفوع {paid:N4} {work.Currency}";
            if (currencyChanged) description += $"\nتغيير عملة العمل بسعر 1 USD = {details.ConversionRate:N4} SYP. إيصالات الدفعات والمصاريف السابقة محفوظة بقيمها الأصلية.";
            db.ClinicOperations.Add(new() { RequestKey = Guid.NewGuid().ToString(), Kind = currencyChanged ? "LabCurrency" : "LabEdit", SourceId = work.Id,
                Description = description, Amount = paid, Currency = work.Currency, RateSnapshot = details.ConversionRate,
                OccurredAt = AppClock.Now, Actor = LoginSessionStore.CurrentUsername ?? "" });
            db.FinancialAuditEntries.Add(new() { Actor = LoginSessionStore.CurrentUsername ?? "", Action = currencyChanged ? "تغيير عملة عمل مخبري" : "تصحيح عمل مخبري",
                EntityType = "LabWork", EntityId = work.Id, Description = description, BeforeJson = before, AfterJson = JsonSerializer.Serialize(work), Reason = details.Reason.Trim() });
            await db.SaveChangesAsync();
        }
        await tx.CommitAsync(); return work.Id;
    }
    public static decimal LegacyLabPaid(Models.LabWork work, IEnumerable<Models.ClinicOperation> history)
    {
        var operations = history.Where(o => o.SourceId == work.Id).OrderBy(o => o.Id).ToList();
        string? firstConversion = operations.FirstOrDefault(o => o.Kind == "LabCurrency")?.Currency;
        string unit = firstConversion == null ? work.Currency : firstConversion == "USD" ? "SYP" : "USD";
        decimal recorded = 0;
        foreach (var operation in operations)
        {
            if (operation.Kind == "LabPayment") recorded += ConvertLabAmount(operation.Amount, operation.Currency, unit, operation.RateSnapshot);
            else if (operation.Kind == "LabCurrency") { recorded = ConvertLabAmount(recorded, unit, operation.Currency, operation.RateSnapshot); unit = operation.Currency; }
        }
        return Math.Max(0, work.AmountPaid - recorded);
    }
    async Task<ClinicOperation> Receipt(string key, string kind, int sourceId, string description, decimal amount, string currency, decimal rate, DateTime date, int quantity)
    {
        var expense = new ExpenseEntry { Description = description, ExpenseDate = date, Amount = (double)(currency == "USD" ? amount * rate : amount) };
        db.Expenses.Add(expense); await db.SaveChangesAsync();
        var receipt = new ClinicOperation { RequestKey = key, Kind = kind, SourceId = sourceId, Description = description,
            Quantity = quantity, Amount = amount, Currency = currency, RateSnapshot = rate, AmountSyp = currency == "USD" ? amount * rate : amount,
            OccurredAt = date, ExpenseId = expense.Id, Actor = LoginSessionStore.CurrentUsername ?? "" };
        db.ClinicOperations.Add(receipt);
        db.FinancialAuditEntries.Add(new() { Actor = receipt.Actor, Action = kind == "Purchase" ? "شراء نواقص" : "دفعة مخبر",
            EntityType = "Expense", EntityId = expense.Id, Description = description, AfterJson = JsonSerializer.Serialize(receipt) });
        await db.SaveChangesAsync(); return receipt;
    }
    public async Task<ClinicOperation> PurchaseAsync(int id, int expectedQuantity, int quantity, decimal unitPrice, string currency, decimal rate, DateTime date, string key)
    {
        Money(unitPrice); Money(rate); Currency(currency); Date(date);
        Require(quantity > 0 && quantity <= expectedQuantity, "راجع الكمية المطلوبة.");
        using var tx = await db.Database.BeginTransactionAsync();
        var previous = await db.ClinicOperations.AsNoTracking().SingleOrDefaultAsync(o => o.RequestKey == key);
        if (previous != null) return previous;
        var item = await db.Shortages.AsNoTracking().SingleOrDefaultAsync(s => s.Id == id && !s.IsDeleted)
            ?? throw new InvalidOperationException("لم تعد المادة متاحة. حدّث القائمة.");
        Require(item.Quantity == expectedQuantity, "تغيرت الكمية. حدّث القائمة وراجع الشراء.");
        int claimed = await db.Shortages.Where(s => s.Id == id && !s.IsDeleted && s.Quantity == expectedQuantity)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.Quantity, expectedQuantity - quantity));
        Require(claimed == 1, "تم تحديث المادة في نافذة أخرى.");
        var receipt = await Receipt(key, "Purchase", id, $"نواقص - {item.Item}", unitPrice * quantity, currency, rate, date, quantity);
        await tx.CommitAsync(); return receipt;
    }
    public async Task<ClinicOperation> PayLabAsync(int id, int expectedRevision, decimal amount, string currency, decimal rate, DateTime date, string key)
    {
        Money(amount); Money(rate); Currency(currency); Date(date);
        using var tx = await db.Database.BeginTransactionAsync();
        var previous = await db.ClinicOperations.AsNoTracking().SingleOrDefaultAsync(o => o.RequestKey == key);
        if (previous != null) return previous;
        var work = await db.LabWorks.AsNoTracking().SingleOrDefaultAsync(w => w.Id == id)
            ?? throw new InvalidOperationException("لم يعد العمل موجوداً.");
        Require(work.Revision == expectedRevision, "تغير العمل؛ أعد فتحه قبل تسجيل الدفعة.");
        decimal nativeAmount = currency == work.Currency ? amount : work.Currency == "USD" ? amount / rate : amount * rate;
        nativeAmount = decimal.Round(nativeAmount, 4);
        Require(nativeAmount > 0 && nativeAmount <= work.Cost - work.AmountPaid, "الدفعة تتجاوز الرصيد المتبقي أو أن العمل مسدد.");
        int claimed = await db.LabWorks.Where(w => w.Id == id && w.Revision == expectedRevision)
            .ExecuteUpdateAsync(s => s.SetProperty(w => w.Revision, expectedRevision + 1));
        Require(claimed == 1, "تم تعديل العمل في نافذة أخرى.");
        work.Revision++; work.AmountPaid += nativeAmount; work.DatePaid = date;
        work.Status = work.AmountPaid >= work.Cost ? work.DateReceived.HasValue ? "تم الدفع" : "تم الدفع ولم يتم الاستلام" : work.WorkStatus;
        db.LabWorks.Update(work);
        var receipt = await Receipt(key, "LabPayment", id, $"مخبر - {work.LabName} - {work.PatientName}", amount, currency, rate, date, 1);
        await tx.CommitAsync(); return receipt;
    }
    public async Task<bool> SetWorkStatusAsync(int id, int revision, string status)
    {
        string[] stages = ["تم الإرسال", "قيد العمل", "جاهز", "تم الاستلام"];
        Require(stages.Contains(status), "اختر حالة عمل صحيحة.");
        using var tx = await db.Database.BeginTransactionAsync();
        var work = await db.LabWorks.AsNoTracking().SingleAsync(w => w.Id == id);
        if (work.DateReceived.HasValue || Array.IndexOf(stages, status) <= Array.IndexOf(stages, work.WorkStatus)) return false;
        Require(work.Revision == revision, "تغير العمل؛ حدّث القائمة.");
        DateTime? received = status == "تم الاستلام" ? AppClock.Now : null;
        int changed = await db.LabWorks.Where(w => w.Id == id && w.Revision == revision && w.DateReceived == null)
            .ExecuteUpdateAsync(s => s.SetProperty(w => w.WorkStatus, status).SetProperty(w => w.DateReceived, received)
                .SetProperty(w => w.Revision, revision + 1).SetProperty(w => w.Status, work.AmountPaid >= work.Cost ? received.HasValue ? "تم الدفع" : "تم الدفع ولم يتم الاستلام" : status));
        if (changed == 1)
        {
            db.ClinicOperations.Add(new() { RequestKey = Guid.NewGuid().ToString(), Kind = "LabStatus", SourceId = id,
                Description = status, OccurredAt = AppClock.Now, Actor = LoginSessionStore.CurrentUsername ?? "" });
            await db.SaveChangesAsync();
        }
        await tx.CommitAsync(); return changed == 1;
    }
}
