using Microsoft.Win32;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace MyClinic;

public sealed record FinanceReport(string Title, string Scope, string[] Headers, IReadOnlyList<string[]> Rows, string Summary);

public static class FinanceExport
{
    public static string Csv(FinanceReport report)
    {
        static string Cell(string value)
        {
            // Quoting alone does not stop spreadsheet applications from evaluating patient text as formulas.
            string trimmed = value.TrimStart();
            bool negativeNumber = trimmed.StartsWith('-') && decimal.TryParse(trimmed, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out _);
            if (!negativeNumber && trimmed.Length > 0 && (trimmed[0] is '=' or '+' or '-' or '@' || trimmed[0] is '\t' or '\r' or '\n')) value = "'" + value;
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }
        var lines = new[] { new[] { report.Title }, new[] { report.Scope }, report.Headers }.Concat(report.Rows).Append(new[] { report.Summary });
        return string.Join("\r\n", lines.Select(row => string.Join(",", row.Select(Cell)))) + "\r\n";
    }

    public static FlowDocument Document(FinanceReport report, double width = 760, double height = 1080)
    {
        var document = new FlowDocument { FlowDirection = FlowDirection.RightToLeft, FontFamily = new FontFamily("Segoe UI"),
            FontSize = 11, Foreground = Brushes.Black, Background = Brushes.White, PagePadding = new Thickness(32),
            ColumnWidth = double.PositiveInfinity, PageWidth = width, PageHeight = height };
        document.Blocks.Add(new Paragraph(new Run(report.Title)) { FontSize = 22, FontWeight = FontWeights.Bold, Margin = new(0, 0, 0, 8) });
        document.Blocks.Add(new Paragraph(new Run(report.Scope)) { FontSize = 11, Margin = new(0, 0, 0, 16) });
        var table = new Table { CellSpacing = 0 };
        for (int i = 0; i < report.Headers.Length; i++) table.Columns.Add(new TableColumn());
        var group = new TableRowGroup(); table.RowGroups.Add(group);
        void AddRow(IEnumerable<string> values, bool header)
        {
            var row = new TableRow();
            foreach (string value in values) row.Cells.Add(new TableCell(new Paragraph(new Run(value)) { Margin = new(0), KeepTogether = true })
                { Padding = new(7), BorderBrush = Brushes.LightGray, BorderThickness = new(0, 0, 0, 0.5),
                    Background = header ? Brushes.WhiteSmoke : Brushes.White, FontWeight = header ? FontWeights.Bold : FontWeights.Normal });
            group.Rows.Add(row);
        }
        AddRow(report.Headers, true);
        foreach (var row in report.Rows) AddRow(row, false);
        document.Blocks.Add(table);
        document.Blocks.Add(new Paragraph(new Run(report.Summary)) { FontWeight = FontWeights.Bold, Margin = new(0, 18, 0, 0) });
        return document;
    }
}

