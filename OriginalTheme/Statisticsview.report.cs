using Microsoft.EntityFrameworkCore;
using Microsoft.Win32;
using MyClinic.Models;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace MyClinic.OriginalTheme
{
    /// <summary>
    /// One combined PDF report for the selected day / month / year:
    ///   1) الدخل والمصاريف   2) المصاريف   3) العلاجات
    /// A4 pages retain selectable Arabic text, embedded fonts and vector charts.
    /// </summary>
    public partial class StatisticsView
    {
        // ── Page geometry & palette ───────────────────────────────────────────
        private const double PgW = 1240, PgH = 1754, PgM = 70, PgR = 1170, PgCW = 1100;

        private const string CNavy = "#12294D", CTeal = "#10A18C", CRed = "#E05252", CAmber = "#E8A33D",
                             CBlue = "#3B82C4", CPurple = "#7C5CBF", CGray = "#9AA5B1", CMuted = "#6B7280",
                             CBorder = "#E3E7EE", CTrack = "#EDF0F5", CHead = "#EAF0F8";

        private static readonly string[] ReportColors =
        {
            CNavy, CTeal, CAmber, CBlue, CRed, CPurple, CGray,
            "#F97316", "#EC4899", "#84CC16", "#06B6D4", "#6366F1", "#14B8A6"
        };

        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        private static BitmapImage? _reportLogo;
        private static BitmapImage ReportLogo()
        {
            if (_reportLogo != null) return _reportLogo;
            var logo = new BitmapImage();
            logo.BeginInit();
            logo.UriSource = new Uri("pack://application:,,,/MyClinic;component/Assets/logo.png");
            logo.DecodePixelWidth = 160;
            logo.CacheOption = BitmapCacheOption.OnLoad;
            logo.EndInit(); logo.Freeze();
            return _reportLogo = logo;
        }

        private enum Anchor { Left, Center, Right }

        private sealed record Bucket(string Label, string Name, double Income, double Expense);
        private sealed record Kpi(string Label, string Value, string Sub, string ValueColor, string Strip);
        private sealed record CatInfo(string Label, List<ExpenseEntry> Rows, double Total, string Color);

        private sealed class ReportData
        {
            public List<Visit> AllVisits = new();
            public List<Visit> PeriodVisits = new();   // every visit inside the period
            public List<Visit> Visits = new();         // "registered" visits (cost != 0 or paid > 0)
            public List<ExpenseEntry> Expenses = new();
            public List<string> KnownTreatments = new();
            public List<ExpenseEntry> ComparisonExpenses = new();
            public DateTime CreatedAt = AppClock.Now;
        }

        private sealed class ReportDoc
        {
            public readonly List<(Canvas Canvas, string Footer)> Pages = new();
            public Canvas C = null!;
            public double Y;
            public string Footer = "";
            public string Section = "";
            public DateTime CreatedAt;

            public void NewPage(string? footer = null)
            {
                if (footer != null) Footer = footer;
                C = new Canvas { Width = PgW, Height = PgH, Background = Brushes.White, FlowDirection = FlowDirection.LeftToRight };
                Pages.Add((C, Footer));
                Y = 100;
                if (footer == null)
                {
                    Txt(C, Footer, PgR, 36, 20, CNavy, true);
                    HLine(C, PgM, PgR, 76, CBorder);
                }
            }

            public void Ensure(double h)
            {
                if (Y + h > PgH - 110) NewPage();
            }
        }

        // ═════════════════════════════════════════════════════════════════════
        // Export entry point
        // ═════════════════════════════════════════════════════════════════════

        private async void BtnExportPdf_Click(object sender, RoutedEventArgs e)
        {
            if (!IsEnabled) return;
            IsEnabled = false; // Keep period selection stable while loading the export snapshot.
            try
            {
                ReportData data = await LoadReportDataAsync();

                SaveFileDialog dialog = new()
                {
                    Title = "تصدير التقرير الإحصائي",
                    Filter = "ملف PDF (*.pdf)|*.pdf",
                    FileName = $"التقرير-{PeriodFileLabel()}.pdf",
                    AddExtension = true,
                    OverwritePrompt = true
                };
                if (dialog.ShowDialog() != true) return;

                Mouse.OverrideCursor = Cursors.Wait;
                try
                {
                    byte[] pdf = BuildReportPdf(data);
                    File.WriteAllBytes(dialog.FileName, pdf);
                }
                finally
                {
                    Mouse.OverrideCursor = null;
                }

                MessageBox.Show("تم تصدير التقرير بنجاح.", "التصدير", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"تعذر تصدير التقرير حالياً.\n{ex.Message}", "التصدير", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally { IsEnabled = true; }
        }

        private async Task<ReportData> LoadReportDataAsync()
        {
            var data = new ReportData();
            using (var ctx = new AppDbContext())
            {
                DateTime previousStart = StatisticsAnalytics.PreviousPeriodStart(SelectedPeriodStart(), ReportPeriod());
                DateTime exclusiveEnd = SelectedPeriodEnd().AddDays(1);
                data.AllVisits = await ctx.Visits.AsNoTracking().ToListAsync();
                data.ComparisonExpenses = await ctx.Expenses.AsNoTracking()
                    .Where(x => x.ExpenseDate >= previousStart && x.ExpenseDate < exclusiveEnd)
                    .OrderBy(x => x.ExpenseDate)
                    .ToListAsync();
                data.KnownTreatments = (await ctx.TreatmentCosts
                        .AsNoTracking()
                        .Select(t => t.TreatmentName)
                        .ToListAsync())
                    .Where(name => !string.IsNullOrWhiteSpace(name))
                    .Distinct(StringComparer.Ordinal)
                    .ToList();
            }

            SelectReportPeriod(data);
            return data;
        }

        private void SelectReportPeriod(ReportData data)
        {
            data.Expenses = data.ComparisonExpenses.Where(x => x.ExpenseDate.Date >= SelectedPeriodStart()
                && x.ExpenseDate.Date <= SelectedPeriodEnd() && x.ExpenseDate.Date <= data.CreatedAt.Date).ToList();
            data.PeriodVisits = data.AllVisits.Where(v => IsInSelectedPeriod(v) && v.VisitDate.Date <= data.CreatedAt.Date)
                .OrderBy(v => v.VisitDate).ThenBy(v => v.Id).ToList();
            data.Visits = data.PeriodVisits.Where(v => v.CurrentCost != 0 || v.TodayPaid > 0).ToList();
        }

        private byte[] BuildReportPdf(ReportData data)
        {
            var doc = new ReportDoc { CreatedAt = data.CreatedAt };
            BuildOverview(doc, data);
            BuildIncomeSection(doc, data);
            BuildExpensesSection(doc, data);
            BuildTreatmentsSection(doc, data);

            int total = doc.Pages.Count;
            for (int i = 0; i < total; i++)
            {
                Canvas page = doc.Pages[i].Canvas;
                HLine(page, PgM, PgR, PgH - 92, CBorder);
                Txt(page, $"{doc.Pages[i].Footer} · صفحة {i + 1} من {total}", PgW / 2, PgH - 72, 18, CMuted, false, Anchor.Center);
            }
            return StatisticsPdfWriter.Build(doc.Pages.Select(p => p.Canvas).ToList(), $"عيادتي - التقرير الإحصائي - {CurrentPeriodLabel()}");
        }

        private StatisticsPeriod ReportPeriod() => _subTab switch
        {
            SubTab.Daily => StatisticsPeriod.Day,
            SubTab.Yearly => StatisticsPeriod.Year,
            _ => StatisticsPeriod.Month
        };

        private void BuildOverview(ReportDoc d, ReportData r)
        {
            ReportBanner(d, "نظرة عامة على أداء العيادة", PeriodHeaderLine(), $"نظرة عامة · {CurrentPeriodLabel()}");
            double income = r.Visits.Sum(v => v.TodayPaid), expense = r.Expenses.Sum(x => x.Amount), net = income - expense;
            int count = r.PeriodVisits.Sum(v => ParseTreatments(v.SelectedTreatmentsJson).Sum(t => Math.Max(1, t.Quantity)));
            KpiRow(d,
                new Kpi("المبالغ المحصلة (ل.س)", Num(income), "الدفعات المستلمة خلال الفترة", CTeal, CTeal),
                new Kpi("المصاريف (ل.س)", Num(expense), $"{r.Expenses.Count} عملية", CRed, CRed),
                new Kpi("صافي التحصيل (ل.س)", Num(net), "المحصّل ناقص المصاريف", net < 0 ? CRed : CNavy, CNavy),
                new Kpi("العلاجات المنفذة", Num(count), "الكمية المسجلة خلال الفترة", CNavy, CBlue));
            string chartTitle = _subTab switch
            {
                SubTab.Daily => "المبالغ المحصلة والمصاريف حسب الساعة",
                SubTab.Yearly => "المبالغ المحصلة والمصاريف الشهرية",
                _ => "المبالغ المحصلة والمصاريف اليومية"
            };
            ChartPanel(d, chartTitle, 260, (c, x, y, w, h) => DrawGroupedBars(c, x, y, w, h, BuildBuckets(r)));

            StatisticsAnalytics Analyze(IEnumerable<StatisticsObservation> observations) => StatisticsAnalytics.Build(
                observations, SelectedPeriodStart(), ReportPeriod(), r.CreatedAt.Date, "", true);
            var collected = Analyze(r.AllVisits.Select(v => new StatisticsObservation(v.VisitDate, v.TodayPaid)));
            var spent = Analyze(r.ComparisonExpenses.Select(e => new StatisticsObservation(e.ExpenseDate, e.Amount)));
            var treatments = Analyze(r.AllVisits.Select(v => new StatisticsObservation(v.VisitDate,
                ParseTreatments(v.SelectedTreatmentsJson).Sum(t => Math.Max(1, t.Quantity)))));
            SectionTitle(d, "المقارنة مع الفترة السابقة");
            string Change(double current, double previous) => previous == 0 ? current == 0 ? "لا تغيير" : "لا توجد قاعدة"
                : $"{(current > previous ? "زيادة" : current < previous ? "انخفاض" : "لا تغيير")} {Pct(Math.Abs(current - previous) / Math.Abs(previous) * 100)}";
            var comparisons = new List<string[]>
            {
                new[] { "المبالغ المحصلة (ل.س)", Num(income), Num(collected.PreviousTotal), Change(income, collected.PreviousTotal) },
                new[] { "المصاريف (ل.س)", Num(expense), Num(spent.PreviousTotal), Change(expense, spent.PreviousTotal) },
                new[] { "صافي التحصيل (ل.س)", Num(net), Num(collected.PreviousTotal - spent.PreviousTotal), Change(net, collected.PreviousTotal - spent.PreviousTotal) },
                new[] { "العلاجات المنفذة", Num(count), Num(treatments.PreviousTotal), Change(count, treatments.PreviousTotal) }
            };
            Table(d, new[] { "المؤشر", "الحالي", "السابق", "التغير" }, new[] { .36, .20, .20, .24 }, "RCCC",
                new[] { CNavy, CNavy, CMuted, CNavy }, comparisons, false);
            string Range(DateTime start, DateTime end) => $"{start:dd/MM/yyyy} - {end:dd/MM/yyyy}";
            string cutoff = r.CreatedAt.Date < SelectedPeriodStart() ? "الفترة المختارة مستقبلية؛ لا توجد نتائج فعلية للمقارنة."
                : $"الحالي: {Range(SelectedPeriodStart(), r.CreatedAt.Date < SelectedPeriodEnd() ? r.CreatedAt.Date : SelectedPeriodEnd())}؛ السابق: {Range(collected.PreviousStart, collected.PreviousEnd)}.";
            d.Y += Para(d.C, cutoff, PgR, d.Y, PgCW, 19, CMuted) + 16;
            SectionTitle(d, "أبرز النتائج");
            var notes = new List<string>
            {
                $"المحصّل {Money(income)}، والمصاريف {Money(expense)}، وصافي التحصيل {Money(net)}."
            };
            CatInfo? topExpense = BuildExpenseCategories(r.Expenses).FirstOrDefault(c => c.Rows.Count > 0);
            if (topExpense != null) notes.Add($"أكبر فئة مصاريف: {topExpense.Label} بقيمة {Money(topExpense.Total)}.");
            notes.Add($"نُفذ {count} علاجاً؛ ويتضمن التقرير التالي جميع الرسوم والتفاصيل والجداول.");
            Bullets(d, notes);
            d.Y += Para(d.C, "أساس التقرير: الدفعات المحصلة والمصاريف المسجلة، وليس الربح المحاسبي. قد تسدد الدفعات علاجات من فترات سابقة. جميع المبالغ بالليرة السورية.", PgR, d.Y, PgCW, 19, CMuted);
        }

        // ═════════════════════════════════════════════════════════════════════
        // SECTION 1 — الدخل والمصاريف
        // ═════════════════════════════════════════════════════════════════════

        private void BuildIncomeSection(ReportDoc d, ReportData r)
        {
            string footer = $"تقرير التحصيل والمصاريف · {CurrentPeriodLabel()}";
            ReportBanner(d, "تفاصيل التحصيل والمصاريف", PeriodHeaderLine(), footer);

            double income = r.Visits.Sum(v => v.TodayPaid);
            double expenses = r.Expenses.Sum(x => x.Amount);
            double net = income - expenses;
            double margin = income > 0 ? net / income * 100 : 0;
            int paidCount = r.Visits.Count(v => v.TodayPaid > 0.009);
            double avgVisit = paidCount > 0 ? income / paidCount : 0;
            List<Bucket> buckets = BuildBuckets(r);
            List<CatInfo> cats = BuildExpenseCategories(r.Expenses);

            KpiRow(d,
                new Kpi("المبالغ المحصلة (ل.س)", Num(income), $"{paidCount} زيارة مدفوعة", CTeal, CTeal),
                new Kpi("المصاريف (ل.س)", Num(expenses), $"{r.Expenses.Count} بند مصروف", CRed, CRed),
                new Kpi("صافي التحصيل (ل.س)", Num(net), income > 0 ? $"نسبته من المحصّل {Pct(margin)}" : "النسبة غير متاحة", net < 0 ? CRed : CNavy, CNavy),
                new Kpi("متوسط الدفعة (ل.س)", Num(avgVisit), $"المحصّل ÷ {paidCount} زيارة مدفوعة", CNavy, CAmber));
            ChartPanel(d, "صافي التحصيل التراكمي", 340, (c, x, y, w, h) => DrawCumulative(c, x, y, w, h, buckets));

            // Expense distribution (donut) + visit-amount distribution (bars)
            var donutItems = cats.Where(ct => ct.Rows.Count > 0)
                .Select(ct => (ct.Label, ct.Total, ct.Color)).ToList();
            var visitDist = BuildVisitDistribution(r.Visits);
            double donutH = Math.Max(330, 50 + donutItems.Count * 54 + 20);
            double barsH = 24 + visitDist.Count * 40 + 56;
            double panelH = Math.Max(donutH, barsH);
            TwoPanels(d, "توزيع المصاريف", "توزيع الزيارات حسب المبلغ", panelH,
                (c, x, y, w, h) => DrawDonutLegend(c, x, y, w, h, donutItems, Num(expenses), "ل.س", Num, false),
                (c, x, y, w, h) => DrawHBars(c, x, y, w, h, visitDist, "عدد الزيارات في كل فئة (بالعملة نفسها)"));

            // Key findings
            var findings = new List<string>
            {
                $"بلغ المحصّل {Money(income)} مقابل مصاريف {Money(expenses)}؛ صافي التحصيل {Money(net)}."
            };
            if (_subTab != SubTab.Daily && buckets.Any(b => b.Income > 0))
            {
                Bucket bestInc = buckets.OrderByDescending(b => b.Income).First();
                Bucket bestNet = buckets.OrderByDescending(b => b.Income - b.Expense).First();
                findings.Add($"أعلى {UnitWord()} دخلاً هو {bestInc.Name} بمبلغ {Num(bestInc.Income)}؛ وأفضل صافٍ كان في {bestNet.Name} بمبلغ {Num(bestNet.Income - bestNet.Expense)}.");
            }
            if (_subTab != SubTab.Daily && buckets.Count > 0)
            {
                Bucket worst = buckets.OrderBy(b => b.Income - b.Expense).First();
                double worstNet = worst.Income - worst.Expense;
                if (worstNet < 0)
                {
                    string cause = "";
                    CatInfo? top = cats.FirstOrDefault(ct => ct.Rows.Count > 0);
                    if (top != null && expenses > 0)
                        cause = $" وأكبر فئة مصاريف هي {top.Label} ({Num(top.Total)}، أي {Pct(top.Total / expenses * 100)} من المصاريف).";
                    findings.Add($"أكبر عجز {UnitWord()} كان في {worst.Name} بمبلغ {Num(worstNet)}.{cause}");
                }
            }
            int activeBuckets = buckets.Count(b => b.Income > 0);
            if (_subTab != SubTab.Daily && activeBuckets > 0)
                findings.Add($"سُجّل دخل في {activeBuckets} من أصل {buckets.Count} {UnitAcc()}، بمتوسط {Num(income / activeBuckets)} لكل {UnitWord()} فيه دخل.");

            d.Ensure(140);
            SectionTitle(d, "أهم النتائج");
            Bullets(d, findings);

            // Weekly summary (monthly only)
            if (_subTab == SubTab.Monthly)
            {
                var weekRows = new List<string[]>();
                for (int s = 1; s <= buckets.Count; s += 7)
                {
                    int last = Math.Min(s + 6, buckets.Count);
                    var seg = buckets.Skip(s - 1).Take(last - s + 1).ToList();
                    double wi = seg.Sum(b => b.Income), we = seg.Sum(b => b.Expense);
                    weekRows.Add(new[] { $"من {s} إلى {last}", Num(wi), Num(we), Num(wi - we) });
                }
                weekRows.Add(new[] { "الإجمالي", Num(income), Num(expenses), Num(net) });
                d.Ensure(220);
                SectionTitle(d, "الملخص الأسبوعي");
                Table(d, new[] { "الأيام", "المحصّل", "المصاريف", "الصافي" }, new[] { .28, .24, .24, .24 }, "RCCC",
                      new[] { CNavy, CTeal, CRed, "sign" }, weekRows, true);
            }

            // Daily / monthly summary table
            if (_subTab == SubTab.Monthly)
            {
                DateTime start = SelectedPeriodStart();
                var rows = new List<string[]>();
                for (int i = 0; i < buckets.Count; i++)
                {
                    DateTime day = start.AddDays(i);
                    rows.Add(new[] { day.ToString("dd/MM/yyyy", Inv), ArabicDay(day.DayOfWeek), Num(buckets[i].Income), Num(buckets[i].Expense), Num(buckets[i].Income - buckets[i].Expense) });
                }
                rows.Add(new[] { "الإجمالي", "", Num(income), Num(expenses), Num(net) });
                d.Ensure(220);
                SectionTitle(d, "الملخص اليومي");
                Table(d, new[] { "التاريخ", "اليوم", "المحصّل", "المصاريف", "الصافي" }, new[] { .22, .18, .20, .20, .20 }, "RCCCC",
                      new[] { CNavy, CMuted, CTeal, CRed, "sign" }, rows, true);
            }
            else if (_subTab == SubTab.Yearly)
            {
                var rows = buckets.Select(b => new[] { b.Name, Num(b.Income), Num(b.Expense), Num(b.Income - b.Expense) }).ToList();
                rows.Add(new[] { "الإجمالي", Num(income), Num(expenses), Num(net) });
                d.Ensure(220);
                SectionTitle(d, "الملخص الشهري");
                Table(d, new[] { "الشهر", "المحصّل", "المصاريف", "الصافي" }, new[] { .28, .24, .24, .24 }, "RCCC",
                      new[] { CNavy, CTeal, CRed, "sign" }, rows, true);
            }

            // Details table
            var detailItems = r.Visits.Select(v => (Date: v.VisitDate, Type: "تحصيل", Desc: IncomeDescription(v, r.AllVisits), Cat: "التحصيل", Amount: v.TodayPaid))
                .Where(it => it.Amount != 0)
                .Concat(r.Expenses.Select(ex => (Date: ex.ExpenseDate, Type: "مصروف", Desc: ex.Description ?? "", Cat: ClassifyExpense(ex.Description ?? ""), Amount: ex.Amount)))
                .Where(it => it.Amount != 0)
                .OrderBy(it => it.Date)
                .ToList();
            d.Ensure(220);
            SectionTitle(d, "تفاصيل التحصيل والمصاريف");
            if (detailItems.Count == 0)
            {
                Txt(d.C, "لا توجد بيانات لهذه الفترة", PgR, d.Y, 18, CMuted);
                d.Y += 50;
            }
            else
            {
                var rows = detailItems.Select(it => new[]
                {
                    it.Date.ToString("dd/MM/yyyy HH:mm", Inv), it.Type, it.Desc, it.Cat, Num(it.Amount)
                }).ToList();
                Table(d, new[] { "التاريخ", "النوع", "الوصف", "التصنيف", "المبلغ (ل.س)" }, new[] { .20, .09, .36, .15, .20 }, "RCRCC",
                      new[] { CNavy, "type", CNavy, CMuted, CNavy }, rows, false);
                SectionTitle(d, "إجماليات الحركة المالية");
                Table(d, new[] { "البند", "المبلغ (ل.س)" }, new[] { .70, .30 }, "RC", new[] { CNavy, "sign" },
                    new List<string[]> { new[] { "إجمالي المبالغ المحصلة", Num(income) }, new[] { "إجمالي المصاريف", Num(expenses) }, new[] { "صافي التحصيل (المحصّل - المصاريف)", Num(net) } }, true);
            }
        }

        private static string IncomeDescription(Visit visit, IReadOnlyCollection<Visit> allVisits)
        {
            List<Visit> patientVisits = allVisits
                .Where(v => v.PatientId == visit.PatientId)
                .OrderBy(v => v.VisitDate)
                .ThenBy(v => v.Id)
                .ToList();

            IEnumerable<string> treatmentNames;
            if (visit.TodayPaid > 0.009)
            {
                double paidBefore = patientVisits
                    .Where(v => v.VisitDate < visit.VisitDate || (v.VisitDate == visit.VisitDate && v.Id < visit.Id))
                    .Sum(v => v.TodayPaid);
                double paidAfter = paidBefore + visit.TodayPaid;
                var treatmentQueue = patientVisits
                    .SelectMany(patientVisit => ParseTreatments(patientVisit.SelectedTreatmentsJson)
                        .Select(treatment =>
                        {
                            double cost = (double)treatment.Cost;
                            if (treatment.Currency == "USD")
                                cost *= patientVisit.UsdToSypRateSnapshot > 0 ? patientVisit.UsdToSypRateSnapshot : 15000;
                            cost *= Math.Max(1, treatment.Quantity);
                            return (Name: treatment.TreatmentName.Trim(), Cost: cost);
                        }))
                    .Where(item => item.Cost > 0.009)
                    .ToList();
                double runningCost = 0;
                var allocatedNames = new List<string>();

                foreach (var item in treatmentQueue)
                {
                    double treatmentStart = runningCost;
                    double treatmentEnd = runningCost + item.Cost;
                    runningCost = treatmentEnd;

                    if (treatmentEnd <= paidBefore) continue;
                    if (treatmentStart >= paidAfter) break;

                    double allocated = Math.Min(treatmentEnd, paidAfter) - Math.Max(treatmentStart, paidBefore);
                    if (allocated > 0.009)
                        allocatedNames.Add(item.Name);
                }

                treatmentNames = allocatedNames;
            }
            else
            {
                treatmentNames = ParseTreatments(visit.SelectedTreatmentsJson)
                    .Where(treatment =>
                    {
                        double cost = (double)treatment.Cost;
                        if (treatment.Currency == "USD")
                            cost *= visit.UsdToSypRateSnapshot > 0 ? visit.UsdToSypRateSnapshot : 15000;
                        return cost * Math.Max(1, treatment.Quantity) > 0.009;
                    })
                    .Select(treatment => treatment.TreatmentName.Trim());
            }

            string description = string.Join("، ", treatmentNames.Where(name => !string.IsNullOrWhiteSpace(name)));
            return string.IsNullOrWhiteSpace(description) ? "لا يوجد علاج محدد" : description;
        }

        // ═════════════════════════════════════════════════════════════════════
        // SECTION 2 — المصاريف
        // ═════════════════════════════════════════════════════════════════════

        private void BuildExpensesSection(ReportDoc d, ReportData r)
        {
            string footer = $"تفاصيل المصاريف · {CurrentPeriodLabel()}";
            ReportBanner(d, "تفاصيل المصاريف", $"الفترة: {CurrentPeriodLabel()}", footer);

            List<CatInfo> cats = BuildExpenseCategories(r.Expenses);
            List<CatInfo> active = cats.Where(ct => ct.Rows.Count > 0).ToList();
            double total = r.Expenses.Sum(x => x.Amount);
            int count = r.Expenses.Count;
            double avg = count > 0 ? total / count : 0;
            CatInfo? topCat = active.FirstOrDefault();

            KpiRow(d,
                new Kpi("إجمالي المصاريف", Money(total), $"خلال {CurrentPeriodLabel()}", CNavy, CRed),
                new Kpi("عدد العمليات", $"{count} عملية", $"في {active.Count} فئات من أصل {cats.Count}", CNavy, CNavy),
                new Kpi("أعلى فئة إنفاقاً", topCat?.Label ?? "—", topCat != null && total > 0 ? $"{Pct(topCat.Total / total * 100)} من الإجمالي" : "لا توجد مصاريف", CNavy, CAmber),
                new Kpi("متوسط العملية", Money(avg), "الإجمالي ÷ عدد العمليات", CNavy, CTeal));

            // Category distribution
            d.Ensure(56 + 30 + Math.Max(1, active.Count) * 54 + 40);
            SectionTitle(d, "توزيع المصاريف حسب الفئة");
            double panelH = 24 + Math.Max(1, active.Count) * 54 + 10;
            Box(d.C, PgM, d.Y, PgCW, panelH, "#FFFFFF", CBorder, 16);
            if (active.Count == 0)
                Txt(d.C, "لا توجد مصاريف في هذه الفترة", PgM + PgCW / 2, d.Y + 40, 18, CMuted, false, Anchor.Center);
            double maxCat = active.Count > 0 ? active.Max(ct => ct.Total) : 1;
            for (int i = 0; i < active.Count; i++)
            {
                CatInfo ct = active[i];
                double ry = d.Y + 20 + i * 54;
                double rowRight = PgM + PgCW - 28;
                Ellipse dot = new() { Width = 16, Height = 16, Fill = HexBrush(ct.Color) };
                Canvas.SetLeft(dot, rowRight - 16); Canvas.SetTop(dot, ry + 8);
                d.C.Children.Add(dot);
                Txt(d.C, ct.Label, rowRight - 30, ry + 2, 20, CNavy, true, Anchor.Right, 190);
                double trackRight = rowRight - 240, trackLeft = PgM + 330, trackW = trackRight - trackLeft;
                Box(d.C, trackLeft, ry + 10, trackW, 14, CTrack, null, 7);
                double fillW = Math.Max(6, ct.Total / maxCat * trackW);
                Box(d.C, trackRight - fillW, ry + 10, fillW, 14, ct.Color, null, 7);
                Txt(d.C, Money(ct.Total), PgM + 28, ry + 2, 19, ct.Color, true, Anchor.Left);
                Txt(d.C, Pct(total > 0 ? ct.Total / total * 100 : 0), PgM + 215, ry + 4, 17, CMuted, false, Anchor.Left);
            }
            d.Y += panelH + 36;

            // Expenses by date
            List<Bucket> buckets = BuildBuckets(r);
            var dated = buckets.Where(b => b.Expense > 0).ToList();
            if (dated.Count > 0)
            {
                string title = _subTab switch { SubTab.Daily => "المصاريف حسب الساعة", SubTab.Yearly => "المصاريف حسب الشهر", _ => "المصاريف حسب التاريخ" };
                ChartPanel(d, title, 340, (c, x, y, w, h) =>
                    DrawSimpleBars(c, x, y, w, h, dated.Select(b => (b.Name, b.Expense)).ToList(), CNavy));

                var notes = new List<string>();
                Bucket peak = dated.OrderByDescending(b => b.Expense).First();
                notes.Add($"أعلى {UnitWord()} إنفاقاً {peak.Name} بمبلغ {Money(peak.Expense)} ({Pct(total > 0 ? peak.Expense / total * 100 : 0)} من الإجمالي).");
                var empty = cats.Where(ct => ct.Rows.Count == 0).Select(ct => ct.Label).ToList();
                if (empty.Count > 0)
                    notes.Add($"فئات بدون مصاريف {PeriodWord()}: {string.Join("، ", empty)}.");
                Bullets(d, notes);
            }

            // Category details
            if (active.Count > 0)
            {
                d.Ensure(200);
                SectionTitle(d, "تفاصيل الفئات");
                foreach (CatInfo ct in active)
                {
                    d.Ensure(60 + 44 + 40 * 3);
                    double by = d.Y;
                    Box(d.C, PgM, by, PgCW, 52, "#22" + ct.Color.Substring(1), null, 10);
                    Box(d.C, PgR - 8, by, 8, 52, ct.Color, null, 4);
                    Txt(d.C, ct.Label, PgR - 26, by + 10, 24, ct.Color, true);
                    Txt(d.C, $"{ct.Rows.Count} عملية  ·  {Pct(total > 0 ? ct.Total / total * 100 : 0)} من الإجمالي", PgR - 230, by + 14, 17, CMuted);
                    Txt(d.C, Money(ct.Total), PgM + 24, by + 11, 22, ct.Color, true, Anchor.Left);
                    d.Y += 64;
                    d.Section = $"تفاصيل المصاريف · {ct.Label}";

                    var rows = ct.Rows.Select(ex => new[]
                    {
                        ex.ExpenseDate.ToString("dd/MM/yyyy", Inv),
                        CleanExpenseText(ex.Description, ct.Label),
                        Money(ex.Amount)
                    }).ToList();
                    rows.Add(new[] { "مجموع الفئة", "", Money(ct.Total) });
                    Table(d, new[] { "التاريخ", "التفاصيل", "المبلغ" }, new[] { .20, .55, .25 }, "RRC",
                          new[] { CNavy, CNavy, CNavy }, rows, true);
                }
            }

            // Categories without expenses
            var emptyCats = cats.Where(ct => ct.Rows.Count == 0).ToList();
            if (emptyCats.Count > 0)
            {
                d.Ensure(220);
                SectionTitle(d, $"فئات بدون مصاريف ({emptyCats.Count} فئات)");
                var rows = emptyCats.Select(ct => new[] { ct.Label, "لا توجد مصاريف", "0 ل.س" }).ToList();
                Table(d, new[] { "الفئة", "الحالة", "المبلغ" }, new[] { .30, .40, .30 }, "RCC",
                      new[] { CNavy, CMuted, CMuted }, rows, false);
            }
        }

        // ═════════════════════════════════════════════════════════════════════
        // SECTION 3 — العلاجات
        // ═════════════════════════════════════════════════════════════════════

        private void BuildTreatmentsSection(ReportDoc d, ReportData r)
        {
            string footer = $"إحصائيات العلاجات · {CurrentPeriodLabel()}";
            ReportBanner(d, "إحصائيات العلاجات", $"الفترة: {CurrentPeriodLabel()}  ·  المبالغ بالليرة السورية (ل.س)", footer);

            List<TreatmentIncomeExportRow> rows = BuildTreatmentIncomeExportRows(r.AllVisits, r.PeriodVisits, r.KnownTreatments);
            double totalIncome = rows.Sum(tr => tr.Income);
            int totalCount = rows.Sum(tr => tr.Count);
            int kinds = rows.Count(tr => tr.Count > 0);
            var byIncome = rows.Where(tr => tr.Income > 0.009).OrderByDescending(tr => tr.Income).ToList();
            var byCount = rows.Where(tr => tr.Count > 0).OrderByDescending(tr => tr.Count).ToList();
            TreatmentIncomeExportRow? topIncome = byIncome.FirstOrDefault();
            TreatmentIncomeExportRow? topCount = byCount.FirstOrDefault();
            var treatmentValues = r.PeriodVisits.SelectMany(v => ParseTreatments(v.SelectedTreatmentsJson)
                .Select(t => new { Name = string.IsNullOrWhiteSpace(t.TreatmentName) ? OtherLabel : t.TreatmentName,
                    Quantity = Math.Max(1, t.Quantity),
                    Value = (double)t.Cost * Math.Max(1, t.Quantity) *
                        (t.Currency == "USD" ? v.UsdToSypRateSnapshot > 0 ? v.UsdToSypRateSnapshot : 15000 : 1) }))
                .GroupBy(t => t.Name, StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => g.Sum(t => t.Value) / g.Sum(t => t.Quantity), StringComparer.Ordinal);

            KpiRow(d,
                new Kpi("المحصّل للعلاجات", Num(totalIncome), "ل.س · دفعات الفترة", CTeal, CTeal),
                new Kpi("عدد العلاجات المنفذة", Num(totalCount), $"من {kinds} أنواع", CNavy, CNavy),
                new Kpi("الأعلى تحصيلاً", topIncome?.Name ?? "—", topIncome != null && totalIncome > 0 ? $"{Num(topIncome.Income)} ({Pct(topIncome.Income / totalIncome * 100)})" : "", CNavy, CAmber),
                new Kpi("الأكثر تكراراً", topCount?.Name ?? "—", topCount != null && totalCount > 0 ? $"{topCount.Count} مرة ({Pct((double)topCount.Count / totalCount * 100)})" : "", CNavy, CBlue));

            // Pareto
            var paretoItems = byIncome.Select(tr => (tr.Name, tr.Income)).ToList();
            ChartPanel(d, "التحصيل حسب العلاج (مخطط باريتو)", 440, (c, x, y, w, h) => DrawPareto(c, x, y, w, h, paretoItems));

            // Donut share + count bars
            var donutItems = byIncome.Select((tr, i) => (tr.Name, tr.Income, ReportColors[i % ReportColors.Length])).ToList();
            var countRows = byCount.Select(tr => (tr.Name, (double)tr.Count)).ToList();
            double donutH = Math.Max(340, 50 + donutItems.Count * 54 + 70);
            double barsH = 24 + Math.Max(1, countRows.Count) * 40 + 56;
            double panelH = Math.Max(donutH, barsH);
            TwoPanels(d, "حصة كل علاج من التحصيل", "عدد مرات كل علاج", panelH,
                (c, x, y, w, h) => DrawDonutLegend(c, x, y, w, h, donutItems, "", "", Num, true),
                (c, x, y, w, h) => DrawHBars(c, x, y, w, h, countRows, "العلاجات ذات العدد المسجَّل فقط"));

            // Key findings
            var findings = new List<string>();
            findings.Add($"بلغ المحصّل الموزع على العلاجات {Money(totalIncome)}؛ وسُجل {Num(totalCount)} علاجاً منفذاً خلال الفترة. قد تخص الدفعات علاجات سابقة.");
            if (byIncome.Count > 0 && totalIncome > 0)
            {
                var top3 = byIncome.Take(3).ToList();
                findings.Add($"أعلى {(top3.Count == 1 ? "بند" : top3.Count == 2 ? "بندين" : "ثلاثة بنود")} تحصيلاً {(top3.Count == 1 ? "يشكل" : "تشكل")} {Pct(top3.Sum(tr => tr.Income) / totalIncome * 100)} من الإجمالي: {string.Join("، ", top3.Select(tr => tr.Name))}.");
            }
            if (topCount != null && totalCount > 0)
            {
                double countShare = (double)topCount.Count / totalCount * 100;
                double incShare = totalIncome > 0 ? topCount.Income / totalIncome * 100 : 0;
                findings.Add($"«{topCount.Name}» هو الأكثر تكراراً بـ {topCount.Count} مرة ({Pct(countShare)} من العدد)، وحصته من التحصيل {Pct(incShare)}.");
            }
            var noCount = rows.Where(tr => tr.Income > 0.009 && tr.Count == 0).Select(tr => $"«{tr.Name}»").ToList();
            if (noCount.Count > 0)
                findings.Add($"{(noCount.Count == 1 ? "بند" : "بنود")} {string.Join(" و", noCount)} سُجل لها تحصيل دون تنفيذ خلال الفترة؛ لذلك لا يتوفر لها متوسط قيمة علاج حالي.");

            d.Ensure(160);
            SectionTitle(d, "أهم النتائج");
            Bullets(d, findings);

            // Detailed table
            d.Ensure(220);
            SectionTitle(d, "جدول العلاجات التفصيلي");
            if (rows.Count == 0)
            {
                Txt(d.C, "لا توجد بيانات لهذه الفترة", PgR, d.Y, 18, CMuted);
                d.Y += 50;
            }
            else
            {
                var tableRows = rows.Select(tr => new[]
                {
                    tr.Name,
                    Num(tr.Count),
                    Num(tr.Income),
                    treatmentValues.TryGetValue(tr.Name, out double average) ? Num(average) : "—",
                    tr.Income > 0.009 && totalIncome > 0 ? Pct(tr.Income / totalIncome * 100) : "—"
                }).ToList();
                tableRows.Add(new[] { "الإجمالي", Num(totalCount), Num(totalIncome), "", totalIncome > 0 ? "100%" : "—" });
                Table(d, new[] { "اسم العلاج", "العدد", "المحصّل (ل.س)", "متوسط قيمة العلاج", "حصة التحصيل" }, new[] { .32, .10, .22, .19, .17 }, "RCCCC",
                      new[] { CNavy, CNavy, CTeal, CNavy, CMuted }, tableRows, true);
                Bullets(d, new[] { "متوسط قيمة العلاج = مجموع قيم العلاجات المنفذة خلال الفترة ÷ كميتها، بصرف الدولار المحفوظ لكل زيارة. وهو مستقل عن الدفعات المحصلة.",
                    "التحصيل موزع على العلاجات بحسب ترتيب تكاليفها في سجل المريض؛ وقد يشمل سداد علاجات من فترات سابقة." });
            }
        }

        // ═════════════════════════════════════════════════════════════════════
        // Data helpers
        // ═════════════════════════════════════════════════════════════════════

        private List<Bucket> BuildBuckets(ReportData r)
        {
            var list = new List<Bucket>();
            if (_subTab == SubTab.Daily)
            {
                for (int h = 0; h < 24; h++)
                {
                    int hour = h;
                    list.Add(new Bucket(Two(hour), $"{Two(hour)}:00",
                        r.Visits.Where(v => v.VisitDate.Hour == hour).Sum(v => v.TodayPaid),
                        r.Expenses.Where(ex => ex.ExpenseDate.Hour == hour).Sum(ex => ex.Amount)));
                }
            }
            else if (_subTab == SubTab.Monthly)
            {
                DateTime start = SelectedPeriodStart(), end = SelectedPeriodEnd();
                for (DateTime day = start; day <= end; day = day.AddDays(1))
                {
                    DateTime dayCopy = day;
                    list.Add(new Bucket(day.Day.ToString(Inv), day.ToString("dd/MM", Inv),
                        r.Visits.Where(v => v.VisitDate.Date == dayCopy.Date).Sum(v => v.TodayPaid),
                        r.Expenses.Where(ex => ex.ExpenseDate.Date == dayCopy.Date).Sum(ex => ex.Amount)));
                }
            }
            else
            {
                for (int m = 1; m <= 12; m++)
                {
                    int month = m;
                    list.Add(new Bucket(Two(month), ArabicMonth(month),
                        r.Visits.Where(v => v.VisitDate.Month == month).Sum(v => v.TodayPaid),
                        r.Expenses.Where(ex => ex.ExpenseDate.Month == month).Sum(ex => ex.Amount)));
                }
            }
            return list;
        }

        private static List<CatInfo> BuildExpenseCategories(List<ExpenseEntry> expenses)
        {
            var labels = ExpenseCategories.Labels;
            var raw = labels.Select(label =>
            {
                var rows = expenses.Where(ex => ClassifyExpense(ex.Description) == label).OrderBy(ex => ex.ExpenseDate).ToList();
                return (Label: label, Rows: rows, Total: rows.Sum(ex => ex.Amount));
            })
            .OrderByDescending(t => t.Total)
            .ToList();

            return raw.Select((t, i) => new CatInfo(t.Label, t.Rows, t.Total, ReportColors[i % ReportColors.Length])).ToList();
        }

        private static List<(string Label, double Value)> BuildVisitDistribution(List<Visit> visits)
        {
            string[] labels = { "0", "500", "750", "1,000", "1,500", "2,000+" };
            double[] counts = new double[6];
            foreach (Visit v in visits)
            {
                double p = v.TodayPaid;
                int idx = p <= 0.009 ? 0 : p <= 500 ? 1 : p <= 750 ? 2 : p <= 1000 ? 3 : p <= 1500 ? 4 : 5;
                counts[idx]++;
            }
            return labels.Select((l, i) => (l, counts[i])).ToList();
        }

        private static string CleanExpenseText(string? desc, string category)
        {
            string t = (desc ?? "").Trim();
            if (t.StartsWith(category, StringComparison.Ordinal))
            {
                string rest = t.Substring(category.Length).TrimStart(' ', '-', '–', ':', '،');
                if (rest.Length > 0) return rest;
            }
            return t.Length == 0 ? "بدون تفاصيل" : t;
        }

        private string UnitWord() => _subTab switch { SubTab.Daily => "ساعة", SubTab.Yearly => "شهر", _ => "يوم" };
        private string UnitAcc() => _subTab switch { SubTab.Daily => "ساعة", SubTab.Yearly => "شهراً", _ => "يوماً" };
        private string PeriodWord() => _subTab switch { SubTab.Daily => "هذا اليوم", SubTab.Yearly => "هذه السنة", _ => "هذا الشهر" };

        private string PeriodHeaderLine() => _subTab switch
        {
            SubTab.Daily => $"الفترة: {CurrentPeriodLabel()}",
            _ => $"الفترة: {CurrentPeriodLabel()}  ·  من {SelectedPeriodStart().ToString("dd/MM/yyyy", Inv)} إلى {SelectedPeriodEnd().ToString("dd/MM/yyyy", Inv)}"
        };

        private static string ArabicDay(DayOfWeek day) => day switch
        {
            DayOfWeek.Sunday => "الأحد",
            DayOfWeek.Monday => "الاثنين",
            DayOfWeek.Tuesday => "الثلاثاء",
            DayOfWeek.Wednesday => "الأربعاء",
            DayOfWeek.Thursday => "الخميس",
            DayOfWeek.Friday => "الجمعة",
            _ => "السبت"
        };

        private static string Two(int n) => n.ToString("00", Inv);

        // Negative numbers are wrapped in an LTR embedding so the minus sign stays on the left.
        private static string Num(double v)
        {
            double rounded = Math.Round(v, MidpointRounding.AwayFromZero);
            string s = Math.Abs(rounded).ToString("N0", Inv);
            return rounded < 0 ? "\u202A-" + s + "\u202C" : s;
        }
        private static string Money(double v) => $"{Num(v)} ل.س";
        private static string Pct(double v) => $"{v.ToString("0.0", Inv)}%";

        // ═════════════════════════════════════════════════════════════════════
        // Drawing primitives
        // ═════════════════════════════════════════════════════════════════════

        private static TextBlock Txt(Canvas c, string text, double x, double y, double size, string color,
                                     bool bold = false, Anchor anchor = Anchor.Right, double maxWidth = 0)
        {
            var tb = new TextBlock
            {
                Text = text ?? "",
                FontFamily = new FontFamily("Segoe UI"),
                FontSize = size,
                FontWeight = bold ? FontWeights.Bold : FontWeights.Normal,
                Foreground = HexBrush(color),
                FlowDirection = text.Any(ch => ch >= '\u0600' && ch <= '\u06ff') ? FlowDirection.RightToLeft : FlowDirection.LeftToRight,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            tb.Measure(new Size(maxWidth > 0 ? maxWidth : double.PositiveInfinity, double.PositiveInfinity));
            double w = Math.Ceiling(tb.DesiredSize.Width) + 2;
            tb.Width = w;
            double left = anchor switch { Anchor.Right => x - w, Anchor.Center => x - w / 2, _ => x };
            Canvas.SetLeft(tb, left);
            Canvas.SetTop(tb, y);
            c.Children.Add(tb);
            return tb;
        }

        // Wrapped paragraph; returns its height. Default alignment follows the RTL flow (right edge).
        private static double Para(Canvas c, string text, double xRight, double y, double width, double size, string color,
                                   bool bold = false, bool center = false)
        {
            var tb = new TextBlock
            {
                Text = text ?? "",
                FontFamily = new FontFamily("Segoe UI"),
                FontSize = size,
                FontWeight = bold ? FontWeights.Bold : FontWeights.Normal,
                Foreground = HexBrush(color),
                FlowDirection = FlowDirection.RightToLeft,
                TextWrapping = TextWrapping.Wrap,
                LineHeight = size * 1.6,
                LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
                Width = width
            };
            if (center) tb.TextAlignment = TextAlignment.Center;
            tb.Measure(new Size(width, double.PositiveInfinity));
            Canvas.SetLeft(tb, xRight - width);
            Canvas.SetTop(tb, y);
            c.Children.Add(tb);
            return tb.DesiredSize.Height;
        }

        private static Border Box(Canvas c, double x, double y, double w, double h, string fill, string? stroke = null,
                                  double radius = 0, bool shadow = false)
        {
            var b = new Border
            {
                Width = Math.Max(0, w),
                Height = Math.Max(0, h),
                Background = HexBrush(fill),
                CornerRadius = new CornerRadius(radius)
            };
            if (stroke != null)
            {
                b.BorderBrush = HexBrush(stroke);
                b.BorderThickness = new Thickness(1.5);
            }
            if (shadow)
                b.Effect = new DropShadowEffect { BlurRadius = 16, ShadowDepth = 4, Direction = 270, Opacity = 0.14, Color = Colors.Black };
            Canvas.SetLeft(b, x);
            Canvas.SetTop(b, y);
            c.Children.Add(b);
            return b;
        }

        private static void HLine(Canvas c, double x1, double x2, double y, string color, double thickness = 1)
        {
            c.Children.Add(new Line { X1 = x1, X2 = x2, Y1 = y, Y2 = y, Stroke = HexBrush(color), StrokeThickness = thickness });
        }

        private static (double Min, double Max, double Step) NiceScale(double min, double max, int target = 5)
        {
            if (max <= min) max = min + 1;
            double raw = (max - min) / target;
            double mag = Math.Pow(10, Math.Floor(Math.Log10(raw)));
            double norm = raw / mag;
            double step = (norm <= 1 ? 1 : norm <= 2 ? 2 : norm <= 2.5 ? 2.5 : norm <= 5 ? 5 : 10) * mag;
            return (Math.Floor(min / step) * step, Math.Ceiling(max / step) * step, step);
        }

        // ── Page furniture ────────────────────────────────────────────────────

        private static void ReportBanner(ReportDoc d, string title, string period, string footer)
        {
            d.NewPage(footer);
            Canvas c = d.C;
            var band = new Rectangle
            {
                Width = PgW,
                Height = 310,
                Fill = new LinearGradientBrush(Color.FromRgb(0x0F, 0x2A, 0x4D), Color.FromRgb(0x1D, 0x45, 0x7C), 20)
            };
            Canvas.SetLeft(band, 0);
            Canvas.SetTop(band, 0);
            c.Children.Add(band);

            var logo = new Image { Source = ReportLogo(), Width = 60, Height = 60 };
            Canvas.SetLeft(logo, PgM); Canvas.SetTop(logo, 40); c.Children.Add(logo);
            Txt(c, "عيادتي · My Clinic", PgM + 78, 52, 24, "#FFFFFF", true, Anchor.Left);
            Box(c, PgR - 150, 48, 150, 38, CTeal, null, 19);
            Txt(c, "تقرير إحصائي", PgR - 75, 53, 18, "#FFFFFF", false, Anchor.Center);
            Txt(c, title, PgR, 112, 46, "#FFFFFF", true);
            Txt(c, period, PgR, 184, 22, "#C9D5E8");
            Txt(c, $"تاريخ التصدير: {d.CreatedAt:dd/MM/yyyy HH:mm} · جميع المبالغ بالليرة السورية", PgR, 226, 20, "#C9D5E8");
            d.Section = title;
            d.Y = 278;
        }

        private static void KpiRow(ReportDoc d, params Kpi[] cards)
        {
            const double gap = 20, h = 132;
            double w = (PgCW - gap * (cards.Length - 1)) / cards.Length;
            double y = d.Y;
            for (int i = 0; i < cards.Length; i++)
            {
                Kpi k = cards[i];
                double x = PgR - w - i * (w + gap);
                Box(d.C, x, y, w, h, k.Strip, null, 14, true);
                Box(d.C, x, y + 5, w, h - 5, "#FFFFFF", CBorder, 14);
                Box(d.C, x + 1, y + 1, w - 2, 6, k.Strip, null, 3);
                Txt(d.C, k.Label, x + w - 22, y + 22, 17, CMuted, false, Anchor.Right, w - 44);
                double size = k.Value.Length <= 9 ? 40 : k.Value.Length <= 12 ? 32 : 26;
                Txt(d.C, k.Value, x + w - 22, y + 50 + (40 - size) / 2, size, k.ValueColor, true, Anchor.Right, w - 44);
                Txt(d.C, k.Sub, x + w - 22, y + 100, 15, CMuted, false, Anchor.Right, w - 44);
            }
            d.Y = y + h + 46;
        }

        private static void TitleAt(Canvas c, double xRight, double y, string text)
        {
            Box(c, xRight - 6, y + 4, 6, 30, CTeal, null, 3);
            Txt(c, text, xRight - 22, y, 26, CNavy, true);
        }

        private static void SectionTitle(ReportDoc d, string text)
        {
            d.Ensure(170);
            d.Section = text;
            TitleAt(d.C, PgR, d.Y, text);
            d.Y += 56;
        }

        private static void ChartPanel(ReportDoc d, string title, double h, Action<Canvas, double, double, double, double> draw)
        {
            d.Ensure(56 + h + 30);
            SectionTitle(d, title);
            Box(d.C, PgM, d.Y, PgCW, h, "#FFFFFF", CBorder, 16);
            draw(d.C, PgM, d.Y, PgCW, h);
            d.Y += h + 36;
        }

        private static void TwoPanels(ReportDoc d, string rightTitle, string leftTitle, double h,
            Action<Canvas, double, double, double, double> drawRight,
            Action<Canvas, double, double, double, double> drawLeft)
        {
            d.Ensure(56 + h + 30);
            double colW = (PgCW - 40) / 2;
            double rx = PgR - colW, lx = PgM;
            TitleAt(d.C, PgR, d.Y, rightTitle);
            TitleAt(d.C, lx + colW, d.Y, leftTitle);
            double y = d.Y + 56;
            Box(d.C, rx, y, colW, h, "#FFFFFF", CBorder, 16);
            Box(d.C, lx, y, colW, h, "#FFFFFF", CBorder, 16);
            drawRight(d.C, rx, y, colW, h);
            drawLeft(d.C, lx, y, colW, h);
            d.Y = y + h + 36;
        }

        private static void Bullets(ReportDoc d, IEnumerable<string> lines)
        {
            foreach (string line in lines)
            {
                double required = WrappedLines(line, PgCW - 30, 19, false).Count * 30.4 + 14;
                d.Ensure(required);
                Txt(d.C, "•", PgR, d.Y - 3, 26, CTeal, true);
                double height = Para(d.C, line, PgR - 30, d.Y, PgCW - 30, 19, CNavy);
                d.Y += height + 14;
            }
            d.Y += 14;
        }

        // ── Table ─────────────────────────────────────────────────────────────
        // Column 0 is the right-most column. align: one char per column (R / C / L).
        // colors: hex, or "sign" (red when negative) / "type" (دخل teal, مصروف red).
        private static void Table(ReportDoc d, string[] headers, double[] widths, string align, string[] colors,
                                  List<string[]> rows, bool totalLast)
        {
            const double size = 21, lineH = 33, padding = 12;
            int n = widths.Length;
            double[] colW = widths.Select(wd => wd * PgCW).ToArray();
            double[] colRight = new double[n];
            double cur = PgR;
            for (int i = 0; i < n; i++) { colRight[i] = cur; cur -= colW[i]; }

            double Height(List<string>[] cells) => cells.Max(c => c.Count) * lineH + padding * 2;
            List<string>[] Lines(string[] cells, bool bold) => cells.Select((text, i) => WrappedLines(text, colW[i] - 28, size, bold)).ToArray();
            void Cells(List<string>[] cells, double y, int first, int count, bool bold, bool header = false)
            {
                for (int i = 0; i < n; i++)
                {
                    string color = header || (bold && colors[i] != "sign") ? CNavy : ResolveColor(colors[i], string.Join("", cells[i]));
                    for (int line = first; line < Math.Min(first + count, cells[i].Count); line++)
                    {
                        Anchor anchor = align[i] == 'R' ? Anchor.Right : align[i] == 'L' ? Anchor.Left : Anchor.Center;
                        double x = anchor == Anchor.Right ? colRight[i] - 14 : anchor == Anchor.Left ? colRight[i] - colW[i] + 14 : colRight[i] - colW[i] / 2;
                        Txt(d.C, cells[i][line], x, y + padding + (line - first) * lineH, size, color, bold, anchor);
                    }
                }
            }
            var heading = Lines(headers, true);
            double headH = Height(heading);
            string section = d.Section;
            void Header(bool continued)
            {
                if (continued)
                {
                    TitleAt(d.C, PgR, d.Y, section + " (تابع)"); d.Y += 56;
                }
                Box(d.C, PgM, d.Y, PgCW, headH, CHead, null, 8);
                Cells(heading, d.Y, 0, heading.Max(c => c.Count), true, true);
                d.Y += headH;
            }
            int initialPage = d.Pages.Count;
            d.Ensure(headH + (rows.Count == 0 ? 0 : Math.Min(Height(Lines(rows[0], totalLast && rows.Count == 1)), 180)));
            Header(d.Pages.Count != initialPage);
            for (int ri = 0; ri < rows.Count; ri++)
            {
                bool isTotal = totalLast && ri == rows.Count - 1;
                var cells = Lines(rows[ri], isTotal);
                int lineCount = cells.Max(c => c.Count), first = 0;
                double rowH = Height(cells);
                double together = rowH;
                if (totalLast && ri == rows.Count - 2) together += Height(Lines(rows[ri + 1], true));
                // Keep ordinary rows together; very long descriptions continue without losing text.
                if (d.Y + together > PgH - 110 && together < PgH - 110 - 156 - headH)
                { d.NewPage(); Header(true); }
                while (first < lineCount)
                {
                    int capacity = (int)((PgH - 110 - d.Y - padding * 2) / lineH);
                    if (capacity < 1) { d.NewPage(); Header(true); continue; }
                    int count = Math.Min(capacity, lineCount - first);
                    double height = count * lineH + padding * 2;
                    if (isTotal) Box(d.C, PgM, d.Y + 2, PgCW, height - 4, CHead, null, 8);
                    else if (ri % 2 == 1) Box(d.C, PgM, d.Y, PgCW, height, "#F8FAFC");
                    Cells(cells, d.Y, first, count, isTotal);
                    HLine(d.C, PgM, PgR, d.Y + height, CBorder);
                    d.Y += height; first += count;
                    if (first < lineCount) { d.NewPage(); Header(true); }
                }
            }
            d.Y += 24;
        }

        private static List<string> WrappedLines(string text, double width, double size, bool bold)
        {
            var result = new List<string>();
            var measure = new TextBlock { FontFamily = new FontFamily("Segoe UI"), FontSize = size,
                FontWeight = bold ? FontWeights.Bold : FontWeights.Normal, FlowDirection = FlowDirection.RightToLeft };
            foreach (string paragraph in (text ?? "").Replace("\r", "").Split('\n'))
            {
                string line = "";
                foreach (string word in paragraph.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                {
                    string candidate = line.Length == 0 ? word : line + " " + word;
                    measure.Text = candidate; measure.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                    if (measure.DesiredSize.Width > width && line.Length > 0) { result.Add(line); line = ""; }
                    string rest = word;
                    measure.Text = rest; measure.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                    if (measure.DesiredSize.Width > width)
                    {
                        var elements = StringInfo.GetTextElementEnumerator(rest);
                        while (elements.MoveNext())
                        {
                            string element = elements.GetTextElement();
                            measure.Text = line + element; measure.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                            if (measure.DesiredSize.Width > width && line.Length > 0) { result.Add(line); line = ""; }
                            line += element;
                        }
                    }
                    else line = line.Length == 0 ? rest : line + " " + rest;
                }
                result.Add(line);
            }
            return result;
        }

        private static string ResolveColor(string spec, string text) => spec switch
        {
            "sign" => text.Contains('-') ? CRed : (text == "0" ? CMuted : CTeal),
            "type" => text == "تحصيل" ? CTeal : CRed,
            _ => spec
        };

        // ═════════════════════════════════════════════════════════════════════
        // Charts
        // ═════════════════════════════════════════════════════════════════════

        private static void DrawGroupedBars(Canvas c, double x, double y, double w, double h, List<Bucket> data)
        {
            Box(c, x + w - 40, y + 22, 14, 14, CTeal, null, 3);
            Txt(c, "المحصّل", x + w - 48, y + 16, 16, CMuted);
            Box(c, x + w - 130, y + 22, 14, 14, CRed, null, 3);
            Txt(c, "المصاريف", x + w - 138, y + 16, 16, CMuted);

            double plotL = x + 84, plotR = x + w - 24, plotT = y + 64, plotB = y + h - 46, plotH = plotB - plotT;
            double maxV = Math.Max(1, data.Max(b => Math.Max(b.Income, b.Expense)));
            var (_, niceMax, step) = NiceScale(0, maxV, 5);

            for (double v = 0; v <= niceMax + step * 0.01; v += step)
            {
                double yy = plotB - v / niceMax * plotH;
                HLine(c, plotL, plotR, yy, v == 0 ? "#9CA3AF" : "#E8ECF2");
                Txt(c, Num(v), plotL - 10, yy - 10, 13, CMuted);
            }

            int n = data.Count;
            double slot = (plotR - plotL) / n;
            double barW = Math.Min(slot * 0.34, 26);
            int stride = (int)Math.Ceiling(n / 16.0);
            for (int i = 0; i < n; i++)
            {
                double cx = plotL + slot * i + slot / 2;
                double hi = data[i].Income / niceMax * plotH;
                double he = data[i].Expense / niceMax * plotH;
                if (data[i].Income > 0) Box(c, cx - barW - 1, plotB - Math.Max(2, hi), barW, Math.Max(2, hi), CTeal, null, 2);
                if (data[i].Expense > 0) Box(c, cx + 1, plotB - Math.Max(2, he), barW, Math.Max(2, he), CRed, null, 2);
                if (i % stride == 0) Txt(c, data[i].Label, cx, plotB + 10, 13, CMuted, false, Anchor.Center);
            }
        }

        private static void DrawCumulative(Canvas c, double x, double y, double w, double h, List<Bucket> data)
        {
            double running = 0;
            var vals = new List<double>();
            foreach (Bucket b in data) { running += b.Income - b.Expense; vals.Add(running); }

            double plotL = x + 90, plotR = x + w - 30, plotT = y + 40, plotB = y + h - 44, plotH = plotB - plotT;
            var (sMin, sMax, step) = NiceScale(Math.Min(0, vals.Min()), Math.Max(0, vals.Max()), 6);

            double Yof(double v) => plotB - (v - sMin) / (sMax - sMin) * plotH;

            for (double v = sMin; v <= sMax + step * 0.01; v += step)
            {
                double yy = Yof(v);
                HLine(c, plotL, plotR, yy, Math.Abs(v) < step * 0.001 ? "#9CA3AF" : "#E8ECF2");
                Txt(c, Num(v), plotL - 10, yy - 10, 13, CMuted);
            }

            int n = data.Count;
            double XOf(int i) => n == 1 ? (plotL + plotR) / 2 : plotL + i * (plotR - plotL) / (n - 1);
            var pts = new PointCollection();
            for (int i = 0; i < n; i++) pts.Add(new Point(XOf(i), Yof(vals[i])));

            var area = new PointCollection(pts) { new Point(XOf(n - 1), Yof(0)), new Point(XOf(0), Yof(0)) };
            c.Children.Add(new Polygon { Points = area, Fill = HexBrush("#2210A18C") });
            c.Children.Add(new Polyline { Points = pts, Stroke = HexBrush(CNavy), StrokeThickness = 3, StrokeLineJoin = PenLineJoin.Round });

            int stride = (int)Math.Ceiling(n / 16.0);
            for (int i = 0; i < n; i++)
            {
                Ellipse dot = new() { Width = 8, Height = 8, Fill = HexBrush(CNavy) };
                Canvas.SetLeft(dot, XOf(i) - 4); Canvas.SetTop(dot, Yof(vals[i]) - 4);
                c.Children.Add(dot);
                if (i % stride == 0) Txt(c, data[i].Label, XOf(i), plotB + 10, 13, CMuted, false, Anchor.Center);
            }
            Txt(c, Num(vals[n - 1]), XOf(n - 1), Yof(vals[n - 1]) - 30, 15, CNavy, true, Anchor.Center);
        }

        private static void DrawSimpleBars(Canvas c, double x, double y, double w, double h, List<(string Label, double Value)> items, string color)
        {
            double plotL = x + 84, plotR = x + w - 24, plotT = y + 50, plotB = y + h - 50, plotH = plotB - plotT;
            double maxV = Math.Max(1, items.Max(it => it.Value));
            var (_, niceMax, step) = NiceScale(0, maxV, 5);
            for (double v = 0; v <= niceMax + step * 0.01; v += step)
            {
                double yy = plotB - v / niceMax * plotH;
                HLine(c, plotL, plotR, yy, v == 0 ? "#9CA3AF" : "#E8ECF2");
                Txt(c, Num(v), plotL - 10, yy - 10, 13, CMuted);
            }
            int n = items.Count;
            double slot = (plotR - plotL) / n;
            double barW = Math.Min(slot * 0.5, 64);
            for (int i = 0; i < n; i++)
            {
                double cx = plotL + slot * i + slot / 2;
                double bh = Math.Max(2, items[i].Value / niceMax * plotH);
                Box(c, cx - barW / 2, plotB - bh, barW, bh, color, null, 4);
                if (n <= 16) Txt(c, Num(items[i].Value), cx, plotB - bh - 22, 14, CNavy, true, Anchor.Center);
                Txt(c, items[i].Label, cx, plotB + 10, 14, CMuted, false, Anchor.Center, slot);
            }
        }

        private static void DrawPareto(Canvas c, double x, double y, double w, double h, List<(string Name, double Income)> items)
        {
            if (items.Count == 0)
            {
                Txt(c, "لا توجد بيانات لهذه الفترة", x + w / 2, y + h / 2 - 12, 18, CMuted, false, Anchor.Center);
                return;
            }

            Box(c, x + w - 40, y + 26, 14, 14, CNavy, null, 3);
            Txt(c, "المحصّل (ل.س)", x + w - 48, y + 19, 16, CMuted);
            Box(c, x + w - 210, y + 26, 14, 14, CAmber, null, 3);
            Txt(c, "النسبة التراكمية", x + w - 218, y + 19, 16, CMuted);

            double total = items.Sum(it => it.Income);
            double plotL = x + 84, plotR = x + w - 84, plotT = y + 76, plotB = y + h - 96, plotH = plotB - plotT;
            var (_, niceMax, step) = NiceScale(0, items.Max(it => it.Income), 4);

            for (double v = 0; v <= niceMax + step * 0.01; v += step)
            {
                double yy = plotB - v / niceMax * plotH;
                HLine(c, plotL, plotR, yy, v == 0 ? "#9CA3AF" : "#E8ECF2");
                Txt(c, Num(v), plotL - 10, yy - 10, 13, CMuted);
            }
            foreach (int p in new[] { 0, 25, 50, 75, 100 })
                Txt(c, $"{p}%", plotR + 10, plotB - p / 100.0 * plotH - 10, 13, CAmber, false, Anchor.Left);

            int n = items.Count;
            double slot = (plotR - plotL) / n;
            double barW = Math.Min(slot * 0.55, 64);
            var pts = new PointCollection();
            double cum = 0;
            for (int i = 0; i < n; i++)
            {
                double cx = plotL + slot * i + slot / 2;
                double bh = Math.Max(2, items[i].Income / niceMax * plotH);
                Box(c, cx - barW / 2, plotB - bh, barW, bh, ReportColors[i % ReportColors.Length], null, 4);
                Txt(c, Num(items[i].Income), cx, plotB - bh - 22, 13, CNavy, true, Anchor.Center);
                Para(c, items[i].Name, cx + (slot - 8) / 2, plotB + 12, slot - 8, 15, CMuted, false, true);
                cum += items[i].Income;
                pts.Add(new Point(cx, plotB - cum / total * plotH));
            }
            c.Children.Add(new Polyline { Points = pts, Stroke = HexBrush(CAmber), StrokeThickness = 3, StrokeLineJoin = PenLineJoin.Round });
            foreach (Point pt in pts)
            {
                Ellipse dot = new() { Width = 11, Height = 11, Fill = Brushes.White, Stroke = HexBrush(CAmber), StrokeThickness = 2.5 };
                Canvas.SetLeft(dot, pt.X - 5.5); Canvas.SetTop(dot, pt.Y - 5.5);
                c.Children.Add(dot);
            }
        }

        private static void DrawDonutLegend(Canvas c, double x, double y, double w, double h,
            List<(string Label, double Value, string Color)> items, string centerText, string centerSub,
            Func<double, string> fmt, bool totalLine)
        {
            var data = items.Where(it => it.Value > 0.0001).ToList();
            if (data.Count == 0)
            {
                Txt(c, "لا توجد بيانات لهذه الفترة", x + w / 2, y + h / 2 - 12, 18, CMuted, false, Anchor.Center);
                return;
            }

            double total = data.Sum(it => it.Value);
            double r = 112, inner = 64, cx = x + w - 28 - r, cy = y + h / 2;
            double start = -90;

            if (data.Count == 1)
            {
                Ellipse ring = new() { Width = r * 2, Height = r * 2, Fill = HexBrush(data[0].Color) };
                Canvas.SetLeft(ring, cx - r); Canvas.SetTop(ring, cy - r);
                c.Children.Add(ring);
                Ellipse hole = new() { Width = inner * 2, Height = inner * 2, Fill = Brushes.White };
                Canvas.SetLeft(hole, cx - inner); Canvas.SetTop(hole, cy - inner);
                c.Children.Add(hole);
            }
            else
            {
                foreach (var it in data)
                {
                    double sweep = it.Value / total * 360.0;
                    c.Children.Add(CreateDonutSlice(cx, cy, r, inner, start, Math.Min(sweep, 359.99), HexBrush(it.Color)));
                    start += sweep;
                }
            }

            if (!string.IsNullOrEmpty(centerText))
            {
                Txt(c, centerText, cx, cy - 22, 24, CNavy, true, Anchor.Center);
                if (!string.IsNullOrEmpty(centerSub)) Txt(c, centerSub, cx, cy + 10, 13, CMuted, false, Anchor.Center);
            }

            double edge = cx - r - 34;
            double block = data.Count * 54 + (totalLine ? 56 : 0);
            double yy = Math.Max(y + 16, y + (h - block) / 2);
            double maxW = Math.Max(60, edge - 28 - (x + 16));
            foreach (var it in data)
            {
                Ellipse dot = new() { Width = 14, Height = 14, Fill = HexBrush(it.Color) };
                Canvas.SetLeft(dot, edge - 14); Canvas.SetTop(dot, yy + 7);
                c.Children.Add(dot);
                Txt(c, it.Label, edge - 26, yy, 18, CNavy, true, Anchor.Right, maxW);
                Txt(c, $"{fmt(it.Value)} ({Pct(it.Value / total * 100)})", edge - 26, yy + 26, 16, CMuted, false, Anchor.Right, maxW);
                yy += 54;
            }
            if (totalLine)
            {
                HLine(c, x + 20, edge, yy + 4, CBorder);
                Txt(c, $"الإجمالي: {fmt(total)}", edge - 26, yy + 14, 18, CNavy, true, Anchor.Right, maxW);
            }
        }

        private static void DrawHBars(Canvas c, double x, double y, double w, double h,
            List<(string Label, double Value)> rows, string caption)
        {
            if (rows.Count == 0)
            {
                Txt(c, "لا توجد بيانات لهذه الفترة", x + w / 2, y + h / 2 - 12, 18, CMuted, false, Anchor.Center);
                return;
            }
            double max = Math.Max(1, rows.Max(rw => rw.Value));
            double labelRight = x + w - 24, trackRight = labelRight - 190, trackLeft = x + 64, trackW = trackRight - trackLeft;
            double yy = y + 24;
            foreach (var row in rows)
            {
                Txt(c, row.Label, labelRight, yy + 4, 18, CMuted, false, Anchor.Right, 180);
                Box(c, trackLeft, yy + 10, trackW, 16, CTrack, null, 8);
                double fw = row.Value > 0 ? Math.Max(8, row.Value / max * trackW) : 0;
                if (fw > 0) Box(c, trackRight - fw, yy + 10, fw, 16, CBlue, null, 8);
                Txt(c, Num(row.Value), x + 24, yy + 4, 18, CNavy, true, Anchor.Left);
                yy += 40;
            }
            Txt(c, caption, labelRight, yy + 6, 15, CMuted);
        }

    }
}
