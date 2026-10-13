using Microsoft.EntityFrameworkCore;
using MyClinic.Models;
using System;
using System.ComponentModel;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace MyClinic
{
    public partial class SettingsView : UserControl
    {
        private readonly ObservableCollection<TreatmentCost> _treatmentCosts = new();
        private int? _editingTreatmentId;

        public SettingsView()
        {
            InitializeComponent();
            TreatmentItemsControl.ItemsSource = _treatmentCosts;
            Loaded += SettingsView_Loaded;
            
            // Set default currency selection
            CmbTreatmentCurrency.SelectedIndex = 0;
        }

        private async void SettingsView_Loaded(object sender, RoutedEventArgs e)
        {
            LoadExchangeRate();
            TxtUsername.Text = LoginSessionStore.CurrentUsername;
            SetAppClockFields(AppClock.Now);
            if (Window.GetWindow(this) is MainWindow mainWindow)
                ThemeToggle.IsChecked = mainWindow.IsDarkTheme;
            await LoadTreatmentsAsync();
        }

        private void SetAppClockFields(DateTime appTime)
        {
            DpAppDate.SelectedDate = appTime.Date;
            TxtAppClockTime.Text = appTime.ToString("HH:mm", CultureInfo.InvariantCulture);
        }

        private async void BtnSaveAppTime_Click(object sender, RoutedEventArgs e)
        {
            if (DpAppDate.SelectedDate is not DateTime selectedDate ||
                !TimeSpan.TryParseExact(TxtAppClockTime.Text.Trim(), @"hh\:mm", CultureInfo.InvariantCulture, out TimeSpan selectedTime) ||
                selectedTime < TimeSpan.Zero ||
                selectedTime >= TimeSpan.FromDays(1))
            {
                MessageBox.Show("يرجى إدخال تاريخ ووقت صحيحين بصيغة ساعة:دقيقة، مثل 14:30.", "وقت غير صالح", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                DateTime previousAppTime = AppClock.Now;
                DateTime selectedAppTime = selectedDate.Date.Add(selectedTime);
                AppClock.SetCurrentTime(selectedAppTime);
                SetAppClockFields(AppClock.Now);
                if (Window.GetWindow(this) is MainWindow mainWindow)
                    await mainWindow.NotifyAppTimeChangedAsync(previousAppTime);
                MessageBox.Show("تم حفظ وقت التطبيق وتطبيقه على أقسام البرنامج.", "تم الحفظ", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"تعذر حفظ وقت التطبيق: {ex.Message}", "خطأ", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void BtnResetAppTime_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                DateTime previousAppTime = AppClock.Now;
                AppClock.ResetToComputerTime();
                SetAppClockFields(AppClock.Now);
                if (Window.GetWindow(this) is MainWindow mainWindow)
                    await mainWindow.NotifyAppTimeChangedAsync(previousAppTime);
                MessageBox.Show("تمت إعادة التطبيق إلى وقته الافتراضي.", "تمت إعادة الضبط", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"تعذر إعادة ضبط وقت التطبيق: {ex.Message}", "خطأ", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnSaveUsername_Click(object sender, RoutedEventArgs e)
        {
            string newUsername = TxtUsername.Text.Trim();
            string currentUsername = LoginSessionStore.CurrentUsername;

            if (string.IsNullOrWhiteSpace(newUsername))
            {
                MessageBox.Show("يرجى إدخال اسم مستخدم صحيح.", "اسم غير صالح", MessageBoxButton.OK, MessageBoxImage.Warning);
                TxtUsername.Focus();
                return;
            }

            if (string.IsNullOrWhiteSpace(currentUsername))
            {
                MessageBox.Show("تعذر تحديد المستخدم الحالي. يرجى تسجيل الدخول مجدداً.", "تعذر تغيير الاسم", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                using var db = new AppDbContext();
                var user = db.Users.FirstOrDefault(u => u.Username == currentUsername);
                if (user == null)
                {
                    MessageBox.Show("لم يتم العثور على المستخدم الحالي في قاعدة البيانات.", "تعذر تغيير الاسم", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                if (db.Users.Any(u => u.Id != user.Id && u.Username == newUsername))
                {
                    MessageBox.Show("اسم المستخدم مستخدم مسبقاً. يرجى اختيار اسم آخر.", "اسم مستخدم موجود", MessageBoxButton.OK, MessageBoxImage.Warning);
                    TxtUsername.Focus();
                    TxtUsername.SelectAll();
                    return;
                }

                user.Username = newUsername;
                db.SaveChanges();

                try
                {
                    LoginSessionStore.UpdateUsername(newUsername);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(
                        $"تم تغيير اسم المستخدم في قاعدة البيانات، لكن تعذر تحديث الجلسة الحالية: {ex.Message}\nأعد تسجيل الدخول لتطبيق الاسم الجديد.",
                        "تم الحفظ مع تنبيه",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    return;
                }

                if (Window.GetWindow(this) is MainWindow mainWindow)
                    mainWindow.RefreshWelcomeName();

                MessageBox.Show("تم تغيير اسم المستخدم بنجاح.", "تم الحفظ", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"خطأ في تغيير اسم المستخدم: {ex.Message}", "خطأ", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void BtnCheckForUpdates_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mainWindow)
                await mainWindow.CheckForUpdatesFromSettingsAsync(BtnCheckForUpdates);
        }

        private void BtnWhatsNew_Click(object sender, RoutedEventArgs e)
        {
            const string updates =
                "الإصدار 1.7.0\n\n" +
                "التحديثات الجديدة:\n" +
                "1- تم تحسين التقارير الشهرية لتظهر تفاصيل أدق وتعطي إحصائيات احترافية.\n\n" +
                "2- تم إصلاح خلل ظهور عدد الأقنية في المعالجات التي لا تتضمن سحب عصب.\n\n" +
                "3- تم تحسين المواعيد لتظهر حالة الزيارة.\n\n" +
                "4- تم إضافة ضبط الوقت في الإعدادات لمراعاة اختلاف الوقت في الأجهزة.\n\n" +
                "5- تم إضافة خيار تغيير اسم المستخدم في الإعدادات.\n\n" +
                "6- تم إضافة إمكانية تعديل تفاصيل الزيارة وإمكانية إضافة الصور لاحقاً.";

            MessageBox.Show(updates, "لمحة عن التحديث الجديد", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void BtnContactDeveloper_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "https://wa.me/963935606912",
                    UseShellExecute = true
                });
            }
            catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
            {
                MessageBox.Show("تعذر فتح واتساب. يمكنك التواصل مع المطور على الرقم +963935606912.", "تعذر فتح واتساب", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void ThemeToggle_Checked(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mainWindow)
                mainWindow.SetDarkTheme(true);
        }

        private void ThemeToggle_Unchecked(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mainWindow)
                mainWindow.SetDarkTheme(false);
        }

        private async void BtnRestoreDrive_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mainWindow)
                await mainWindow.RestoreDatabaseFromDriveAsync();
        }

        private void LoadExchangeRate()
        {
            try
            {
                using var db = new AppDbContext();
                var settings = db.AppSettings.FirstOrDefault();
                if (settings == null)
                {
                    settings = new AppSettings();
                    db.AppSettings.Add(settings);
                    db.SaveChanges();
                }

                TxtExchangeRate.Text = settings.UsdToSypRate.ToString("N0", CultureInfo.CurrentCulture);
                TxtCurrentExchangeRate.Text = $"{settings.UsdToSypRate:N0} ل.س";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"خطأ في تحميل سعر الصرف: {ex.Message}", "خطأ", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnSaveExchangeRate_Click(object sender, RoutedEventArgs e)
        {
            if (!decimal.TryParse(TxtExchangeRate.Text, NumberStyles.Any, CultureInfo.CurrentCulture, out var rate) || rate <= 0)
            {
                MessageBox.Show("يرجى إدخال سعر صرف صحيح.", "قيمة غير صحيحة", MessageBoxButton.OK, MessageBoxImage.Warning);
                TxtExchangeRate.Focus();
                TxtExchangeRate.SelectAll();
                return;
            }

            try
            {
                using var db = new AppDbContext();
                var settings = db.AppSettings.FirstOrDefault();
                if (settings == null)
                {
                    settings = new AppSettings { UsdToSypRate = rate };
                    db.AppSettings.Add(settings);
                }
                else
                {
                    settings.UsdToSypRate = rate;
                    settings.UpdatedAt = AppClock.Now;
                }

                db.SaveChanges();
                TxtExchangeRate.Text = rate.ToString("N0", CultureInfo.CurrentCulture);
                TxtCurrentExchangeRate.Text = $"{rate:N0} ل.س";
                GlobalEvents.NotifyExchangeRateChanged();
                MessageBox.Show("تم حفظ سعر الصرف وتحديث التبويبات المرتبطة.", "تم الحفظ", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"خطأ في حفظ سعر الصرف: {ex.Message}", "خطأ", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async Task LoadTreatmentsAsync()
        {
            try
            {
                using var db = new AppDbContext();
                var treatments = await db.TreatmentCosts
                    .OrderBy(t => t.TreatmentName)
                    .ToListAsync();

                _treatmentCosts.Clear();
                foreach (var treatment in treatments)
                {
                    _treatmentCosts.Add(treatment);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"خطأ في تحميل العلاجات: {ex.Message}", "خطأ", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void BtnAddTreatment_Click(object sender, RoutedEventArgs e)
        {
            string treatmentName = TxtTreatmentName.Text.Trim();
            string costText = TxtTreatmentCost.Text.Trim();
            string currency = CmbTreatmentCurrency.SelectedItem is ComboBoxItem item ? item.Content?.ToString() ?? "SYP" : "SYP";

            if (string.IsNullOrWhiteSpace(treatmentName))
            {
                MessageBox.Show("يرجى إدخال اسم العلاج.", "بيانات ناقصة", MessageBoxButton.OK, MessageBoxImage.Warning);
                TxtTreatmentName.Focus();
                return;
            }

            if (!decimal.TryParse(costText, out decimal cost) || cost <= 0)
            {
                MessageBox.Show("يرجى إدخال تكلفة صحيحة.", "قيمة غير صحيحة", MessageBoxButton.OK, MessageBoxImage.Warning);
                TxtTreatmentCost.Focus();
                TxtTreatmentCost.SelectAll();
                return;
            }

            try
            {
                using var db = new AppDbContext();
                if (_editingTreatmentId is int editingId)
                {
                    var treatment = await db.TreatmentCosts.FindAsync(editingId);
                    if (treatment == null) return;

                    treatment.Cost = cost;
                    treatment.Currency = currency;
                    await db.SaveChangesAsync();
                    await LoadTreatmentsAsync();
                    ResetTreatmentEditor();
                    return;
                }

                var newTreatment = new TreatmentCost
                {
                    TreatmentName = treatmentName,
                    Cost = cost,
                    Currency = currency
                };

                db.TreatmentCosts.Add(newTreatment);
                await db.SaveChangesAsync();

                _treatmentCosts.Add(newTreatment);

                TxtTreatmentName.Clear();
                TxtTreatmentCost.Clear();
                TxtTreatmentName.Focus();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"خطأ في إضافة العلاج: {ex.Message}", "خطأ", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnEditTreatment_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button button || button.Tag is not int treatmentId)
                return;

            var treatment = _treatmentCosts.FirstOrDefault(t => t.Id == treatmentId);
            if (treatment == null) return;

            _editingTreatmentId = treatmentId;
            TxtTreatmentName.Text = treatment.TreatmentName;
            TxtTreatmentName.IsReadOnly = true;
            TxtTreatmentCost.Text = treatment.Cost.ToString(CultureInfo.CurrentCulture);
            CmbTreatmentCurrency.SelectedIndex = treatment.Currency == "USD" ? 1 : 0;
            BtnAddTreatment.Content = "حفظ التعديل";
            BtnCancelTreatmentEdit.Visibility = Visibility.Visible;
            TxtTreatmentCost.Focus();
            TxtTreatmentCost.SelectAll();
        }

        private void BtnCancelTreatmentEdit_Click(object sender, RoutedEventArgs e)
        {
            ResetTreatmentEditor();
        }

        private void ResetTreatmentEditor()
        {
            _editingTreatmentId = null;
            TxtTreatmentName.Clear();
            TxtTreatmentName.IsReadOnly = false;
            TxtTreatmentCost.Clear();
            CmbTreatmentCurrency.SelectedIndex = 0;
            BtnAddTreatment.Content = "إضافة";
            BtnCancelTreatmentEdit.Visibility = Visibility.Collapsed;
        }

        private async void BtnDeleteTreatment_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is int treatmentId)
            {
                var result = MessageBox.Show(
                    "هل أنت متأكد من حذف هذا العلاج؟",
                    "تأكيد الحذف",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (result == MessageBoxResult.Yes)
                {
                    try
                    {
                        using var db = new AppDbContext();
                        var treatment = await db.TreatmentCosts.FindAsync(treatmentId);
                        if (treatment != null)
                        {
                            db.TreatmentCosts.Remove(treatment);
                            await db.SaveChangesAsync();

                            var localTreatment = _treatmentCosts.FirstOrDefault(t => t.Id == treatmentId);
                            if (localTreatment != null)
                            {
                                _treatmentCosts.Remove(localTreatment);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"خطأ في حذف العلاج: {ex.Message}", "خطأ", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
            }
        }
    }
}
