using Microsoft.EntityFrameworkCore;
using MyClinic.Models;
using System.Text.Json;

namespace MyClinic;

/// <summary>Writes financial changes and their audit entries in the same SQLite transaction.</summary>
public sealed class FinancialTransactions(AppDbContext db, string actor)
{
    private void Audit(string action, string type, int id, string description, object? before, object after, string reason = "") =>
        db.FinancialAuditEntries.Add(new() { Actor = string.IsNullOrWhiteSpace(actor) ? "غير معروف" : actor, Action = action, EntityType = type, EntityId = id,
            Description = description, BeforeJson = before == null ? "" : JsonSerializer.Serialize(before), AfterJson = JsonSerializer.Serialize(after), Reason = reason });
    private static object ExpenseState(ExpenseEntry e) => new { e.ExpenseDate, e.Description, e.Amount };
    private static object UpcomingState(UpcomingPayment p) => new { p.Description, p.Amount, p.Recipient, p.DueDate, p.Status, p.CompletedAt, p.ExpenseId };

    public async Task<int> SaveExpenseAsync(int? id, string category, string details, decimal amount, DateTime date, string reason)
    {
        FinanceMoney.RequirePositive(amount);
        if (date.Date > AppClock.Today) throw new ArgumentException("تاريخ المصروف لا يتجاوز اليوم.");
        if (!ExpenseCategories.IsCategory(category)) throw new ArgumentException("اختر فئة المصروف.");
        if (id.HasValue && string.IsNullOrWhiteSpace(reason)) throw new ArgumentException("أدخل سبب التصحيح.");
        if (id.HasValue && await db.ClinicOperations.AnyAsync(o => o.ExpenseId == id.Value))
            throw new InvalidOperationException("هذا المصروف مرتبط بإيصال شراء أو دفعة مخبر محفوظة؛ لا يمكن تغيير قيمته من السجل المالي.");
        using var transaction = await db.Database.BeginTransactionAsync();
        ExpenseEntry expense = id.HasValue ? await db.Expenses.FindAsync(id.Value) ?? throw new InvalidOperationException("لم يعد المصروف موجوداً.") : new();
        if (id.HasValue) await db.Entry(expense).ReloadAsync();
        object? before = id.HasValue ? ExpenseState(expense) : null;
        expense.Description = ExpenseCategories.FormatDescription(category, details);
        expense.ExpenseDate = date; expense.Amount = (double)amount;
        if (!id.HasValue) db.Expenses.Add(expense);
        await db.SaveChangesAsync();
        if (id.HasValue)
        {
            var linked = await db.UpcomingPayments.SingleOrDefaultAsync(p => p.ExpenseId == expense.Id && p.Status == "Paid");
            if (linked != null)
            {
                await db.Entry(linked).ReloadAsync();
                object linkedBefore = UpcomingState(linked);
                linked.Amount = expense.Amount; linked.Description = expense.Description; linked.CompletedAt = expense.ExpenseDate;
                Audit("تصحيح سداد مستحق", "Upcoming", linked.Id, linked.Description, linkedBefore, UpcomingState(linked), reason.Trim());
            }
        }
        Audit(id.HasValue ? "تصحيح مصروف" : "إضافة مصروف", "Expense", expense.Id, expense.Description, before, ExpenseState(expense), reason.Trim());
        await db.SaveChangesAsync(); await transaction.CommitAsync();
        return expense.Id;
    }

    public async Task<int> SaveUpcomingAsync(int? id, string category, string details, decimal amount, DateTime? due)
    {
        FinanceMoney.RequirePositive(amount);
        if (!ExpenseCategories.IsCategory(category) || string.IsNullOrWhiteSpace(details))
            throw new ArgumentException("أكمل الفئة والتفاصيل.");
        using var transaction = await db.Database.BeginTransactionAsync();
        var payment = id.HasValue ? await db.UpcomingPayments.FindAsync(id.Value) ?? throw new InvalidOperationException("لم تعد الدفعة موجودة.") : new UpcomingPayment();
        if (id.HasValue) await db.Entry(payment).ReloadAsync();
        if (payment.Status != "Pending") throw new InvalidOperationException("يمكن تعديل الدفعات المعلقة فقط.");
        object? before = id.HasValue ? UpcomingState(payment) : null;
        payment.Description = ExpenseCategories.FormatDescription(category, details);
        payment.Amount = (double)amount; payment.DueDate = due?.Date;
        if (!id.HasValue) db.UpcomingPayments.Add(payment);
        await db.SaveChangesAsync();
        Audit(id.HasValue ? "تعديل مستحق" : "إضافة مستحق", "Upcoming", payment.Id, payment.Description, before, UpcomingState(payment));
        await db.SaveChangesAsync(); await transaction.CommitAsync();
        return payment.Id;
    }