public partial class FinancialRecordsView
{
    private FinanceReport PeriodReport()
    {
        string filter = string.IsNullOrWhiteSpace(WorkspaceSearch.Text) ? "" : " · البحث: " + WorkspaceSearch.Text.Trim();
        string category = CategoryFilter.SelectedIndex > 0 ? " · الفئة: " + CategoryFilter.SelectedItem : "";
        string scope = $"الفترة: {_range.Label}{filter}{category} · أُعد في {AppClock.Now:dd/MM/yyyy HH:mm}";
        string title = "الشؤون المالية — " + ((TabItem)FinanceTabs.SelectedItem).Header;
        return FinanceTabs.SelectedIndex switch
        {
            1 => new(title, scope, new[] { "التاريخ", "الفئة", "التفاصيل", "القيمة (ل.س)" },
                ExpenseItemsControl.Items.Cast<FinanceExpenseRow>().Select(r => new[] { r.DateText, r.Category, r.Description, Number(r.Amount) }).ToList(),
                "مجموع المصاريف المعروضة: " + FinanceMoney.Format(ExpenseItemsControl.Items.Cast<FinanceExpenseRow>().Sum(r => r.Amount))),
            2 => new(title, $"جميع التواريخ · الحالة: {((ComboBoxItem)UpcomingStatusFilter.SelectedItem).Content}{filter}{category}",
                new[] { "الاستحقاق", "الحالة", "التفاصيل", "القيمة (ل.س)", "تاريخ الإتمام", "رقم المصروف" },
                _filteredUpcoming.Select(r => new[] { r.DueText, r.StatusText, r.Description, Number(r.Amount), r.CompletedAt?.ToString("dd/MM/yyyy HH:mm") ?? "", r.ExpenseId?.ToString() ?? "" }).ToList(),
                "مجموع المستحقات المطابقة (جميع الصفحات): " + FinanceMoney.Format(_filteredUpcoming.Sum(r => r.Amount))),
            3 => new(title, $"حتى {AppClock.Today:dd/MM/yyyy}{filter} · العمر: {((ComboBoxItem)AccountAgeFilter.SelectedItem).Content}",
                new[] { "المريض", "الهاتف", "بداية الرصيد", "العمر بالأيام", "المستحق (ل.س)" },
                UnpaidItemsControl.Items.Cast<FinanceAccount>().Select(r => new[] { r.PatientName, r.Phone, r.FirstDebtDateText, r.AgeDays.ToString(), Number(r.Balance) }).ToList(),
                "إجمالي الأرصدة المعروضة: " + FinanceMoney.Format(UnpaidItemsControl.Items.Cast<FinanceAccount>().Sum(r => r.Balance))),
            _ => new(title, scope, new[] { "التاريخ", "الوقت", "المريض", "الهاتف", "النوع", "المحصّل (ل.س)" },
                IncomeItemsControl.Items.Cast<FinanceVisitRow>().Select(r => new[] { r.DateText, r.TimeText, r.PatientName, r.Phone, r.Kind, Number(r.Paid) }).ToList(),
                "مجموع المبالغ المحصلة المعروضة: " + FinanceMoney.Format(IncomeItemsControl.Items.Cast<FinanceVisitRow>().Sum(r => r.Paid)))
        };
    }
    private FinanceReport? PatientReport()
    {
        if (_selectedAccount == null) { ShowStatus("اختر مريضاً أولاً."); return null; }
        var a = _selectedAccount;
        return new("كشف حساب المريض", $"{a.PatientName} · {a.Phone} · حتى {AppClock.Today:dd/MM/yyyy}",
            new[] { "التاريخ", "النوع", "التكلفة (ل.س)", "المسامحة (ل.س)", "المدفوع (ل.س)", "الرصيد بعد العملية (ل.س)", "الملاحظة" },
            a.Rows.OrderBy(r => r.Date).ThenBy(r => r.Id).Select(r => new[] { r.Date.ToString("dd/MM/yyyy HH:mm"), r.Kind, Number(r.Cost), Number(r.Forgiven), Number(r.Paid), Number(r.Balance), r.Note }).ToList(),
            $"التكلفة: {FinanceMoney.Format(a.Cost)} · الدفعات: {FinanceMoney.Format(a.Paid)} · المسامحة: {FinanceMoney.Format(a.Forgiven)} · {BalanceLabel(a.Balance)}");
    }
    private static string Number(decimal value) => value.ToString("0.##", CultureInfo.InvariantCulture);
    private void ExportPeriod_Click(object sender, RoutedEventArgs e) { if (TryRange(out _)) ExportReport(PeriodReport(), $"السجل-المالي-{AppClock.Today:yyyyMMdd}"); }
    private void ExportPatient_Click(object sender, RoutedEventArgs e) { if (PatientReport() is { } report) ExportReport(report, $"كشف-حساب-{_selectedAccount!.PatientId}-{AppClock.Today:yyyyMMdd}"); }
    private void ExportReport(FinanceReport report, string name)
    {
        var dialog = new SaveFileDialog { Title = "تصدير " + report.Title, FileName = name + ".csv", Filter = "CSV (*.csv)|*.csv", AddExtension = true, OverwritePrompt = true };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;
        try { File.WriteAllText(dialog.FileName, FinanceExport.Csv(report), new UTF8Encoding(true)); ShowStatus("تم تصدير السجلات المعروضة."); }
        catch (Exception) { ShowStatus("تعذر كتابة ملف التصدير. اختر مجلداً آخر وأعد المحاولة.", true); }
    }
    private void PrintPeriod_Click(object sender, RoutedEventArgs e) { if (TryRange(out _)) PrintReport(PeriodReport()); }
    private void PrintPatient_Click(object sender, RoutedEventArgs e) { if (PatientReport() is { } report) PrintReport(report); }
    private void PrintReceipt_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not FinanceVisitRow row) return;
        var lines = new List<string[]> { new[] { "التكلفة", row.CostText }, new[] { "المسامحة", row.ForgivenText }, new[] { "المدفوع", row.PaidText }, new[] { "الرصيد بعد العملية", row.BalanceText } };
        lines.AddRange(row.Treatments.Select(t => new[] { t.TreatmentName, $"قيمة العلاج: {t.CostText} · المخصص: {t.PaidText} · من رصيد سابق: {t.CreditAppliedText} · مسامحة: {t.ForgivenText} · المتبقي: {t.RemainingText}" }));
        PrintReport(new("إيصال عملية مالية", $"العملية #{row.Id} · {row.PatientName} · {row.Phone}\n{row.Date:dd/MM/yyyy HH:mm} · {row.Kind}", new[] { "البيان", "القيمة" }, lines, row.Note));
    }
    private void PrintReport(FinanceReport report)
    {
        var dialog = new PrintDialog();
        try
        {
            if (dialog.ShowDialog() != true) return;
            var doc = FinanceExport.Document(report, dialog.PrintableAreaWidth, dialog.PrintableAreaHeight);
            dialog.PrintDocument(((IDocumentPaginatorSource)doc).DocumentPaginator, report.Title);
            ShowStatus("أُرسل التقرير للطباعة. يمكن اختيار Microsoft Print to PDF لحفظه كملف PDF.");
        }
        catch (Exception) { ShowStatus("تعذرت الطباعة. تحقق من الطابعة وأعد المحاولة.", true); }
    }
}
