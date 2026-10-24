using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using System.IO;
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using System.Threading.Tasks;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Threading;
using MyClinic.Services;

namespace MyClinic
{
    public partial class MainWindow : Window
    {
        private DashboardView? _dashboardView;
        private PatientRecordsView? _patientRecordsView;
        private FinancialRecordsView? _financialRecordsView;
        private ShortagesView? _shortagesView;
        private LabView? _labView;
        private SettingsView? _settingsView;
        private StatisticsView? _statisticsView;
        private Button? _activeNavButton;

        // متغيرات المزامنة التلقائية والساعة
        private DispatcherTimer _clockTimer;
        private bool _isAutoSyncEnabled = false;
        private bool _isDarkTheme;
        private int _lastSyncedHour = -1;
        private readonly string _syncSettingsPath;
        private readonly string _themeSettingsPath;
        private static readonly HttpClient _internetClient = new() { Timeout = TimeSpan.FromSeconds(5) };
        private DateTime _lastInternetCheckUtc = DateTime.MinValue;
        private int _internetCheckVersion;

        public MainWindow()
        {
            // 1. تحديد المسار الآمن الجديد (AppData)
            string appDataFolder = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string clinicFolder = System.IO.Path.Combine(appDataFolder, "MyClinicApp");

            // التأكد من وجود المجلد
            if (!Directory.Exists(clinicFolder))
            {
                Directory.CreateDirectory(clinicFolder);
            }

            // 2. توجيه ملفات الإعدادات إلى المجلد الآمن
            _syncSettingsPath = System.IO.Path.Combine(clinicFolder, "SyncConfig.txt");
            _themeSettingsPath = System.IO.Path.Combine(clinicFolder, "ThemeConfig.txt");

            // 3. فحص وتطبيق الاستعادة قبل تشغيل أي شيء أو الاتصال بقاعدة البيانات
            ApplyPendingRestore();

            InitializeComponent();

            _activeNavButton = BtnDashboard;

            // تحميل الثيم المحفوظ
            LoadThemeSettings();
            ThemePreference.Attach(this, SetDarkTheme);

            // تهيئة إعدادات المزامنة التلقائية والساعة
            LoadSyncSettings();
            InitializeAppClock();
            NetworkChange.NetworkAvailabilityChanged += NetworkAvailabilityChanged;
            NetworkChange.NetworkAddressChanged += NetworkAddressChanged;

            _ = UpdateInternetStatusAsync();

            ShowDashboard();
            TxtCurrentVersion.Text = $"الإصدار {UpdateService.CurrentVersion.ToString(3)}";
            Loaded += MainWindow_Loaded;
        }

        private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            Loaded -= MainWindow_Loaded;
            await CheckForUpdatesAsync(showResult: false);
        }

        private void Window_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (MainContent.Content is ShortagesView or LabView or SettingsView)
            { Window_MouseLeave(sender, e); return; }
            if (!MotionPreferences.Enabled)
            {
                Window_MouseLeave(sender, e);
                return;
            }

            if (!_isDarkTheme || WindowSpotlightCanvas is null || WindowSpotlightTransform is null)
                return;

            if (WindowSpotlightCanvas.Visibility != Visibility.Visible)
                WindowSpotlightCanvas.Visibility = Visibility.Visible;