    public async Task<bool> CompleteUpcomingAsync(int id, bool pay, string reason)
    {
        using var transaction = await db.Database.BeginTransactionAsync();
        var payment = await db.UpcomingPayments.AsNoTracking().SingleOrDefaultAsync(p => p.Id == id);
        if (payment == null || payment.Status != "Pending") return false;
        object before = UpcomingState(payment);
        var now = AppClock.Now;
        // Claim the pending entry before creating any expense, including competing window requests.
        int claimed = await db.UpcomingPayments.Where(p => p.Id == id && p.Status == "Pending")
            .ExecuteUpdateAsync(set => set.SetProperty(p => p.Status, pay ? "Paid" : "Cancelled").SetProperty(p => p.CompletedAt, now));
        if (claimed != 1) return false;
        payment.Status = pay ? "Paid" : "Cancelled"; payment.CompletedAt = now;
        if (pay)
        {
            var expense = new ExpenseEntry { ExpenseDate = now, Description = payment.Description, Amount = payment.Amount };
            db.Expenses.Add(expense); await db.SaveChangesAsync();
            payment.ExpenseId = expense.Id;
            await db.UpcomingPayments.Where(p => p.Id == id).ExecuteUpdateAsync(set => set.SetProperty(p => p.ExpenseId, expense.Id));
            Audit("مصروف من مستحق", "Expense", expense.Id, expense.Description, null, ExpenseState(expense));
        }
        Audit(pay ? "تسجيل سداد مستحق" : "إلغاء مستحق", "Upcoming", id, payment.Description, before, UpcomingState(payment), reason);
        await db.SaveChangesAsync(); await transaction.CommitAsync();
        if (db.UpcomingPayments.Local.FirstOrDefault(p => p.Id == id) is { } tracked) await db.Entry(tracked).ReloadAsync();
        return true;
    }

    public async Task<int> AddPatientEntryAsync(int patientId, decimal amount, bool forgiveness, DateTime date, string reason, decimal? expectedBalance = null)
    {
        FinanceMoney.RequirePositive(amount);
        if (date.Date > AppClock.Today) throw new ArgumentException("تاريخ العملية لا يتجاوز اليوم.");
        if (forgiveness && string.IsNullOrWhiteSpace(reason)) throw new ArgumentException("أدخل سبب المسامحة.");
        using var transaction = await db.Database.BeginTransactionAsync();
        var patient = await db.Patients.FindAsync(patientId) ?? throw new InvalidOperationException("لم يعد المريض موجوداً.");
        var visits = (await db.Visits.Where(v => v.PatientId == patientId).AsNoTracking().ToListAsync()).Where(v => v.VisitDate.Date <= AppClock.Today).ToList();
        decimal before = FinancialLedger.BuildAccount(patient, visits).Balance;
        if (expectedBalance.HasValue && before != expectedBalance.Value) throw new InvalidOperationException("تغير رصيد المريض. حدّث الحساب وراجع العملية مجدداً.");
        if (forgiveness && amount > Math.Max(0, before)) throw new ArgumentException("المسامحة أكبر من الرصيد المستحق.");
        if (forgiveness && amount > Math.Max(0, FinancialLedger.BuildAccount(patient, visits.Where(v => v.VisitDate <= date)).Balance))
            throw new ArgumentException("المسامحة أكبر من الرصيد المستحق في التاريخ المحدد.");
        var entry = new Visit { PatientId = patientId, VisitDate = date, IsFinancialOnly = true, FinancialNote = reason.Trim(),
            CurrentCost = forgiveness ? -(double)amount : 0, TodayPaid = forgiveness ? 0 : (double)amount };
        db.Visits.Add(entry); await db.SaveChangesAsync();
        Audit(forgiveness ? "مسامحة" : "دفعة مريض", "Visit", entry.Id, patient.FullName ?? "", new { Balance = before },
            new { Balance = before - amount, Amount = amount, Forgiveness = forgiveness, entry.VisitDate }, reason.Trim());
        await db.SaveChangesAsync(); await transaction.CommitAsync();
        return entry.Id;
    }
}
