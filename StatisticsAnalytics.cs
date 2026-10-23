using System.Globalization;

namespace MyClinic;

internal enum StatisticsPeriod { Day, Month, Year }
internal sealed record StatisticsObservation(DateTime Date, double Value);
internal sealed record StatisticsCategory(string Label, double Value, string Color);
internal sealed record StatisticsBucket(DateTime Date, DateTime? PreviousDate, double? Value, double? PreviousValue);

// Calendar comparisons are shared by both interface styles and independent of chart rendering.
internal sealed class StatisticsAnalytics
{
    public required StatisticsPeriod Period { get; init; }
    public required DateTime Start { get; init; }
    public required DateTime End { get; init; }
    public required DateTime PreviousStart { get; init; }
    public required DateTime PreviousEnd { get; init; }
    public required IReadOnlyList<StatisticsBucket> Buckets { get; init; }
    public required IReadOnlyList<double> DailyValues { get; init; }
    public required string Title { get; init; }
    public bool Money { get; init; }
    public bool LowerIsBetter { get; init; }
    public double Total => DailyValues.Sum();
    public double PreviousTotal { get; init; }
    public int ElapsedDays => DailyValues.Count;
    public double Average => ElapsedDays == 0 ? 0 : Total / ElapsedDays;
    public DateTime? PeakDate { get; init; }
    public double Peak { get; init; }
    public string Unit => Money ? "ل.س" : "العدد";
    public string Format(double value) => Money ? $"{value:N0} ل.س" : $"{value:N0}";
    public static string Change(double current, double previous) => previous == 0
        ? current == 0 ? "لا تغيير" : "لا توجد قاعدة للمقارنة"
        : $"{(current > previous ? "↑" : current < previous ? "↓" : "=")} {Math.Abs((current - previous) / previous):P1}";
    public static DateTime PreviousPeriodStart(DateTime start, StatisticsPeriod period) => period switch
    {
        StatisticsPeriod.Day => start.AddDays(-1),
        StatisticsPeriod.Month => start.AddMonths(-1),
        _ => start.AddYears(-1)
    };
    public static DateTime PeriodEnd(DateTime start, StatisticsPeriod period) => period switch
    {
        StatisticsPeriod.Day => start,
        StatisticsPeriod.Month => start.AddMonths(1).AddDays(-1),
        _ => start.AddYears(1).AddDays(-1)
    };
    public static StatisticsAnalytics Build(IEnumerable<StatisticsObservation> observations,
        DateTime start, StatisticsPeriod period, DateTime today, string title, bool money, bool lowerIsBetter = false)
    {
        start = start.Date; today = today.Date;
        DateTime end = PeriodEnd(start, period), previousStart = PreviousPeriodStart(start, period);
        DateTime previousEnd = PeriodEnd(previousStart, period);
        DateTime cutoff = end < today ? end : today;
        bool ongoing = today >= start && today < end;
        // In a partial year, match month/day rather than ordinal day (leap years differ).
        if (ongoing)
            previousEnd = period == StatisticsPeriod.Year
                ? new DateTime(previousStart.Year, today.Month, Math.Min(today.Day, DateTime.DaysInMonth(previousStart.Year, today.Month)))
                : previousStart.AddDays(Math.Min((cutoff - start).Days, (previousEnd - previousStart).Days));
        var days = observations.Where(o => o.Date.Date <= today).GroupBy(o => o.Date.Date)
            .ToDictionary(g => g.Key, g => g.Sum(o => o.Value));
        var daily = cutoff < start ? new List<double>() : Enumerable.Range(0, (cutoff - start).Days + 1)
            .Select(i => days.GetValueOrDefault(start.AddDays(i))).ToList();
        var buckets = new List<StatisticsBucket>();
        int count = period == StatisticsPeriod.Year ? 12 : period == StatisticsPeriod.Month ? end.Day : 1;
        for (int i = 0; i < count; i++)
        {
            DateTime date = period == StatisticsPeriod.Year ? start.AddMonths(i) : start.AddDays(i);
            DateTime? previousDate = period == StatisticsPeriod.Year ? previousStart.AddMonths(i)
                : previousStart.AddDays(i) <= PeriodEnd(previousStart, period) ? previousStart.AddDays(i) : null;
            double? value = date > cutoff ? null : period == StatisticsPeriod.Year
                ? days.Where(d => d.Key >= date && d.Key < date.AddMonths(1) && d.Key <= cutoff).Sum(d => d.Value)
                : days.GetValueOrDefault(date);
            double? previousValue = date > cutoff || previousDate == null || previousDate > previousEnd ? null
                : period == StatisticsPeriod.Year
                    ? days.Where(d => d.Key >= previousDate && d.Key < previousDate.Value.AddMonths(1) && d.Key <= previousEnd).Sum(d => d.Value)
                    : days.GetValueOrDefault(previousDate.Value);
            buckets.Add(new(date, previousDate, value, previousValue));
        }
        // Total comparisons include all days of a completed previous month, even when month lengths differ.
        double previousTotal = cutoff < start ? 0 : days.Where(d => d.Key >= previousStart && d.Key <= previousEnd).Sum(d => d.Value);
        double peak = daily.Count == 0 ? 0 : daily.Max();
        return new StatisticsAnalytics
        {
            Period = period, Start = start, End = end, PreviousStart = previousStart, PreviousEnd = previousEnd,
            Buckets = buckets, DailyValues = daily, PreviousTotal = previousTotal, Title = title, Money = money,
            LowerIsBetter = lowerIsBetter, Peak = peak, PeakDate = peak > 0 ? start.AddDays(daily.IndexOf(peak)) : null
        };
    }
}
