using MyClinic.Models;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace MyClinic.OriginalTheme
{
    // Both interface themes share the records workflow; this adapter supplies the original palette.
    public partial class PatientRecordsView : UserControl
    {
        public PatientRecordsView()
        {
            InitializeComponent();
            ApplyPalette(false);
            Loaded += (_, _) => { ApplyPalette(IsDark()); GlobalEvents.OnThemeChanged -= ApplyPalette; GlobalEvents.OnThemeChanged += ApplyPalette; };
            Unloaded += (_, _) => GlobalEvents.OnThemeChanged -= ApplyPalette;
        }
        private bool IsDark() => Window.GetWindow(this) is MainWindow main ? main.IsDarkTheme : Application.Current?.MainWindow is MainWindow app && app.IsDarkTheme;
        private void ApplyPalette(bool dark)
        {
            Resources["DashCalendarTodayNumFg"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(dark ? "#2DD4BF" : "#0F766E"));
            Resources["DashCanalBoxBg"] = Application.Current.TryFindResource("AppInputBg") ?? (dark ? Brushes.DimGray : Brushes.White);
            Resources["DashCanalBoxBorder"] = Application.Current.TryFindResource("AppBorder") ?? (dark ? Brushes.DimGray : Brushes.White);
            Resources["DashCanalBoxFg"] = Application.Current.TryFindResource("AppTextPrimary") ?? (dark ? Brushes.DimGray : Brushes.White);
            Resources["PatientCardBg"] = Application.Current.TryFindResource("AppCardBg") ?? (dark ? Brushes.DimGray : Brushes.White);
            Resources["PatientCardBorder"] = Application.Current.TryFindResource("AppBorder") ?? (dark ? Brushes.DimGray : Brushes.White);
            Resources["SettingsBannerBg"] = Application.Current.TryFindResource("AppCardBg") ?? (dark ? Brushes.DimGray : Brushes.White);
            Resources["SettingsBannerBorder"] = Application.Current.TryFindResource("AppBorder") ?? (dark ? Brushes.DimGray : Brushes.White);
            Resources["SettingsBannerPillBorder"] = Application.Current.TryFindResource("AppBorder") ?? (dark ? Brushes.DimGray : Brushes.White);
            Resources["SettingsBannerSubFg"] = Application.Current.TryFindResource("AppTextSecondary") ?? (dark ? Brushes.DimGray : Brushes.White);
            Resources["SettingsBannerTitleFg"] = Application.Current.TryFindResource("AppTextPrimary") ?? (dark ? Brushes.DimGray : Brushes.White);
            Resources["SettingsCardBg"] = Application.Current.TryFindResource("AppCardBg") ?? (dark ? Brushes.DimGray : Brushes.White);
            Resources["SettingsCardBorder"] = Application.Current.TryFindResource("AppBorder") ?? (dark ? Brushes.DimGray : Brushes.White);
            Resources["SettingsPrimaryBtnBg"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(dark ? "#2DD4BF" : "#0F766E"));
            Resources["SettingsPrimaryBtnFg"] = dark ? Brushes.Black : Brushes.White;
            Resources["VisitBannerBg"] = Application.Current.TryFindResource("AppCardBg") ?? (dark ? Brushes.DimGray : Brushes.White);
            Resources["VisitBannerBorder"] = Application.Current.TryFindResource("AppBorder") ?? (dark ? Brushes.DimGray : Brushes.White);
            Resources["VisitBannerSubFg"] = Application.Current.TryFindResource("AppTextSecondary") ?? (dark ? Brushes.DimGray : Brushes.White);
            Resources["VisitBannerTitleFg"] = Application.Current.TryFindResource("AppTextPrimary") ?? (dark ? Brushes.DimGray : Brushes.White);
            Resources["VisitBannerToothBrush"] = Brushes.Transparent;
            Resources["VisitCardBg"] = Application.Current.TryFindResource("AppCardBg") ?? (dark ? Brushes.DimGray : Brushes.White);
            Resources["VisitCardBorder"] = Application.Current.TryFindResource("AppBorder") ?? (dark ? Brushes.DimGray : Brushes.White);
            Resources["VisitCostPillBg"] = Application.Current.TryFindResource("AppInputBg") ?? (dark ? Brushes.DimGray : Brushes.White);
            Resources["VisitCostPillBorder"] = Application.Current.TryFindResource("AppBorder") ?? (dark ? Brushes.DimGray : Brushes.White);
            Resources["VisitCostPillFg"] = Application.Current.TryFindResource("AppTextPrimary") ?? (dark ? Brushes.DimGray : Brushes.White);
            Resources["VisitGlowBoxBg"] = Application.Current.TryFindResource("AppInputBg") ?? (dark ? Brushes.DimGray : Brushes.White);
            Resources["VisitGlowBoxBorder"] = Application.Current.TryFindResource("AppBorder") ?? (dark ? Brushes.DimGray : Brushes.White);
            Resources["VisitGlowBoxFg"] = Application.Current.TryFindResource("AppTextPrimary") ?? (dark ? Brushes.DimGray : Brushes.White);
            Resources["VisitGlowOpacity"] = 0.0;
            Resources["VisitModalChipBg"] = Application.Current.TryFindResource("AppInputBg") ?? (dark ? Brushes.DimGray : Brushes.White);
            Resources["VisitModalChipBorder"] = Application.Current.TryFindResource("AppBorder") ?? (dark ? Brushes.DimGray : Brushes.White);
            Resources["VisitModalOuterBg"] = Application.Current.TryFindResource("AppCardBg") ?? (dark ? Brushes.DimGray : Brushes.White);
            Resources["VisitModalOuterBorder"] = Application.Current.TryFindResource("AppBorder") ?? (dark ? Brushes.DimGray : Brushes.White);
            Resources["VisitPrimaryBtnBg"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(dark ? "#2DD4BF" : "#0F766E"));
            Resources["VisitPrimaryBtnBorder"] = Application.Current.TryFindResource("AppBorder") ?? (dark ? Brushes.DimGray : Brushes.White);
            Resources["VisitQtyPillBg"] = Application.Current.TryFindResource("AppInputBg") ?? (dark ? Brushes.DimGray : Brushes.White);
            Resources["VisitQtyPillBorder"] = Application.Current.TryFindResource("AppBorder") ?? (dark ? Brushes.DimGray : Brushes.White);
            Resources["VisitQtyPillFg"] = Application.Current.TryFindResource("AppTextPrimary") ?? (dark ? Brushes.DimGray : Brushes.White);
            Resources["VisitSecondaryBtnBg"] = Application.Current.TryFindResource("AppCardBg") ?? (dark ? Brushes.DimGray : Brushes.White);
            Resources["VisitSecondaryBtnBorder"] = Application.Current.TryFindResource("AppBorder") ?? (dark ? Brushes.DimGray : Brushes.White);
            Resources["VisitSecondaryBtnFg"] = Application.Current.TryFindResource("AppTextPrimary") ?? (dark ? Brushes.DimGray : Brushes.White);
            Resources["VisitSubCardBg"] = Application.Current.TryFindResource("AppInputBg") ?? (dark ? Brushes.DimGray : Brushes.White);
            Resources["VisitSubCardBorder"] = Application.Current.TryFindResource("AppBorder") ?? (dark ? Brushes.DimGray : Brushes.White);
            Resources["VisitToothBadgeBg"] = Application.Current.TryFindResource("AppInputBg") ?? (dark ? Brushes.DimGray : Brushes.White);
            Resources["VisitToothBadgeBorder"] = Application.Current.TryFindResource("AppBorder") ?? (dark ? Brushes.DimGray : Brushes.White);
            Resources["VisitToothBadgeFg"] = Application.Current.TryFindResource("AppTextPrimary") ?? (dark ? Brushes.DimGray : Brushes.White);
            Resources["VisitTreatmentPillBg"] = Application.Current.TryFindResource("AppInputBg") ?? (dark ? Brushes.DimGray : Brushes.White);
            Resources["VisitTreatmentPillBorder"] = Application.Current.TryFindResource("AppBorder") ?? (dark ? Brushes.DimGray : Brushes.White);
            Resources["VisitTreatmentPillFg"] = Application.Current.TryFindResource("AppTextPrimary") ?? (dark ? Brushes.DimGray : Brushes.White);
            Resources["VisitWarningCardBg"] = Application.Current.TryFindResource("AppCardBg") ?? (dark ? Brushes.DimGray : Brushes.White);
            Resources["VisitWarningCardBorder"] = Application.Current.TryFindResource("AppBorder") ?? (dark ? Brushes.DimGray : Brushes.White);
            Resources["VisitWarningFg"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(dark ? "#FCD34D" : "#92400E"));
        }
        public void RequestRefresh() => Records.RequestRefresh();
        public Task EnsureDataCurrentAsync() => Records.EnsureDataCurrentAsync();
    }

    // ── Public model types ────────────────────────────────────────────────────────

    public sealed class PatientCardModel
    {
        public int      Id              { get; init; }
        public string   DisplayName     { get; init; } = string.Empty;
        public string   PhoneNumber     { get; init; } = string.Empty;
        public string   AgeText         { get; init; } = "--";
        public string   AgeWithUnit     { get; init; } = "العمر غير محدد";
        public string   GenderLabel     { get; init; } = "غير محدد";
        public int      VisitCount      { get; init; }
        public DateTime LatestVisitDate { get; init; }
        public string   BloodType       { get; init; } = "غير محدد";
        public string   Allergies       { get; init; } = "غير محددة";
        public string   ChronicDiseases { get; init; } = "غير محددة";
        public string   SmokingSummary  { get; init; } = "غير مدخن";

        public string VisitCountLabel =>
            VisitCount == 1 ? "1 زيارة" : $"{VisitCount} زيارات";
    }

    public sealed class VisitHistoryItem
    {
        public int      VisitId             { get; init; }       // unique DB id — used as cache key
        public bool     HasEditHistory      { get; init; }
        public DateTime VisitDate           { get; init; }
        public string   VisitDateText       { get; init; } = string.Empty;
        public string   VisitDateTimeText   { get; init; } = string.Empty;
        public string   Title               { get; init; } = string.Empty;
        public string   Details             { get; init; } = string.Empty;
        public bool     HasAttachments      { get; init; }
        public string   AttachmentLabel     { get; init; } = string.Empty;
        public string   PregnancySummary    { get; init; } = "غير مسجل";
        public string   BloodPressureText   { get; init; } = "--";
        public string   HeartRateText       { get; init; } = "--";
        public string   TemperatureText     { get; init; } = "--";
        public string   RespiratoryRateText { get; init; } = "--";
        public string   WeightText          { get; init; } = "--";
        public string   HeightText          { get; init; } = "--";
        public string   BloodSugarText      { get; init; } = "--";
        public string   BmiText             { get; init; } = "--";
        public string   SymptomsText        { get; init; } = "لا توجد شكوى مسجلة";
        public string   DiagnosisText       { get; init; } = "لا يوجد تشخيص مسجل";
        public string   ChartModeText       { get; init; } = "بالغين (Adult)";
        public IReadOnlyList<string>                  ToothIds          { get; init; } = Array.Empty<string>();
        public string   TreatmentPlanText   { get; init; } = "لا توجد خطة علاج أو ملاحظات إضافية.";
        public string   FinalTreatmentText  { get; init; } = "لا يوجد إجراء نهائي مسجل.";
        public IReadOnlyList<PrescriptionDisplayItem> PrescriptionItems { get; init; } = Array.Empty<PrescriptionDisplayItem>();
        public IReadOnlyList<string>                  AttachmentPaths   { get; init; } = Array.Empty<string>();
        public IReadOnlyList<SelectedTreatment>       SelectedTreatments { get; init; } = Array.Empty<SelectedTreatment>();
    }

    public sealed class VisitDetailsViewModel
    {
        public int VisitId { get; init; }
        public bool HasEditHistory { get; init; }
        public string PatientName           { get; init; } = string.Empty;
        public string? ChartModeText        { get; set; }
        public string DoctorName            { get; init; } = string.Empty;
        public string VisitDateTimeText     { get; init; } = string.Empty;
        public string BloodTypeText         { get; init; } = "غير محدد";
        public string AllergiesText         { get; init; } = "غير محددة";
        public string ChronicDiseasesText   { get; init; } = "غير محددة";
        public string SmokingText           { get; init; } = "غير مدخن";
        public string PregnancyText         { get; init; } = "غير مسجل";
        public string BloodPressureText     { get; init; } = "--";
        public string HeartRateText         { get; init; } = "--";
        public string TemperatureText       { get; init; } = "--";
        public string RespiratoryRateText   { get; init; } = "--";
        public string WeightText            { get; init; } = "--";
        public string HeightText            { get; init; } = "--";
        public string BloodSugarText        { get; init; } = "--";
        public string BmiText               { get; init; } = "--";
        public string SymptomsText          { get; init; } = "لا توجد شكوى مسجلة";
        public string DiagnosisText         { get; init; } = "لا يوجد تشخيص مسجل";
        public string DentalChartCategoryText { get; init; } = "بالغين (Adult)";
        public IReadOnlyList<string>                  Teeth             { get; init; } = Array.Empty<string>();
        public string TreatmentPlanText     { get; init; } = "لا توجد خطة علاج أو ملاحظات إضافية.";
        public string FinalTreatmentText    { get; init; } = "لا يوجد إجراء نهائي مسجل.";
        public IReadOnlyList<PrescriptionDisplayItem> PrescriptionItems { get; init; } = Array.Empty<PrescriptionDisplayItem>();
        public IReadOnlyList<SelectedTreatment>       SelectedTreatments { get; init; } = Array.Empty<SelectedTreatment>();
        public ObservableCollection<VisitAttachmentItem> AttachmentItems { get; init; } = new();

        public bool   HasTeeth               => Teeth.Count > 0;
        public bool   HasSelectedTreatments  => SelectedTreatments.Count > 0;
        public bool   HasFinalTreatment  => !string.IsNullOrWhiteSpace(FinalTreatmentText)
                                         && FinalTreatmentText != "لا يوجد إجراء نهائي مسجل.";
        public bool   HasPrescriptions   => PrescriptionItems.Count > 0;
        public bool   HasAttachments     => AttachmentItems.Count > 0;
        public string AttachmentCountText => AttachmentItems.Count == 1
                                         ? "1 ملف" : $"{AttachmentItems.Count} ملفات";
    }

    public sealed class PrescriptionDisplayItem
    {
        public string MedicineName    { get; init; } = string.Empty;
        public string InstructionText { get; init; } = "بدون تعليمات إضافية";
    }

    public sealed class VisitAttachmentItem
    {
        public string      FilePath  { get; init; } = string.Empty;
        public string      FileName  { get; init; } = string.Empty;
        public BitmapImage Thumbnail { get; init; } = null!;
    }
}