            Point pos = e.GetPosition(RootWindowGrid);
            WindowSpotlightTransform.X = pos.X - (WindowSpotlightOuter.Width / 2.0);
            WindowSpotlightTransform.Y = pos.Y - (WindowSpotlightOuter.Height / 2.0);
        }

        private void Window_MouseLeave(object sender, MouseEventArgs e)
        {
            if (WindowSpotlightTransform is not null)
            {
                WindowSpotlightTransform.X = -1200;
                WindowSpotlightTransform.Y = -1200;
            }
        }

        public Task CheckForUpdatesFromSettingsAsync(Button triggerButton) =>
            CheckForUpdatesAsync(showResult: true, triggerButton: triggerButton);

        private async Task CheckForUpdatesAsync(bool showResult, Button? triggerButton = null)
        {
            string originalContent = triggerButton?.Content?.ToString() ?? "تفقد وجود نسخة جديدة";
            if (triggerButton != null)
            {
                triggerButton.IsEnabled = false;
                triggerButton.Content = "جاري التفقد...";
            }

            try
            {
                UpdateInfo? update = await UpdateService.GetLatestUpdateAsync();
                if (update is null)
                {
                    if (showResult)
                    {
                        MessageBox.Show("أنت تستخدم أحدث نسخة متاحة حالياً.", "تفقد التحديثات", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                    return;
                }

                MessageBoxResult result = MessageBox.Show(
                    $"تتوفر نسخة جديدة: {update.Version}\nحجم الملف: {update.SizeText}\n\nهل تريد فتح صفحة التحميل؟",
                    "تحديث جديد متاح",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Information);

                if (result == MessageBoxResult.Yes)
                {
                    try
                    {
                        Process.Start(new ProcessStartInfo
                        {
                            FileName = update.DownloadUrl,
                            UseShellExecute = true
                        });
                    }
                    catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
                    {
                        MessageBox.Show(
                            "تعذر فتح صفحة تحميل التحديث. يمكنك المحاولة مرة أخرى لاحقاً.",
                            "تعذر فتح التحميل",
                            MessageBoxButton.OK,
                            MessageBoxImage.Warning);
                    }
                }
            }
            catch (Exception ex) when (
                ex is HttpRequestException or JsonException or TaskCanceledException or InvalidDataException)
            {
                if (showResult)
                {
                    MessageBox.Show(
                        "تعذر التحقق من تحديثات GitHub. تحقق من اتصال الإنترنت وحاول لاحقاً.",
                        "تعذر التفقد",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                }
            }
            finally
            {
                if (triggerButton != null)
                {
                    triggerButton.Content = originalContent;
                    triggerButton.IsEnabled = true;
                }
            }
        }

        private void InitializeAppClock()
        {
            _clockTimer = new DispatcherTimer();
            _clockTimer.Interval = TimeSpan.FromSeconds(1);
            _clockTimer.Tick += ClockTimer_Tick;
            _clockTimer.Start();
        }

        private async void ClockTimer_Tick(object? sender, EventArgs e)
        {
            DateTime appTime = AppClock.Now;

            TxtAppTime.Text = appTime.ToString("hh:mm:ss tt", new System.Globalization.CultureInfo("ar-SY"));
            TxtAppDate.Text = appTime.ToString("yyyy/MM/dd");
            TxtAppDay.Text = appTime.ToString("dddd", new System.Globalization.CultureInfo("ar-SY"));

            if (_isAutoSyncEnabled)
            {
                if ((appTime.Hour == 10 || appTime.Hour == 13 || appTime.Hour == 19) && appTime.Minute == 0)
                {
                    if (_lastSyncedHour != appTime.Hour)
                    {
                        _lastSyncedHour = appTime.Hour;
                        await ExecuteAutoSyncAsync(appTime);
                    }
                }
                else if (appTime.Minute != 0)
                {
                    _lastSyncedHour = -1;
                }

                UpdateNextSyncUI();
            }

            if ((DateTime.UtcNow - _lastInternetCheckUtc).TotalSeconds >= 5)
            {
                _ = UpdateInternetStatusAsync();
            }
        }

        private void NetworkAvailabilityChanged(object? sender, NetworkAvailabilityEventArgs e)
        {
            if (!e.IsAvailable)
            {
                ApplyInternetStatus(false);
            }

            _ = UpdateInternetStatusAsync();
        }

        private void NetworkAddressChanged(object? sender, EventArgs e)
        {
            _ = UpdateInternetStatusAsync();
        }

        private async Task UpdateInternetStatusAsync()
        {
            int checkVersion = Interlocked.Increment(ref _internetCheckVersion);
            _lastInternetCheckUtc = DateTime.UtcNow;
            bool isConnected = NetworkInterface.GetIsNetworkAvailable();

            if (isConnected)
            {
                try
                {
                    using HttpResponseMessage response = await _internetClient.GetAsync("https://www.google.com/generate_204");
                    isConnected = (int)response.StatusCode >= 200 && (int)response.StatusCode < 400;
                }
                catch
                {
                    isConnected = false;
                }
            }

            if (checkVersion != Volatile.Read(ref _internetCheckVersion))
            {
                return;
            }

            if (!Dispatcher.CheckAccess())
            {
                await Dispatcher.InvokeAsync(() => ApplyInternetStatus(isConnected));
                return;
            }

            ApplyInternetStatus(isConnected);
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            if (MainContent.Content is IRecoverableClinicForm form && !form.PreserveDraft())
            {
                e.Cancel = true;
                UiFeedback.Notify(this, "تعذر حفظ المسودة. احفظ البيانات أو تجاهل المسودة قبل إغلاق البرنامج.");
            }
            base.OnClosing(e);
        }

        protected override void OnClosed(EventArgs e)
        {
            _clockTimer?.Stop();
            _dashboardView?.ReleaseResources();
            _labView?.ReleaseResources();
            NetworkChange.NetworkAvailabilityChanged -= NetworkAvailabilityChanged;
            NetworkChange.NetworkAddressChanged -= NetworkAddressChanged;
            base.OnClosed(e);
        }

        private void ApplyInternetStatus(bool isConnected)
        {
            TxtConnectionStatus.Text = isConnected ? "متصل بالإنترنت" : "غير متصل بالإنترنت";
            string dotHex = isConnected ? "#10B981" : "#EF4444";
            ConnectionStatusDot.Fill = CreateBrush(dotHex);
            Application.Current.Resources["AppConnectionTextBrush"] = CreateBrush(isConnected
                ? (_isDarkTheme ? "#34D399" : "#10B981")
                : "#EF4444");
        }

        private async Task ExecuteAutoSyncAsync(DateTime appTime)
        {
            var result = await GoogleDriveSyncService.BackupDatabaseAsync();
            if (result.Success)
            {
                UpdateLastSyncUI(appTime);
            }
        }

        private void UpdateLastSyncUI(DateTime time)
        {
            string formattedTime = time.ToString("yyyy/MM/dd hh:mm tt", new System.Globalization.CultureInfo("ar-SY"));
            TxtLastSync.Text = $"آخر مزامنة: {formattedTime}";
            SaveSyncSettings(formattedTime);
        }

        private void UpdateNextSyncUI()
        {
            if (!_isAutoSyncEnabled)
            {
                TxtNextSync.Visibility = Visibility.Collapsed;
                return;
            }

            TxtNextSync.Visibility = Visibility.Visible;
            DateTime appTime = AppClock.Now;
            DateTime nextSyncTime = appTime.Date;

            if (appTime.Hour < 10)
                nextSyncTime = nextSyncTime.AddHours(10);
            else if (appTime.Hour < 13)
                nextSyncTime = nextSyncTime.AddHours(13);
            else if (appTime.Hour < 19)
                nextSyncTime = nextSyncTime.AddHours(19);
            else
                nextSyncTime = nextSyncTime.AddDays(1).AddHours(10);

            TxtNextSync.Text = $"المزامنة القادمة: {nextSyncTime.ToString("hh:mm tt", new System.Globalization.CultureInfo("ar-SY"))}";
        }

        private void LoadSyncSettings()
        {
            if (File.Exists(_syncSettingsPath))
            {
                var lines = File.ReadAllLines(_syncSettingsPath);
                if (lines.Length >= 2)
                {
                    bool.TryParse(lines[0], out _isAutoSyncEnabled);
                    AutoSyncToggle.IsChecked = _isAutoSyncEnabled;
                    TxtLastSync.Text = $"آخر مزامنة: {lines[1]}";
                }
            }
            UpdateNextSyncUI();
        }

        private void SaveSyncSettings(string lastSyncDateStr)
        {
            try
            {
                File.WriteAllLines(_syncSettingsPath, new[] { _isAutoSyncEnabled.ToString(), lastSyncDateStr });
            }
            catch { }
        }

        private void AutoSyncToggle_Checked(object sender, RoutedEventArgs e)
        {
            if (!IsLoaded) return;
            _isAutoSyncEnabled = true;
            UpdateNextSyncUI();
            SaveSyncSettings(TxtLastSync.Text.Replace("آخر مزامنة: ", ""));
        }

        private void AutoSyncToggle_Unchecked(object sender, RoutedEventArgs e)
        {
            if (!IsLoaded) return;
            _isAutoSyncEnabled = false;
            UpdateNextSyncUI();
            SaveSyncSettings(TxtLastSync.Text.Replace("آخر مزامنة: ", ""));
        }

        private void ApplyPendingRestore() => MyClinic.Services.GoogleDriveSyncService.ApplyPendingRestore();

        private void LoadThemeSettings()
        {
            bool isDark = true;
            if (File.Exists(_themeSettingsPath))
            {
                var content = File.ReadAllText(_themeSettingsPath).Trim();
                bool.TryParse(content, out isDark);
            }

            _isDarkTheme = isDark;
            SetTheme(isDark);
        }

        private void SaveThemeSettings(bool isDark)
        {
            try
            {
                File.WriteAllText(_themeSettingsPath, isDark.ToString());
            }
            catch { }
        }

        public bool IsDarkTheme => _isDarkTheme;

        public void SetDarkTheme(bool isDark)
        {
            _isDarkTheme = isDark;
            SetTheme(isDark);
            SaveThemeSettings(isDark);
        }

        public void SetInterfaceTheme(bool useOriginalTheme) =>
            InterfaceThemeSettings.SwitchTo(this, useOriginalTheme);

        private static SolidColorBrush CreateBrush(string hex) =>
            new((Color)ColorConverter.ConvertFromString(hex));

        private static LinearGradientBrush CreateGradient(string startHex, string endHex, Point startPoint, Point endPoint)
        {
            var brush = new LinearGradientBrush
            {
                StartPoint = startPoint,
                EndPoint = endPoint
            };
            brush.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString(startHex), 0.0));
            brush.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString(endHex), 1.0));
            return brush;
        }

        private void SetTheme(bool isDark)
        {
            var res = Application.Current.Resources;

            if (isDark)
            {
                // الألوان العامة للثيم الداكن
                res["AppWindowBg"] = CreateGradient("#061923", "#09131C", new Point(0, 0), new Point(1, 1));
                res["AppSidebarBg"] = CreateBrush("#14212B");
                res["AppSidebarHoverBg"] = CreateBrush("#1B2E3B");
                res["AppCardBg"] = CreateBrush("#121A24");
                res["AppInputBg"] = CreateBrush("#0B121B");
                res["AppBorder"] = CreateBrush("#382DD4BF");
                res["AppTextPrimary"] = CreateBrush("#F8FAFC");
                res["AppTextSecondary"] = CreateBrush("#94A3B8");

                // الشريط الجانبي (الوضع الداكن)
                res["AppSidebarOuterBg"] = Brushes.Transparent;
                res["AppSidebarOuterBorder"] = Brushes.Transparent;
                res["AppSidebarOuterBorderThickness"] = new Thickness(0);
                res["AppSidebarOuterPadding"] = new Thickness(12, 14, 0, 14);
                res["AppSidebarTopCardBg"] = CreateGradient("#182B36", "#121A24", new Point(0, 0), new Point(1, 1));
                res["AppSidebarTopCardBorder"] = CreateBrush("#552DD4BF");
                res["AppSidebarBottomCardBg"] = CreateGradient("#13232E", "#0F2E35", new Point(0, 0), new Point(1, 1));
                res["AppSidebarBottomCardBorder"] = CreateBrush("#452DD4BF");
                res["AppSidebarText"] = CreateBrush("#F8FAFC");
                res["AppSidebarSubText"] = CreateBrush("#94A3B8");
                res["AppSidebarActiveBg"] = CreateGradient("#14B8A6", "#0D746B", new Point(0, 0), new Point(1, 0));
                res["AppSidebarActiveHoverBg"] = CreateGradient("#0D9488", "#115E59", new Point(0, 0), new Point(1, 0));
                res["AppSidebarActiveBorder"] = CreateBrush("#75F3E8");
                res["AppSidebarActiveGlowOpacity"] = 0.45d;
                res["AppConnectionPillBg"] = CreateBrush("#153837");
                res["AppConnectionTextBrush"] = CreateBrush("#34D399");
                res["AppToggleActiveBg"] = CreateGradient("#14B8A6", "#0D9488", new Point(0, 0), new Point(1, 0));
                res["AppToggleActiveBorder"] = CreateBrush("#5EEAD4");

                // بطاقات تبويب الرئيسية (الوضع الداكن)
                var welcomeGradient = new LinearGradientBrush { StartPoint = new Point(1, 0), EndPoint = new Point(0, 1) };
                welcomeGradient.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString("#0D6C6B"), 0.0));
                welcomeGradient.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString("#0B8674"), 0.48));
                welcomeGradient.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString("#216382"), 1.0));
                res["DashWelcomeCardBg"] = welcomeGradient;
                res["DashWelcomeCardBorder"] = CreateBrush("#662DD4BF");
                res["DashWelcomeTitleFg"] = CreateBrush("#FFFFFF");
                res["DashWelcomeSubFg"] = CreateBrush("#E2F2F0");
                res["DashWelcomeToothBrush"] = CreateBrush("#1EFFFFFF");

                res["DashCanalCardBg"] = CreateBrush("#121721");
                res["DashCanalCardBorder"] = CreateBrush("#422DD4BF");
                res["DashCanalTitleFg"] = CreateBrush("#F8FAFC");
                res["DashCanalBoxBg"] = CreateBrush("#0F353D");
                res["DashCanalBoxBorder"] = CreateBrush("#2DD4BF");
                res["DashCanalBoxFg"] = CreateBrush("#5EEAD4");
                res["DashCanalBoxGlowOpacity"] = 0.55d;
                res["DashCanalResetBg"] = CreateBrush("#161B26");
                res["DashCanalResetBorder"] = CreateBrush("#2A3446");
                res["DashCanalResetFg"] = CreateBrush("#F8FAFC");

                res["DashActionsCardBg"] = CreateGradient("#1E383E", "#192630", new Point(0, 0), new Point(1, 1));
                res["DashActionsCardBorder"] = CreateBrush("#552DD4BF");

                res["DashMainCardBg"] = CreateBrush("#121A24");
                res["DashMainCardBorder"] = CreateBrush("#422DD4BF");
                res["DashFilterGroupBg"] = CreateBrush("#0D131D");
                res["DashFilterGroupBorder"] = CreateBrush("#283A4A");
                res["DashActiveFilterBg"] = CreateGradient("#14B8A6", "#0D9488", new Point(0, 0), new Point(1, 0));

                res["DashCalendarEmptyBg"] = CreateBrush("#121720");
                res["DashCalendarEmptyBorder"] = CreateBrush("#1A2230");
                res["DashCalendarDayBg"] = CreateBrush("#0A0E19");
                res["DashCalendarDayBorder"] = CreateBrush("#1E2638");
                res["DashCalendarTodayBg"] = CreateBrush("#103339");
                res["DashCalendarTodayBorder"] = CreateBrush("#14B8A6");
                res["DashCalendarTodayNumFg"] = CreateBrush("#2DD4BF");
                res["DashCalendarTodayBadgeBg"] = CreateBrush("#14B8A6");
                res["DashCalendarPillIndicator"] = CreateBrush("#242D40");

                // ثيم نموذج إضافة مريض وبطاقات الزيارات (الوضع الداكن)
                res["VisitBannerBg"] = welcomeGradient;
                res["VisitBannerBorder"] = CreateBrush("#662DD4BF");
                res["VisitBannerTitleFg"] = CreateBrush("#FFFFFF");
                res["VisitBannerSubFg"] = CreateBrush("#E2F2F0");
                res["VisitBannerToothBrush"] = CreateBrush("#1EFFFFFF");

                res["VisitCardBg"] = CreateGradient("#14212B", "#101821", new Point(0, 0), new Point(1, 1));
                res["VisitCardBorder"] = CreateBrush("#422DD4BF");
                res["VisitSubCardBg"] = CreateBrush("#0B121B");
                res["VisitSubCardBorder"] = CreateBrush("#243644");

                res["VisitAccentCardBg"] = CreateBrush("#102930");
                res["VisitAccentCardBorder"] = CreateBrush("#452DD4BF");
                res["VisitGlowBoxBg"] = CreateBrush("#0F353D");
                res["VisitGlowBoxBorder"] = CreateBrush("#2DD4BF");
                res["VisitGlowBoxFg"] = CreateBrush("#5EEAD4");
                res["VisitGlowOpacity"] = 0.52d;

                res["VisitPrimaryBtnBg"] = CreateGradient("#14B8A6", "#0D9488", new Point(0, 0), new Point(1, 0));
                res["VisitPrimaryBtnBorder"] = CreateBrush("#5EEAD4");
                res["VisitSecondaryBtnBg"] = CreateBrush("#202C39");
                res["VisitSecondaryBtnBorder"] = CreateBrush("#384B5E");
                res["VisitSecondaryBtnFg"] = CreateBrush("#FFFFFF");

                res["VisitHistoryHeaderBg"] = CreateGradient("#14464B", "#102B33", new Point(0, 0), new Point(1, 1));
                res["VisitHistoryHeaderFg"] = CreateBrush("#F8FAFC");
                res["VisitEntryCardBg"] = CreateBrush("#0A0E19");
                res["VisitEntryCardBorder"] = CreateBrush("#1E2638");
                res["VisitEntryPillIndicator"] = CreateBrush("#14B8A6");

                res["VisitModalOuterBg"] = CreateBrush("#0B131D");
                res["VisitModalOuterBorder"] = CreateBrush("#552DD4BF");
                res["VisitModalChipBg"] = CreateBrush("#12363B");
                res["VisitModalChipBorder"] = CreateBrush("#452DD4BF");

                res["VisitTreatmentPillBg"] = CreateBrush("#0D514F");
                res["VisitTreatmentPillBorder"] = CreateBrush("#14857E");
                res["VisitTreatmentPillFg"] = CreateBrush("#E6FFFA");

                res["VisitQtyPillBg"] = CreateBrush("#15203E");
                res["VisitQtyPillBorder"] = CreateBrush("#25386B");
                res["VisitQtyPillFg"] = CreateBrush("#E2E8F0");

                res["VisitCostPillBg"] = CreateBrush("#0F353D");
                res["VisitCostPillBorder"] = CreateBrush("#2DD4BF");
                res["VisitCostPillFg"] = CreateBrush("#5EEAD4");

                res["VisitToothBadgeBg"] = CreateBrush("#103339");
                res["VisitToothBadgeBorder"] = CreateBrush("#14B8A6");
                res["VisitToothBadgeFg"] = CreateBrush("#2DD4BF");

                res["VisitWarningCardBg"] = CreateBrush("#2A161D");
                res["VisitWarningCardBorder"] = CreateBrush("#BA6571");
                res["VisitWarningFg"] = CreateBrush("#FDA4AF");

                // ثيم الإعدادات والتبويبات الموحدة (الوضع الداكن)
                var settingsBannerDark = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 1) };
                settingsBannerDark.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString("#0D6C6B"), 0.0));
                settingsBannerDark.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString("#0E8775"), 0.5));
                settingsBannerDark.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString("#084F4B"), 1.0));
                res["SettingsBannerBg"] = settingsBannerDark;
                res["SettingsBannerBorder"] = CreateBrush("#752DD4BF");
                res["SettingsBannerGlowOpacity"] = 0.45d;
                res["SettingsBannerTitleFg"] = CreateBrush("#FFFFFF");
                res["SettingsBannerSubFg"] = CreateBrush("#D5F5F0");
                res["SettingsBannerPillBg"] = CreateBrush("#124844");
                res["SettingsBannerPillBorder"] = CreateBrush("#5EEAD4");
                res["SettingsBannerPillFg"] = CreateBrush("#F8FAFC");

                res["SettingsCardBg"] = CreateGradient("#14212B", "#101821", new Point(0, 0), new Point(1, 1));
                res["SettingsCardBorder"] = CreateBrush("#452DD4BF");
                res["SettingsInputBg"] = CreateBrush("#0B1219");
                res["SettingsInputBorder"] = CreateBrush("#2B424D");

                res["SettingsPrimaryBtnBg"] = CreateGradient("#14B8A6", "#0D8A7E", new Point(0, 0), new Point(1, 0));
                res["SettingsPrimaryBtnBorder"] = CreateBrush("#5EEAD4");
                res["SettingsPrimaryBtnFg"] = CreateBrush("#FFFFFF");

                res["SettingsGreenBtnBg"] = CreateGradient("#22A058", "#168A48", new Point(0, 0), new Point(1, 0));
                res["SettingsGreenBtnBorder"] = CreateBrush("#4ADE80");
                res["SettingsGreenBtnFg"] = CreateBrush("#FFFFFF");

                res["SettingsOutlineBtnBg"] = CreateBrush("#202C39");
                res["SettingsOutlineBtnBorder"] = CreateBrush("#384B5E");
                res["SettingsOutlineBtnFg"] = CreateBrush("#F1F5F9");

                res["SettingsToggleTrackOffBg"] = CreateBrush("#334155");
                res["SettingsToggleTrackOnBg"] = CreateGradient("#14B8A6", "#0D9488", new Point(0, 0), new Point(1, 0));
                res["SettingsToggleTrackOnBorder"] = CreateBrush("#5EEAD4");

                res["SettingsRateBoxBg"] = CreateGradient("#133D42", "#0F2E34", new Point(0, 0), new Point(1, 1));
                res["SettingsRateBoxBorder"] = CreateBrush("#2DD4BF");
                res["SettingsRateBoxLabelFg"] = CreateBrush("#E2F2F0");
                res["SettingsRateBoxValueFg"] = CreateBrush("#5EEAD4");
                res["SettingsCurrencyUnitBg"] = CreateBrush("#1C2936");
                res["SettingsCurrencyUnitBorder"] = CreateBrush("#2C3E50");
                res["SettingsCurrencyUnitFg"] = CreateBrush("#CBD5E1");
                res["SettingsTipBoxBg"] = CreateBrush("#0D151E");
                res["SettingsTipBoxBorder"] = CreateBrush("#1E2F3D");

                res["SettingsCalloutBg"] = CreateGradient("#10383E", "#0D2B34", new Point(0, 0), new Point(1, 0));
                res["SettingsCalloutBorder"] = CreateBrush("#2DD4BF");
                res["SettingsCalloutFg"] = CreateBrush("#F1F5F9");

                res["SettingsTableContainerBg"] = CreateBrush("#0D141C");
                res["SettingsTableContainerBorder"] = CreateBrush("#22353E");
                res["SettingsTableRowBg"] = CreateBrush("#121B24");
                res["SettingsTableRowAltBg"] = CreateBrush("#0E161E");
                res["SettingsTableRowBorder"] = CreateBrush("#1E2E3C");
                res["SettingsEditPriceBtnBg"] = CreateBrush("#18222E");
                res["SettingsEditPriceBtnBorder"] = CreateBrush("#3B4D61");
                res["SettingsEditPriceBtnFg"] = CreateBrush("#E2E8F0");
                res["SettingsDeleteBtnBg"] = CreateBrush("#2C161D");
                res["SettingsDeleteBtnBorder"] = CreateBrush("#883B47");
                res["SettingsDeleteBtnFg"] = CreateBrush("#F87171");

                // بطاقات سجل المرضى (الوضع الداكن)
                res["PatientCardBg"] = CreateGradient("#14242E", "#101923", new Point(0, 0), new Point(1, 1));
                res["PatientCardBorder"] = CreateBrush("#452DD4BF");
                res["PatientAvatarBg"] = CreateBrush("#0F353D");
                res["PatientAvatarBorder"] = CreateBrush("#2DD4BF");
                res["PatientAvatarFg"] = CreateBrush("#5EEAD4");
                res["PatientChipBg"] = CreateBrush("#0D262D");
                res["PatientChipBorder"] = CreateBrush("#214E56");
                res["PatientChipLabelFg"] = CreateBrush("#94A3B8");
                res["PatientChipValueFg"] = CreateBrush("#F8FAFC");
            }
            else
            {
                // الألوان العامة للثيم الفاتح
                res["AppWindowBg"] = CreateBrush("#F6F5F1");
                res["AppSidebarBg"] = CreateBrush("#F7F9F8");
                res["AppSidebarHoverBg"] = CreateBrush("#EBF0F5");
                res["AppCardBg"] = CreateBrush("#FFFFFF");
                res["AppInputBg"] = CreateBrush("#F8FAFC");
                res["AppBorder"] = CreateBrush("#E2E8F0");
                res["AppTextPrimary"] = CreateBrush("#13284A");
                res["AppTextSecondary"] = CreateBrush("#64748B");

                // الشريط الجانبي (الوضع الفاتح)
                res["AppSidebarOuterBg"] = CreateBrush("#F7F9F8");
                res["AppSidebarOuterBorder"] = CreateBrush("#E2E8EE");
                res["AppSidebarOuterBorderThickness"] = new Thickness(0, 0, 1, 0);
                res["AppSidebarOuterPadding"] = new Thickness(12, 14, 12, 14);
                res["AppSidebarTopCardBg"] = Brushes.Transparent;
                res["AppSidebarTopCardBorder"] = Brushes.Transparent;
                res["AppSidebarBottomCardBg"] = CreateBrush("#E3E8ED");
                res["AppSidebarBottomCardBorder"] = CreateBrush("#D6E0E8");
                res["AppSidebarText"] = CreateBrush("#1E293B");
                res["AppSidebarSubText"] = CreateBrush("#475569");
                res["AppSidebarActiveBg"] = CreateBrush("#3348BD");
                res["AppSidebarActiveHoverBg"] = CreateBrush("#283BA6");
                res["AppSidebarActiveBorder"] = Brushes.Transparent;
                res["AppSidebarActiveGlowOpacity"] = 0.0d;
                res["AppConnectionPillBg"] = Brushes.Transparent;
                res["AppConnectionTextBrush"] = CreateBrush("#10B981");
                res["AppToggleActiveBg"] = CreateBrush("#3348BD");
                res["AppToggleActiveBorder"] = Brushes.Transparent;

                // بطاقات تبويب الرئيسية (الوضع الفاتح)
                res["DashWelcomeCardBg"] = CreateBrush("#E3EFED");
                res["DashWelcomeCardBorder"] = CreateBrush("#D5E6E3");
                res["DashWelcomeTitleFg"] = CreateBrush("#13284A");
                res["DashWelcomeSubFg"] = CreateBrush("#2C3E50");
                res["DashWelcomeToothBrush"] = CreateBrush("#CFE3DF");

                res["DashCanalCardBg"] = CreateBrush("#C3DCD8");
                res["DashCanalCardBorder"] = CreateBrush("#B4D0CB");
                res["DashCanalTitleFg"] = CreateBrush("#13284A");
                res["DashCanalBoxBg"] = CreateBrush("#FFFFFF");
                res["DashCanalBoxBorder"] = CreateBrush("#DCEAE7");
                res["DashCanalBoxFg"] = CreateBrush("#13284A");
                res["DashCanalBoxGlowOpacity"] = 0.0d;
                res["DashCanalResetBg"] = CreateBrush("#ECF4F3");
                res["DashCanalResetBorder"] = CreateBrush("#D5E5E2");
                res["DashCanalResetFg"] = CreateBrush("#1E293B");

                res["DashActionsCardBg"] = CreateBrush("#FFFFFF");
                res["DashActionsCardBorder"] = CreateBrush("#ECEFEA");

                res["DashMainCardBg"] = CreateBrush("#FFFFFF");
                res["DashMainCardBorder"] = CreateBrush("#EAECEF");
                res["DashFilterGroupBg"] = CreateBrush("#F8F9FB");
                res["DashFilterGroupBorder"] = CreateBrush("#E2E8F0");
                res["DashActiveFilterBg"] = CreateBrush("#3547B7");

                res["DashCalendarEmptyBg"] = CreateBrush("#E3E8EC");
                res["DashCalendarEmptyBorder"] = CreateBrush("#DCE2E8");
                res["DashCalendarDayBg"] = CreateBrush("#FFFFFF");
                res["DashCalendarDayBorder"] = CreateBrush("#CBD5E1");
                res["DashCalendarTodayBg"] = CreateBrush("#E3EFED");
                res["DashCalendarTodayBorder"] = CreateBrush("#7FA6A0");
                res["DashCalendarTodayNumFg"] = CreateBrush("#25635A");
                res["DashCalendarTodayBadgeBg"] = CreateBrush("#6C9198");
                res["DashCalendarPillIndicator"] = CreateBrush("#CBD5E1");

                // ثيم نموذج إضافة مريض وبطاقات الزيارات (الوضع الفاتح)
                res["VisitBannerBg"] = CreateBrush("#E3EFED");
                res["VisitBannerBorder"] = CreateBrush("#D5E6E3");
                res["VisitBannerTitleFg"] = CreateBrush("#13284A");
                res["VisitBannerSubFg"] = CreateBrush("#2C3E50");
                res["VisitBannerToothBrush"] = CreateBrush("#CFE3DF");

                res["VisitCardBg"] = CreateBrush("#FFFFFF");
                res["VisitCardBorder"] = CreateBrush("#E4ECEA");
                res["VisitSubCardBg"] = CreateBrush("#F8FAFC");
                res["VisitSubCardBorder"] = CreateBrush("#DCE5E7");

                res["VisitAccentCardBg"] = CreateBrush("#E3EFED");
                res["VisitAccentCardBorder"] = CreateBrush("#B4D0CB");
                res["VisitGlowBoxBg"] = CreateBrush("#FFFFFF");
                res["VisitGlowBoxBorder"] = CreateBrush("#B4D0CB");
                res["VisitGlowBoxFg"] = CreateBrush("#13284A");
                res["VisitGlowOpacity"] = 0.0d;

                res["VisitPrimaryBtnBg"] = CreateBrush("#2250DF");
                res["VisitPrimaryBtnBorder"] = Brushes.Transparent;
                res["VisitSecondaryBtnBg"] = CreateBrush("#F8FAFC");
                res["VisitSecondaryBtnBorder"] = CreateBrush("#CBD5E1");
                res["VisitSecondaryBtnFg"] = CreateBrush("#1E293B");

                res["VisitHistoryHeaderBg"] = CreateBrush("#C3DCD8");
                res["VisitHistoryHeaderFg"] = CreateBrush("#13284A");
                res["VisitEntryCardBg"] = CreateBrush("#FFFFFF");
                res["VisitEntryCardBorder"] = CreateBrush("#CBD5E1");
                res["VisitEntryPillIndicator"] = CreateBrush("#3348BD");

                res["VisitModalOuterBg"] = CreateBrush("#F6F5F1");
                res["VisitModalOuterBorder"] = CreateBrush("#D5E6E3");
                res["VisitModalChipBg"] = CreateBrush("#FFFFFF");
                res["VisitModalChipBorder"] = CreateBrush("#C3DCD8");

                res["VisitTreatmentPillBg"] = CreateBrush("#B2DCCC");
                res["VisitTreatmentPillBorder"] = CreateBrush("#96CEB8");
                res["VisitTreatmentPillFg"] = CreateBrush("#0F291E");

                res["VisitQtyPillBg"] = CreateBrush("#CAD4F0");
                res["VisitQtyPillBorder"] = CreateBrush("#B2C0E8");
                res["VisitQtyPillFg"] = CreateBrush("#13284A");

                res["VisitCostPillBg"] = CreateBrush("#E3EFED");
                res["VisitCostPillBorder"] = CreateBrush("#7FA6A0");
                res["VisitCostPillFg"] = CreateBrush("#25635A");

                res["VisitToothBadgeBg"] = CreateBrush("#E3EFED");
                res["VisitToothBadgeBorder"] = CreateBrush("#7FA6A0");
                res["VisitToothBadgeFg"] = CreateBrush("#13284A");

                res["VisitWarningCardBg"] = CreateBrush("#FFF1F0");
                res["VisitWarningCardBorder"] = CreateBrush("#FB7A6D");
                res["VisitWarningFg"] = CreateBrush("#9F1239");

                // ثيم الإعدادات والتبويبات الموحدة (الوضع الفاتح)
                res["SettingsBannerBg"] = CreateGradient("#C7DFDC", "#D7EAE7", new Point(0, 0), new Point(1, 0));
                res["SettingsBannerBorder"] = CreateBrush("#B5D3CE");
                res["SettingsBannerGlowOpacity"] = 0.0d;
                res["SettingsBannerTitleFg"] = CreateBrush("#0F1E36");
                res["SettingsBannerSubFg"] = CreateBrush("#2C3E50");
                res["SettingsBannerPillBg"] = CreateBrush("#D9EBE8");
                res["SettingsBannerPillBorder"] = CreateBrush("#98B9B4");
                res["SettingsBannerPillFg"] = CreateBrush("#1E3A3A");

                res["SettingsCardBg"] = CreateBrush("#FFFFFF");
                res["SettingsCardBorder"] = CreateBrush("#E2E8F0");
                res["SettingsInputBg"] = CreateBrush("#F8FAFC");
                res["SettingsInputBorder"] = CreateBrush("#CBD5E1");

                res["SettingsPrimaryBtnBg"] = CreateBrush("#2250DF");
                res["SettingsPrimaryBtnBorder"] = Brushes.Transparent;
                res["SettingsPrimaryBtnFg"] = CreateBrush("#FFFFFF");

                res["SettingsGreenBtnBg"] = CreateBrush("#28884B");
                res["SettingsGreenBtnBorder"] = Brushes.Transparent;
                res["SettingsGreenBtnFg"] = CreateBrush("#FFFFFF");

                res["SettingsOutlineBtnBg"] = CreateBrush("#F8FAFC");
                res["SettingsOutlineBtnBorder"] = CreateBrush("#CBD5E1");
                res["SettingsOutlineBtnFg"] = CreateBrush("#1E293B");

                res["SettingsToggleTrackOffBg"] = CreateBrush("#CBD5E1");
                res["SettingsToggleTrackOnBg"] = CreateBrush("#2250DF");
                res["SettingsToggleTrackOnBorder"] = Brushes.Transparent;

                res["SettingsRateBoxBg"] = CreateGradient("#649699", "#5B8C8F", new Point(0, 0), new Point(1, 1));
                res["SettingsRateBoxBorder"] = CreateBrush("#528285");
                res["SettingsRateBoxLabelFg"] = CreateBrush("#ECF7F6");
                res["SettingsRateBoxValueFg"] = CreateBrush("#FFFFFF");
                res["SettingsCurrencyUnitBg"] = CreateBrush("#E5ECEE");
                res["SettingsCurrencyUnitBorder"] = CreateBrush("#CBD5E1");
                res["SettingsCurrencyUnitFg"] = CreateBrush("#334155");
                res["SettingsTipBoxBg"] = CreateBrush("#F1F5F4");
                res["SettingsTipBoxBorder"] = CreateBrush("#DCE6E4");

                res["SettingsCalloutBg"] = CreateGradient("#288855", "#2255DF", new Point(0, 0), new Point(1, 0));
                res["SettingsCalloutBorder"] = CreateBrush("#2563EB");
                res["SettingsCalloutFg"] = CreateBrush("#FFFFFF");

                res["SettingsTableContainerBg"] = CreateBrush("#FFFFFF");
                res["SettingsTableContainerBorder"] = CreateBrush("#E2E8F0");
                res["SettingsTableRowBg"] = CreateBrush("#FFFFFF");
                res["SettingsTableRowAltBg"] = CreateBrush("#F8FAFC");
                res["SettingsTableRowBorder"] = CreateBrush("#EDF2F7");
                res["SettingsEditPriceBtnBg"] = CreateBrush("#FFFFFF");
                res["SettingsEditPriceBtnBorder"] = CreateBrush("#CBD5E1");
                res["SettingsEditPriceBtnFg"] = CreateBrush("#1E293B");
                res["SettingsDeleteBtnBg"] = CreateBrush("#FEE2E2");
                res["SettingsDeleteBtnBorder"] = CreateBrush("#FCA5A5");
                res["SettingsDeleteBtnFg"] = CreateBrush("#DC2626");

                // بطاقات سجل المرضى (الوضع الفاتح)
                res["PatientCardBg"] = CreateBrush("#FFFFFF");
                res["PatientCardBorder"] = CreateBrush("#D5E6E3");
                res["PatientAvatarBg"] = CreateBrush("#E3EFED");
                res["PatientAvatarBorder"] = CreateBrush("#B4D0CB");
                res["PatientAvatarFg"] = CreateBrush("#25635A");
                res["PatientChipBg"] = CreateBrush("#F1F7F6");
                res["PatientChipBorder"] = CreateBrush("#D5E6E3");
                res["PatientChipLabelFg"] = CreateBrush("#64748B");
                res["PatientChipValueFg"] = CreateBrush("#13284A");
            }

            if (WindowSpotlightCanvas is not null)
            {
                WindowSpotlightCanvas.Visibility = isDark ? Visibility.Visible : Visibility.Collapsed;
            }

            if (_activeNavButton is not null)
            {
                SetActiveNavigation(_activeNavButton);
            }

            GlobalEvents.NotifyThemeChanged(isDark);
        }

        private void BtnDashboard_Click(object sender, RoutedEventArgs e) => ShowDashboard();
        private void BtnPatients_Click(object sender, RoutedEventArgs e) => ShowPatientRecords();
        private void BtnFinancials_Click(object sender, RoutedEventArgs e) => ShowFinancialRecords();
        private void BtnShortages_Click(object sender, RoutedEventArgs e) => ShowShortages();
        private void BtnLab_Click(object sender, RoutedEventArgs e) => ShowLab();
        private void BtnSettings_Click(object sender, RoutedEventArgs e) => ShowSettings();
        private void BtnStatistics_Click(object sender, RoutedEventArgs e) => ShowStatistics();

        private async void BtnSyncDrive_Click(object sender, RoutedEventArgs e)
        {
            BtnSyncDrive.IsEnabled = false;
            if (TxtSyncDriveLabel is not null)
                TxtSyncDriveLabel.Text = "جاري المزامنة...";

            var result = await GoogleDriveSyncService.BackupDatabaseAsync();

            if (result.Success)
            {
                MessageBox.Show("تم عمل نسخة احتياطية من البيانات على Google Drive بنجاح!", "نجاح المزامنة", MessageBoxButton.OK, MessageBoxImage.Information);
                DateTime appTime = AppClock.Now;
                UpdateLastSyncUI(appTime);
            }
            else
            {
                MessageBox.Show($"فشلت عملية المزامنة. السبب:\n{result.ErrorMessage}", "خطأ في المزامنة", MessageBoxButton.OK, MessageBoxImage.Error);
            }

            if (TxtSyncDriveLabel is not null)
                TxtSyncDriveLabel.Text = "مزامنة مع Google Drive";
            BtnSyncDrive.IsEnabled = true;
        }

        public async Task RestoreDatabaseFromDriveAsync()
        {
            MessageBoxResult confirmResult = MessageBox.Show(
                "تحذير: استعادة البيانات ستؤدي إلى حذف البيانات الحالية واستبدالها بالنسخة الموجودة على Google Drive. هل أنت متأكد أنك تريد المتابعة؟",
                "تأكيد الاستعادة",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning,
                MessageBoxResult.No,
                MessageBoxOptions.RightAlign);

            if (confirmResult == MessageBoxResult.Yes)
            {
                var result = await GoogleDriveSyncService.RestoreDatabaseAsync();

                if (result.Success)
                {
                    MessageBox.Show("تم تحميل النسخة الاحتياطية بنجاح! سيتم الآن إعادة تشغيل البرنامج لتطبيق البيانات الجديدة.", "نجاح الاستعادة", MessageBoxButton.OK, MessageBoxImage.Information);

                    var processPath = Environment.ProcessPath;
                    if (processPath != null)
                    {
                        System.Diagnostics.Process.Start(processPath);
                    }
                    Application.Current.Shutdown();
                }
                else
                {
                    MessageBox.Show($"فشلت عملية الاستعادة. السبب:\n{result.ErrorMessage}", "خطأ في الاستعادة", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        public void ShowDashboard()
        {
            _dashboardView ??= new DashboardView();
            _dashboardView.RefreshWelcomeName();
            if (!NavigationTransition.Show(MainContent, _dashboardView)) return;
            _dashboardView.RequestRefresh();
            _ = _dashboardView.RefreshNowAsync();
            SetActiveNavigation(BtnDashboard);
        }

        public void RefreshWelcomeName() => _dashboardView?.RefreshWelcomeName();

        public async Task NotifyAppTimeChangedAsync(DateTime previousAppTime)
        {
            _dashboardView?.RefreshDatesForAppTimeChange(previousAppTime);
            _financialRecordsView?.RefreshDateForAppTimeChange(previousAppTime);
            _labView?.RefreshDateForAppTimeChange(previousAppTime);
            if (_statisticsView != null)
                await _statisticsView.RefreshForAppTimeChangeAsync(previousAppTime);
        }

        public void ShowAddPatient()
        {
            if (!NavigationTransition.Show(MainContent, new AddPatientView())) return;
            SetActiveNavigation(BtnDashboard);
        }

        public void ShowPatientRecords()
        {
            _patientRecordsView ??= new PatientRecordsView();
            if (!NavigationTransition.Show(MainContent, _patientRecordsView)) return;
            _patientRecordsView.RequestRefresh();
            _ = _patientRecordsView.EnsureDataCurrentAsync();
            SetActiveNavigation(BtnPatients);
        }

        public async void OpenExpense(int id)
        {
            ShowFinancialRecords();
            if (_financialRecordsView != null) await _financialRecordsView.FocusExpenseAsync(id);
        }

        public void ShowFinancialRecords()
        {
            _financialRecordsView ??= new FinancialRecordsView();
            if (!NavigationTransition.Show(MainContent, _financialRecordsView)) return;
            _financialRecordsView.RequestRefresh();
            _ = _financialRecordsView.EnsureDataCurrentAsync();
            SetActiveNavigation(BtnFinancials);
        }

        public void ShowShortages()
        {
            _shortagesView ??= new ShortagesView();
            if (!NavigationTransition.Show(MainContent, _shortagesView)) return;
            SetActiveNavigation(BtnShortages);
        }

        public void ShowLab()
        {
            _labView ??= new LabView();
            if (!NavigationTransition.Show(MainContent, _labView)) return;
            SetActiveNavigation(BtnLab);
        }

        public void ShowSettings()
        {
            _settingsView ??= new SettingsView();
            if (!NavigationTransition.Show(MainContent, _settingsView)) return;
            SetActiveNavigation(BtnSettings);
        }

        public void ShowStatistics()
        {
            _statisticsView ??= new StatisticsView();
            if (!NavigationTransition.Show(MainContent, _statisticsView)) return;
            SetActiveNavigation(BtnStatistics);
        }

        public void InvalidatePatientRecords() => _patientRecordsView?.RequestRefresh();
        public void InvalidateFinancialRecords() => _financialRecordsView?.RequestRefresh();

        public void AddSavedVisitToDashboard(string patientName, string phoneNumber, string reason, DateTime visitDateTime, int visitId, int patientId)
        {
            _dashboardView ??= new DashboardView();
            _dashboardView.AddSavedVisitImmediately(patientName, phoneNumber, reason, visitDateTime, visitId, patientId);
        }

        private void SetActiveNavigation(Button activeButton)
        {
            _activeNavButton = activeButton;

            BtnDashboard.Style = (Style)FindResource("SidebarButton");
            BtnPatients.Style = (Style)FindResource("SidebarButton");
            BtnFinancials.Style = (Style)FindResource("SidebarButton");
            BtnShortages.Style = (Style)FindResource("SidebarButton");
            BtnLab.Style = (Style)FindResource("SidebarButton");
            BtnSettings.Style = (Style)FindResource("SidebarButton");
            BtnStatistics.Style = (Style)FindResource("SidebarButton");

            activeButton.Style = (Style)FindResource("SidebarActiveButton");

            UpdateSidebarIconVisuals(BtnDashboard, BadgeDashboard, IconDashboard, "#D8E2F8", "#3348BD");
            UpdateSidebarIconVisuals(BtnPatients, BadgePatients, IconPatients, "#D8DBF3", "#3C4167");
            UpdateSidebarIconVisuals(BtnFinancials, BadgeFinancials, IconFinancials, "#CCE2DD", "#406762");
            UpdateSidebarIconVisuals(BtnShortages, BadgeShortages, IconShortages, "#F9E0DF", "#976059");
            UpdateSidebarIconVisuals(BtnLab, BadgeLab, IconLab, "#DCF3F6", "#4D8383");
            UpdateSidebarIconVisuals(BtnStatistics, BadgeStatistics, IconStatistics, "#D7DDF0", "#41456B");
            UpdateSidebarIconVisuals(BtnSettings, BadgeSettings, IconSettings, "#E6E7E5", "#515558");
        }

        private void UpdateSidebarIconVisuals(Button button, Border? badge, System.Windows.Shapes.Path? icon, string lightBadgeHex, string lightIconHex)
        {
            if (badge is null || icon is null)
                return;

            bool isActive = ReferenceEquals(button, _activeNavButton);
            if (isActive)
            {
                badge.Background = _isDarkTheme ? Brushes.Transparent : CreateBrush("#2EFFFFFF");
                icon.Stroke = Brushes.White;
            }
            else if (_isDarkTheme)
            {
                badge.Background = Brushes.Transparent;
                icon.Stroke = CreateBrush("#2DD4BF");
            }
            else
            {
                badge.Background = CreateBrush(lightBadgeHex);
                icon.Stroke = CreateBrush(lightIconHex);
            }
        }
    }
}
