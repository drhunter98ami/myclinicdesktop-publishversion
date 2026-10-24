using Microsoft.EntityFrameworkCore;
using MyClinic.Models;
using MyClinic.Services;
using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace MyClinic;

public sealed class TreatmentSettingRow
{
    public int Id { get; init; }
    public string Name { get; init; } = "";
    public string PriceText { get; set; } = "";
    public string Currency { get; set; } = "SYP";
    public bool RequiresCanals { get; init; }
    public string[] Currencies { get; } = ["SYP", "USD"];
}

public partial class SettingsView : UserControl
{
    static string _lastCategory = "عام"; static double _lastScroll;
    readonly Dictionary<string, string> _keywords = new()
    {
        ["عام"] = "اسم المستخدم تسجيل الدخول الاسم",
        ["المظهر"] = "التصميم الجديد الأصلي الداكن الفاتح النظام الحركة التأثيرات",
        ["الحسابات"] = "سعر الصرف الدولار الليرة الوقت التاريخ الساعة متقدمة",
        ["العلاجات"] = "تكاليف العلاجات الأسعار السعر العملة الأقنية سحب عصب",
        ["النسخ الاحتياطي"] = "Google Drive نسخ استعادة نسخة حساب اتصال",
        ["حول التطبيق"] = "التحديثات الإصدار مساعدة المطور تواصل واتساب"
    };
    List<TreatmentCost> _treatments = new(); bool _ready, _loading, _busy, _backupBusy; decimal _savedRate;
    readonly DispatcherTimer _clock = new() { Interval = TimeSpan.FromSeconds(1) };
    readonly Func<AppDbContext> _createContext;
    public SettingsView() : this(() => new AppDbContext()) { }
    public SettingsView(Func<AppDbContext> createContext)
    {
        _createContext = createContext;
        InitializeComponent(); Categories.ItemsSource = _keywords.Keys.ToList();
        ColorMode.ItemsSource = new[] { "الوضع الفاتح", "الوضع الداكن", "اتباع النظام" };
        MotionLevelComboBox.ItemsSource = new[] { "بدون حركة", "حركة خفيفة", "حركة كاملة" };
        TreatmentCurrency.ItemsSource = new[] { "SYP", "USD" }; TreatmentCurrency.SelectedIndex = 0;
        TreatmentFilterCurrency.ItemsSource = new[] { "كل العملات", "SYP", "USD" }; TreatmentFilterCurrency.SelectedIndex = 0;
        VersionLabel.Text = $"My Clinic · الإصدار {UpdateService.CurrentVersion.ToString(3)}";
        ReleaseNotes.Text = "• تنظيم النواقص وتسجيل المشتريات الجزئية وسجل المصاريف.\n• فصل تسليم أعمال المخبر عن الدفعات مع حفظ سعر الصرف.\n• إعدادات مصنّفة وخيارات الأقنية ومعاينة المظهر والحركة.\n• عرض حالة النسخ الاحتياطي ونسخة استرداد محلية قبل الاستعادة.";
        _clock.Tick += (_, _) => UpdateClockStatus(); _ready = true;
        Loaded += LoadedView; Unloaded += UnloadedView;
    }
    async void LoadedView(object sender, RoutedEventArgs e)
    {
        _loading = true;
        try
        {
            WorkspaceUi.Configure(this); GlobalEvents.OnThemeChanged += ThemeChanged; MotionPreferences.Changed += MotionPreferencesChanged;
            Username.Text = LoginSessionStore.CurrentUsername; SaveUsername.IsEnabled = false;
            ColorMode.SelectedIndex = ThemePreference.FollowSystem ? 2 : IsDark ? 1 : 0; MotionLevelComboBox.SelectedIndex = (int)MotionPreferences.Current;
            ModernPreview.BorderThickness = new(InterfaceThemeSettings.IsOriginalTheme ? 1 : 2); OriginalPreview.BorderThickness = new(InterfaceThemeSettings.IsOriginalTheme ? 2 : 1);
            using var db = _createContext(); var settings = await db.AppSettings.AsNoTracking().FirstOrDefaultAsync();
            _savedRate = settings?.UsdToSypRate ?? 15000; ExchangeRate.Text = _savedRate.ToString("0.##"); SavedRate.Text = $"المحفوظ: 1 USD = {_savedRate:N2} SYP\nآخر تحديث: {settings?.UpdatedAt:dd/MM/yyyy HH:mm}";
            AppDate.SelectedDate = AppClock.Today; AppTime.Text = AppClock.Now.ToString("HH:mm"); UpdateClockStatus(); _clock.Start();
            await LoadTreatments(); ShowBackupInfo();
            Categories.SelectedItem = _lastCategory; if (Categories.SelectedIndex < 0) Categories.SelectedIndex = 0;
            ShowCategory(); ContentScroll.ScrollToVerticalOffset(_lastScroll);
        }
        catch (Exception ex) { UsernameStatus.Text = "تعذر تحميل الإعدادات: " + ex.Message; }
        finally { _loading = false; Rate_Changed(this, new RoutedEventArgs()); }
    }
    void UnloadedView(object sender, RoutedEventArgs e) { _clock.Stop(); GlobalEvents.OnThemeChanged -= ThemeChanged; MotionPreferences.Changed -= MotionPreferencesChanged; }
    bool IsDark => Window.GetWindow(this) is MainWindow m ? m.IsDarkTheme : Window.GetWindow(this) is OriginalTheme.MainWindow o && o.IsDarkTheme;
    void ThemeChanged(bool dark) { WorkspaceUi.Configure(this); if (!ThemePreference.FollowSystem) { _loading = true; ColorMode.SelectedIndex = dark ? 1 : 0; _loading = false; } }
    void MotionPreferencesChanged(object? sender, EventArgs e) { if (!MotionPreferences.Enabled) { FinanceMotion.StopTree(this); BackupProgress.IsIndeterminate = false; } }
    void Category_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!_ready) return; if (!_loading) { _lastScroll = 0; ContentScroll.ScrollToTop(); } ShowCategory();
    }
    void SettingsSearch_Changed(object sender, TextChangedEventArgs e)
    {
        if (!_ready) return; string search = SettingsSearch.Text.Trim(); var matches = _keywords.Where(k => (k.Key + " " + k.Value).Contains(search, StringComparison.CurrentCultureIgnoreCase)).Select(k => k.Key).ToList();
        string? selected = Categories.SelectedItem?.ToString(); Categories.ItemsSource = matches; Categories.SelectedItem = selected;
        if (Categories.SelectedIndex < 0 && matches.Count > 0) Categories.SelectedIndex = 0; ShowCategory();
    }
    void ShowCategory()
    {
        if (!_ready) return; string? selected = Categories.SelectedItem?.ToString();
        foreach (var (name, card) in new[] { ("عام", GeneralCard), ("المظهر", AppearanceCard), ("الحسابات", AccountsCard), ("العلاجات", TreatmentsCard), ("النسخ الاحتياطي", BackupCard), ("حول التطبيق", AboutCard) }) card.Visibility = selected == name ? Visibility.Visible : Visibility.Collapsed;
        NoResults.Visibility = selected == null ? Visibility.Visible : Visibility.Collapsed;
        if (selected != null) { _lastCategory = selected; if (!_loading) WorkspaceUi.Reveal(CategoryContent); }
    }
    void Content_Scrolled(object sender, ScrollChangedEventArgs e) { if (!_loading && e.VerticalChange != 0) _lastScroll = e.VerticalOffset; }
    void Page_SizeChanged(object sender, SizeChangedEventArgs e) => NavColumn.Width = new GridLength(ActualWidth < 850 ? 120 : 164);
    void Username_Changed(object sender, TextChangedEventArgs e) { if (_ready && !_loading) { SaveUsername.IsEnabled = Username.Text.Trim().Length > 0 && Username.Text.Trim() != LoginSessionStore.CurrentUsername; UsernameStatus.Text = "تغييرات غير محفوظة"; } }
    async void SaveUsername_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return; string value = Username.Text.Trim(); if (value.Length == 0) return; _busy = true; SaveUsername.IsEnabled = false;
        try
        {
            using var db = _createContext(); string current = LoginSessionStore.CurrentUsername;
            var user = await db.Users.SingleOrDefaultAsync(u => u.Username == current) ?? throw new InvalidOperationException("تعذر تحديد المستخدم الحالي.");
            if (await db.Users.AnyAsync(u => u.Id != user.Id && u.Username == value)) throw new InvalidOperationException("اسم المستخدم مستخدم مسبقاً.");
            user.Username = value; await db.SaveChangesAsync(); LoginSessionStore.UpdateUsername(value);
            if (Window.GetWindow(this) is MainWindow m) m.RefreshWelcomeName(); else if (Window.GetWindow(this) is OriginalTheme.MainWindow o) o.RefreshWelcomeName();
            UsernameStatus.Text = "✓ تم حفظ اسم تسجيل الدخول.";
        }
        catch (Exception ex) { UsernameStatus.Text = ex.Message; SaveUsername.IsEnabled = true; }
        finally { _busy = false; }
    }
    void Modern_Click(object sender, RoutedEventArgs e) => SwitchDesign(false);
    void Original_Click(object sender, RoutedEventArgs e) => SwitchDesign(true);
    void SwitchDesign(bool original)
    {
        try { if (Window.GetWindow(this) is MainWindow m) m.SetInterfaceTheme(original); else if (Window.GetWindow(this) is OriginalTheme.MainWindow o) o.SetInterfaceTheme(original); }
        catch (Exception ex) { AppearanceStatus.Text = ex.Message; }
    }
    void ColorMode_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!_ready || _loading || ColorMode.SelectedIndex < 0) return;
        try
        {
            ThemePreference.Set(ColorMode.SelectedIndex == 2); bool dark = ColorMode.SelectedIndex == 2 ? ThemePreference.SystemDark : ColorMode.SelectedIndex == 1;
            if (Window.GetWindow(this) is MainWindow m) m.SetDarkTheme(dark); else if (Window.GetWindow(this) is OriginalTheme.MainWindow o) o.SetDarkTheme(dark);
            AppearanceStatus.Text = "✓ تم تطبيق المظهر.";
        }
        catch (Exception ex) { AppearanceStatus.Text = ex.Message; }
    }
    void Motion_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!_ready || _loading || MotionLevelComboBox.SelectedIndex < 0) return;
        try { MotionPreferences.Set((MotionLevel)MotionLevelComboBox.SelectedIndex); AppearanceStatus.Text = "✓ تم حفظ مستوى الحركة."; WorkspaceUi.Reveal(MotionSample); }
        catch (Exception ex) { AppearanceStatus.Text = ex.Message; }
    }
    void PreviewMotion_Click(object sender, RoutedEventArgs e) { AppearanceStatus.Text = MotionPreferences.Enabled ? "معاينة الحركة · تظهر العناصر بهدوء عند تغيّرها" : "الحركة متوقفة · تبقى النتيجة ظاهرة فوراً"; WorkspaceUi.Reveal(MotionSample); }
    void Rate_Changed(object sender, RoutedEventArgs e)
    {
        if (!_ready) return; bool valid = WorkspaceUi.Number(ExchangeRate.Text, out var rate) && rate > 0 && rate <= 1_000_000_000m;
        RateExample.Text = valid ? $"مثال: 10 USD = {10 * rate:N2} SYP" : "أدخل سعر صرف موجباً."; SaveRate.IsEnabled = valid && rate != _savedRate;
    }
    async void SaveRate_Click(object sender, RoutedEventArgs e)
    {
        if (_busy || !WorkspaceUi.Number(ExchangeRate.Text, out decimal rate) || rate <= 0 || rate > 1_000_000_000m) return;
        _busy = true; SaveRate.IsEnabled = false;
        try
        {
            using var db = _createContext(); var settings = await db.AppSettings.FirstOrDefaultAsync();
            if (settings == null) { settings = new(); db.AppSettings.Add(settings); }
            settings.UsdToSypRate = rate; settings.UpdatedAt = AppClock.Now; await db.SaveChangesAsync(); _savedRate = rate;
            SavedRate.Text = $"المحفوظ: 1 USD = {rate:N2} SYP\nآخر تحديث: {settings.UpdatedAt:dd/MM/yyyy HH:mm}"; RateStatus.Text = "✓ تم حفظ سعر الصرف وتحديث التقديرات."; GlobalEvents.NotifyExchangeRateChanged();
        }
        catch (Exception ex) { RateStatus.Text = ex.Message; SaveRate.IsEnabled = true; }
        finally { _busy = false; }
    }
    void UpdateClockStatus() => ClockStatus.Text = $"وقت التطبيق الآن: {AppClock.Now:dd/MM/yyyy HH:mm:ss}\n" + (AppClock.IsAdjusted ? "تعديل وقت التطبيق مفعّل" : "الوقت الافتراضي مفعّل");
    async Task NotifyClock(DateTime previous)
    { if (Window.GetWindow(this) is MainWindow m) await m.NotifyAppTimeChangedAsync(previous); else if (Window.GetWindow(this) is OriginalTheme.MainWindow o) await o.NotifyAppTimeChangedAsync(previous); AppDate.SelectedDate = AppClock.Today; AppTime.Text = AppClock.Now.ToString("HH:mm"); UpdateClockStatus(); }
    async void SaveClock_Click(object sender, RoutedEventArgs e)
    {
        if (!TimeSpan.TryParseExact(WorkspaceUi.Normalize(AppTime.Text), @"hh\:mm", CultureInfo.InvariantCulture, out var time) || !AppDate.SelectedDate.HasValue || time.TotalHours >= 24) { ValidationAssist.Show(AppTime, "أدخل تاريخاً ووقتاً صحيحين بصيغة HH:mm."); return; }
        try { DateTime previous = AppClock.Now; AppClock.SetCurrentTime(AppDate.SelectedDate.Value.Date.Add(time)); await NotifyClock(previous); RateStatus.Text = "✓ تم حفظ وقت التطبيق."; } catch (Exception ex) { RateStatus.Text = ex.Message; }
    }
    async void ResetClock_Click(object sender, RoutedEventArgs e)
    { try { DateTime previous = AppClock.Now; AppClock.ResetToComputerTime(); await NotifyClock(previous); } catch (Exception ex) { RateStatus.Text = ex.Message; } }
    async Task LoadTreatments()
    { using var db = _createContext(); _treatments = await db.TreatmentCosts.AsNoTracking().OrderBy(t => t.TreatmentName).ToListAsync(); FilterTreatments(); }
    void TreatmentFilter_Changed(object sender, RoutedEventArgs e) { if (_ready) FilterTreatments(); }
    void FilterTreatments()
    {
        var rows = _treatments.Where(t => t.TreatmentName.Contains(TreatmentSearch.Text.Trim(), StringComparison.CurrentCultureIgnoreCase) && (TreatmentFilterCurrency.SelectedIndex <= 0 || t.Currency == TreatmentFilterCurrency.SelectedItem?.ToString()))
            .Select(t => new TreatmentSettingRow { Id = t.Id, Name = t.TreatmentName, PriceText = t.Cost.ToString("0.##"), Currency = t.Currency, RequiresCanals = t.RequiresCanals }).ToList();
        TreatmentsGrid.ItemsSource = rows; TreatmentStatus.Text = rows.Count == 0 ? "لا توجد علاجات تطابق البحث." : $"{rows.Count} علاج · تحفظ التغييرات بزر حفظ في الصف";
    }
    async Task PersistTreatment(int? id, string name, decimal cost, string currency, bool canals)
    {
        if (string.IsNullOrWhiteSpace(name) || cost <= 0 || cost > 1_000_000_000m || currency is not ("USD" or "SYP")) throw new InvalidOperationException("أكمل اسم العلاج والسعر الموجب والعملة.");
        using var db = _createContext();
        if ((await db.TreatmentCosts.AsNoTracking().ToListAsync()).Any(t => t.Id != id && t.TreatmentName.Equals(name.Trim(), StringComparison.CurrentCultureIgnoreCase))) throw new InvalidOperationException("اسم العلاج موجود مسبقاً.");
        var treatment = id.HasValue ? await db.TreatmentCosts.SingleAsync(t => t.Id == id) : new TreatmentCost();
        treatment.TreatmentName = name.Trim(); treatment.Cost = cost; treatment.Currency = currency; treatment.RequiresCanals = canals;
        if (!id.HasValue) db.TreatmentCosts.Add(treatment); await db.SaveChangesAsync(); await LoadTreatments(); TreatmentStatus.Text = "✓ تم حفظ العلاج.";
    }
    async void AddTreatment_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return; _busy = true; AddTreatment.IsEnabled = false;
        try { if (!WorkspaceUi.Number(TreatmentPrice.Text, out decimal cost)) throw new InvalidOperationException("أدخل سعراً صحيحاً."); await PersistTreatment(null, TreatmentName.Text, cost, TreatmentCurrency.SelectedItem?.ToString() ?? "SYP", RequiresCanals.IsChecked == true); TreatmentName.Clear(); TreatmentPrice.Clear(); RequiresCanals.IsChecked = false; }
        catch (Exception ex) { TreatmentStatus.Text = ex.Message; }
        finally { _busy = false; AddTreatment.IsEnabled = true; }
    }
    async void SaveTreatmentRow_Click(object sender, RoutedEventArgs e)
    {
        if (_busy || (sender as FrameworkElement)?.DataContext is not TreatmentSettingRow row) return; _busy = true;
        try { if (!WorkspaceUi.Number(row.PriceText, out var cost)) throw new InvalidOperationException("أدخل سعراً صحيحاً."); await PersistTreatment(row.Id, row.Name, cost, row.Currency, row.RequiresCanals); }
        catch (Exception ex) { TreatmentStatus.Text = ex.Message; }
        finally { _busy = false; }
    }
    void EditTreatment_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not TreatmentSettingRow row) return;
        var dialog = new ClinicDialog(this, "تعديل العلاج"); var name = dialog.Input("اسم العرض", row.Name); var price = dialog.Input("السعر", row.PriceText, true); var currency = dialog.Choice("العملة", row.Currencies, row.Currency);
        var canals = new CheckBox { Content = "يتطلب بيانات الأقنية", IsChecked = row.RequiresCanals, Margin = new(0, 8, 0, 12) }; dialog.Fields.Children.Add(canals);
        dialog.Note("التغيير يطبّق على الاختيارات الجديدة؛ تبقى تفاصيل الزيارات المحفوظة كما سُجّلت.");
        dialog.OnSubmit(async () => { if (!WorkspaceUi.Number(price.Text, out var cost)) throw new InvalidOperationException("أدخل سعراً صحيحاً."); await PersistTreatment(row.Id, name.Text, cost, currency.SelectedItem!.ToString()!, canals.IsChecked == true); dialog.Complete(); }); dialog.ShowDialog();
    }
    void DeleteTreatment_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not TreatmentSettingRow row) return; var dialog = new ClinicDialog(this, "حذف علاج", "تأكيد الحذف");
        dialog.Note($"حذف {row.Name} من قائمة العلاجات الجديدة؟ تبقى الزيارات السابقة محفوظة.");
        dialog.OnSubmit(async () => { using var db = _createContext(); await db.TreatmentCosts.Where(t => t.Id == row.Id).ExecuteDeleteAsync(); dialog.Complete(); await LoadTreatments(); TreatmentStatus.Text = "تم حذف العلاج من القائمة."; }); dialog.ShowDialog();
    }
    void ShowBackupInfo()
    {
        var state = GoogleDriveSyncService.ReadState(); BackupInfo.Text = $"الحساب: {(state.Account.Length > 0 ? state.Account : "لم يُتحقق من الاتصال بعد")}\nآخر نسخ ناجح: {(state.LastSuccess.HasValue ? state.LastSuccess.Value.ToLocalTime().ToString("dd/MM/yyyy HH:mm") : "غير متوفر")}\nتاريخ نسخة Drive: {FormatRemoteDate(state.DriveModified)}";
        if (state.RecoveryPath.Length > 0) BackupInfo.Text += "\nنسخة الاسترداد المحلية: " + state.RecoveryPath;
        if (state.LastError.Length > 0) BackupStatus.Text = "آخر محاولة: " + state.LastError;
        if (System.IO.File.Exists(GoogleDriveSyncService.ActiveDatabasePath + ".restore-pending.db")) BackupStatus.Text = "استعادة جاهزة للتطبيق عند إعادة التشغيل التالية.";
    }
    static string FormatRemoteDate(string text) => DateTimeOffset.TryParse(text, out var date) ? date.ToLocalTime().ToString("dd/MM/yyyy HH:mm") : "غير متوفر";
    async Task BackupAction(Func<Task> action)
    {
        if (_backupBusy) return; _backupBusy = true; BackupNow.IsEnabled = InspectBackup.IsEnabled = RestoreBackup.IsEnabled = false; BackupProgress.Visibility = Visibility.Visible; BackupProgress.IsIndeterminate = MotionPreferences.Enabled;
        try { await action(); }
        catch (Exception ex) { BackupStatus.Text = ex.Message; }
        finally { _backupBusy = false; BackupNow.IsEnabled = InspectBackup.IsEnabled = RestoreBackup.IsEnabled = true; BackupProgress.Visibility = Visibility.Collapsed; ShowBackupInfo(); }
    }
    async void BackupNow_Click(object sender, RoutedEventArgs e) => await BackupAction(async () => { BackupStatus.Text = "جارٍ إنشاء نسخة متسقة ورفعها…"; var result = await GoogleDriveSyncService.BackupDatabaseAsync(); BackupStatus.Text = result.Success ? "✓ اكتمل النسخ الاحتياطي." : result.ErrorMessage; });
    async void InspectBackup_Click(object sender, RoutedEventArgs e) => await BackupAction(async () => { BackupStatus.Text = "جارٍ التحقق من Google Drive…"; var backup = await GoogleDriveSyncService.InspectBackupAsync(); BackupStatus.Text = backup == null ? "لا توجد نسخة على Drive." : $"نسخة متاحة · {backup.Size / 1024d:N0} KB"; });
    async void RestoreBackup_Click(object sender, RoutedEventArgs e) => await BackupAction(async () =>
    {
        var backup = await GoogleDriveSyncService.InspectBackupAsync() ?? throw new InvalidOperationException("لا توجد نسخة متاحة للاستعادة.");
        var dialog = new ClinicDialog(this, "استعادة بيانات العيادة", "استعادة هذه النسخة");
        dialog.Note($"الحساب: {backup.Account}\nتاريخ النسخة: {FormatRemoteDate(backup.Modified)}\nالحجم: {backup.Size / 1024d:N0} KB\n\nستستبدل هذه النسخة قاعدة البيانات الحالية بعد إعادة التشغيل. تُحفظ نسخة استرداد محلية أولاً.");
        dialog.OnSubmit(async () =>
        {
            var result = await GoogleDriveSyncService.RestoreDatabaseAsync(backup); if (!result.Success) throw new InvalidOperationException(result.ErrorMessage);
            dialog.Complete(); BackupStatus.Text = "✓ النسخة جاهزة. أعد تشغيل التطبيق لتطبيقها.";
            var restart = new ClinicDialog(this, "الاستعادة جاهزة", "إعادة التشغيل الآن"); restart.Note("حُفظت نسخة الاسترداد. يمكنك إعادة التشغيل الآن أو إغلاق التطبيق وفتحه لاحقاً.");
            restart.CancelButton.Content = "لاحقاً";
            restart.OnSubmit(() => { restart.Complete(); if (Environment.ProcessPath is string path) Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); Application.Current.Shutdown(); return Task.CompletedTask; }); restart.ShowDialog();
        }); dialog.ShowDialog();
    });
    async void CheckUpdates_Click(object sender, RoutedEventArgs e)
    {
        CheckUpdates.IsEnabled = false; UpdateStatus.Text = "جارٍ التحقق…";
        try
        {
            var update = await UpdateService.GetLatestUpdateAsync(); UpdateStatus.Text = $"آخر تحقق ناجح: {DateTime.Now:dd/MM/yyyy HH:mm}\n" + (update == null ? "أنت تستخدم أحدث نسخة متاحة." : $"نسخة متاحة: {update.Version} · {update.SizeText}");
            if (update != null)
            {
                var dialog = new ClinicDialog(this, "تحديث متاح", "فتح صفحة التنزيل"); dialog.Note($"الإصدار {update.Version}\n{update.SizeText}");
                dialog.OnSubmit(() => { Process.Start(new ProcessStartInfo(update.DownloadUrl) { UseShellExecute = true }); dialog.Complete(); return Task.CompletedTask; }); dialog.ShowDialog();
            }
        }
        catch (Exception ex) { UpdateStatus.Text = "تعذر التحقق: " + ex.Message; }
        finally { CheckUpdates.IsEnabled = true; }
    }
    void Contact_Click(object sender, RoutedEventArgs e) { try { Process.Start(new ProcessStartInfo("https://wa.me/963935606912") { UseShellExecute = true }); } catch (Exception ex) { AboutStatus.Text = ex.Message; } }
    void CopyPhone_Click(object sender, RoutedEventArgs e) => Copy("+963935606912");
    void CopyVersion_Click(object sender, RoutedEventArgs e) => Copy($"My Clinic {UpdateService.CurrentVersion.ToString(3)} · Windows {Environment.OSVersion.Version}");
    void Copy(string value) { try { Clipboard.SetText(value); AboutStatus.Text = "✓ تم النسخ."; } catch (Exception ex) { AboutStatus.Text = ex.Message; } }
}
