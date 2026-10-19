using Microsoft.EntityFrameworkCore;
using MyClinic.Models;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;

namespace MyClinic
{
    public partial class VisitEditWindow : Window
    {
        private const int MaxVisitImages = 5;
        private readonly int _visitId;
        private readonly List<string> _selectedImagePaths = new();
        private List<string> _existingImagePaths = new();

        public VisitEditWindow(int visitId)
        {
            InitializeComponent();
            _visitId = visitId;
            ApplyThemeVisuals(IsDarkThemeActive());
            RefreshSelectedImages();
            LoadVisit();
            GlobalEvents.OnThemeChanged += ApplyThemeVisuals;
            Closed += (_, _) => GlobalEvents.OnThemeChanged -= ApplyThemeVisuals;
        }

        private bool IsDarkThemeActive()
        {
            if (Owner is MainWindow ownerMain)
                return ownerMain.IsDarkTheme;
            if (Application.Current?.MainWindow is MainWindow appMain)
                return appMain.IsDarkTheme;
            return false;
        }

        private static SolidColorBrush CreateBrush(string hex) =>
            new((Color)ColorConverter.ConvertFromString(hex));

        private void ApplyThemeVisuals(bool isDark)
        {
            if (isDark)
            {
                var bannerGradient = new LinearGradientBrush { StartPoint = new Point(1, 0), EndPoint = new Point(0, 1) };
                bannerGradient.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString("#0D6C6B"), 0.0));
                bannerGradient.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString("#0B8674"), 0.48));
                bannerGradient.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString("#216382"), 1.0));

                Resources["VisitBannerBg"] = bannerGradient;
                Resources["VisitBannerBorder"] = CreateBrush("#662DD4BF");
                Resources["VisitBannerTitleFg"] = CreateBrush("#FFFFFF");
                Resources["VisitBannerSubFg"] = CreateBrush("#E2F2F0");

                Resources["VisitCardBg"] = CreateBrush("#121621");
                Resources["VisitCardBorder"] = CreateBrush("#232E42");
                Resources["VisitSubCardBg"] = CreateBrush("#0B101B");
                Resources["VisitSubCardBorder"] = CreateBrush("#1E283A");

                var primaryBtnGradient = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 0) };
                primaryBtnGradient.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString("#14B8A6"), 0.0));
                primaryBtnGradient.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString("#0D9488"), 1.0));

                Resources["VisitPrimaryBtnBg"] = primaryBtnGradient;
                Resources["VisitPrimaryBtnBorder"] = CreateBrush("#5EEAD4");
                Resources["VisitSecondaryBtnBg"] = CreateBrush("#47535E");
                Resources["VisitSecondaryBtnBorder"] = CreateBrush("#64748B");
                Resources["VisitSecondaryBtnFg"] = CreateBrush("#FFFFFF");
            }
            else
            {
                Resources["VisitBannerBg"] = CreateBrush("#E3EFED");
                Resources["VisitBannerBorder"] = CreateBrush("#D5E6E3");
                Resources["VisitBannerTitleFg"] = CreateBrush("#13284A");
                Resources["VisitBannerSubFg"] = CreateBrush("#2C3E50");

                Resources["VisitCardBg"] = CreateBrush("#FFFFFF");
                Resources["VisitCardBorder"] = CreateBrush("#E4ECEA");
                Resources["VisitSubCardBg"] = CreateBrush("#F8FAFC");
                Resources["VisitSubCardBorder"] = CreateBrush("#DCE5E7");

                Resources["VisitPrimaryBtnBg"] = CreateBrush("#3348BD");
                Resources["VisitPrimaryBtnBorder"] = Brushes.Transparent;
                Resources["VisitSecondaryBtnBg"] = CreateBrush("#C3DCD8");
                Resources["VisitSecondaryBtnBorder"] = CreateBrush("#8EB4AE");
                Resources["VisitSecondaryBtnFg"] = CreateBrush("#264642");
            }

            if (EditSpotlightCanvas is not null)
                EditSpotlightCanvas.Visibility = isDark ? Visibility.Visible : Visibility.Collapsed;
        }

        private void Window_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (!IsDarkThemeActive() || EditSpotlightCanvas is null || EditSpotlightTransform is null)
                return;

            if (EditSpotlightCanvas.Visibility != Visibility.Visible)
                EditSpotlightCanvas.Visibility = Visibility.Visible;

            Point pos = e.GetPosition(RootEditGrid);
            EditSpotlightTransform.X = pos.X - (EditSpotlightEllipse.Width / 2.0);
            EditSpotlightTransform.Y = pos.Y - (EditSpotlightEllipse.Height / 2.0);
        }

        private void Window_MouseLeave(object sender, MouseEventArgs e)
        {
            if (EditSpotlightTransform is not null)
            {
                EditSpotlightTransform.X = -1200;
                EditSpotlightTransform.Y = -1200;
            }
        }

        private void LoadVisit()
        {
            using AppDbContext db = new();
            Visit? visit = db.Visits.AsNoTracking()
                .Include(item => item.Patient)
                .FirstOrDefault(item => item.Id == _visitId);
            if (visit is null)
            {
                MessageBox.Show("تعذر العثور على الزيارة المطلوبة.", "تعديل الزيارة", MessageBoxButton.OK, MessageBoxImage.Warning);
                Loaded += (_, _) => Close();
                return;
            }

            IsPregnantCheckBox.IsChecked = visit.IsPregnant;
            IsNursingCheckBox.IsChecked = visit.IsNursing;
            PregnancyMonthText.Text = visit.PregnancyMonth?.ToString(System.Globalization.CultureInfo.CurrentCulture) ?? string.Empty;
            BloodPressureText.Text = visit.BloodPressure ?? string.Empty;
            HeartRateText.Text = visit.HeartRate ?? string.Empty;
            TemperatureText.Text = visit.Temperature ?? string.Empty;
            RespiratoryRateText.Text = visit.RespiratoryRate ?? string.Empty;
            WeightText.Text = visit.Weight ?? string.Empty;
            HeightText.Text = visit.Height ?? string.Empty;
            BloodSugarText.Text = visit.BloodSugar ?? string.Empty;
            SymptomsText.Text = visit.Symptoms ?? string.Empty;
            DiagnosisText.Text = visit.Diagnosis ?? string.Empty;
            TreatmentPlanText.Text = visit.TreatmentPlanNotes ?? string.Empty;
            FinalTreatmentText.Text = visit.FinalTreatment ?? string.Empty;
            _existingImagePaths = DeserializeAttachmentPaths(visit.AttachedImagePathsJson);
            RefreshSelectedImages();
        }

        private void SelectImages_Click(object sender, RoutedEventArgs e)
        {
            int remainingSlots = MaxVisitImages - _existingImagePaths.Count - _selectedImagePaths.Count;
            if (remainingSlots <= 0)
            {
                MessageBox.Show($"وصلت الزيارة إلى الحد الأقصى وهو {MaxVisitImages} صور.", "صور الزيارة", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            OpenFileDialog dialog = new()
            {
                Filter = "Image Files|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.webp",
                Multiselect = true,
                Title = "اختر صوراً لإضافتها إلى الزيارة"
            };

            if (dialog.ShowDialog(this) != true)
                return;

            foreach (string path in dialog.FileNames
                         .Where(File.Exists)
                         .Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (_selectedImagePaths.Contains(path, StringComparer.OrdinalIgnoreCase))
                    continue;

                if (_existingImagePaths.Count + _selectedImagePaths.Count >= MaxVisitImages)
                    break;

                _selectedImagePaths.Add(path);
            }

            RefreshSelectedImages();
            if (_existingImagePaths.Count + _selectedImagePaths.Count >= MaxVisitImages)
                MessageBox.Show($"يمكن إرفاق {MaxVisitImages} صور كحد أقصى لكل زيارة.", "صور الزيارة", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void ClearSelectedImages_Click(object sender, RoutedEventArgs e)
        {
            _selectedImagePaths.Clear();
            RefreshSelectedImages();
        }

        private void RefreshSelectedImages()
        {
            if (SelectedImagesList is null)
                return;

            SelectedImagesList.ItemsSource = _selectedImagePaths
                .Select(path => Path.GetFileName(path) ?? path)
                .ToList();
            ImageSelectionHint.Text = $"الصور الحالية: {_existingImagePaths.Count} — الصور الجديدة: {_selectedImagePaths.Count} — الحد الأقصى: {MaxVisitImages}";
        }

        private async void Save_Click(object sender, RoutedEventArgs e)
        {
            if (!int.TryParse(PregnancyMonthText.Text.Trim(), NumberStyles.Integer, CultureInfo.CurrentCulture, out int pregnancyMonth) &&
                !string.IsNullOrWhiteSpace(PregnancyMonthText.Text))
            {
                MessageBox.Show("يرجى إدخال شهر حمل صحيح من 1 إلى 9 أو تركه فارغاً.", "قيمة غير صحيحة", MessageBoxButton.OK, MessageBoxImage.Warning);
                PregnancyMonthText.Focus();
                return;
            }
            if (!string.IsNullOrWhiteSpace(PregnancyMonthText.Text) && pregnancyMonth is < 1 or > 9)
            {
                MessageBox.Show("يجب أن يكون شهر الحمل بين 1 و9.", "قيمة غير صحيحة", MessageBoxButton.OK, MessageBoxImage.Warning);
                PregnancyMonthText.Focus();
                return;
            }

            try
            {
                using AppDbContext db = new();
                Visit? visit = await db.Visits
                    .Include(item => item.ToothRecords)
                    .Include(item => item.Patient)
                    .FirstOrDefaultAsync(item => item.Id == _visitId);
                if (visit is null)
                {
                    MessageBox.Show("لم تعد الزيارة موجودة في قاعدة البيانات.", "تعديل الزيارة", MessageBoxButton.OK, MessageBoxImage.Warning);
                    DialogResult = false;
                    return;
                }

                List<VisitEditChange> changes = new();
                AddChange(changes, "حالة الحمل", FormatBoolean(visit.IsPregnant), FormatBoolean(IsPregnantCheckBox.IsChecked == true));
                AddChange(changes, "حالة الرضاعة", FormatBoolean(visit.IsNursing), FormatBoolean(IsNursingCheckBox.IsChecked == true));
                AddChange(changes, "شهر الحمل", FormatValue(visit.PregnancyMonth), FormatValue(string.IsNullOrWhiteSpace(PregnancyMonthText.Text) ? (int?)null : pregnancyMonth));
                AddChange(changes, "ضغط الدم", FormatValue(visit.BloodPressure), FormatValue(Normalize(BloodPressureText.Text)));
                AddChange(changes, "النبض", FormatValue(visit.HeartRate), FormatValue(Normalize(HeartRateText.Text)));
                AddChange(changes, "الحرارة", FormatValue(visit.Temperature), FormatValue(Normalize(TemperatureText.Text)));
                AddChange(changes, "معدل التنفس", FormatValue(visit.RespiratoryRate), FormatValue(Normalize(RespiratoryRateText.Text)));
                AddChange(changes, "الوزن", FormatValue(visit.Weight), FormatValue(Normalize(WeightText.Text)));
                AddChange(changes, "الطول", FormatValue(visit.Height), FormatValue(Normalize(HeightText.Text)));
                AddChange(changes, "سكر الدم", FormatValue(visit.BloodSugar), FormatValue(Normalize(BloodSugarText.Text)));
                AddChange(changes, "شكوى المريض", FormatValue(visit.Symptoms), FormatValue(Normalize(SymptomsText.Text)));
                AddChange(changes, "التشخيص", FormatValue(visit.Diagnosis), FormatValue(Normalize(DiagnosisText.Text)));
                AddChange(changes, "خطة العلاج والملاحظات", FormatValue(visit.TreatmentPlanNotes), FormatValue(Normalize(TreatmentPlanText.Text)));
                AddChange(changes, "الإجراء النهائي", FormatValue(visit.FinalTreatment), FormatValue(Normalize(FinalTreatmentText.Text)));
                if (_selectedImagePaths.Count > 0)
                {
                    List<string> afterImages = _existingImagePaths
                        .Concat(_selectedImagePaths.Select(Path.GetFileName))
                        .ToList();
                    AddChange(
                        changes,
                        "صور الزيارة",
                        FormatAttachmentList(_existingImagePaths),
                        FormatAttachmentNames(afterImages));
                }

                if (changes.Count == 0)
                {
                    MessageBox.Show("لم يتم إجراء أي تغييرات على الزيارة.", "تعديل الزيارة", MessageBoxButton.OK, MessageBoxImage.Information);
                    DialogResult = false;
                    return;
                }

                DateTime editedAt = AppClock.Now;
                visit.IsPregnant = IsPregnantCheckBox.IsChecked == true;
                visit.IsNursing = IsNursingCheckBox.IsChecked == true;
                visit.PregnancyMonth = string.IsNullOrWhiteSpace(PregnancyMonthText.Text) ? null : pregnancyMonth;
                visit.BloodPressure = Normalize(BloodPressureText.Text);
                visit.HeartRate = Normalize(HeartRateText.Text);
                visit.Temperature = Normalize(TemperatureText.Text);
                visit.RespiratoryRate = Normalize(RespiratoryRateText.Text);
                visit.Weight = Normalize(WeightText.Text);
                visit.Height = Normalize(HeightText.Text);
                visit.BloodSugar = Normalize(BloodSugarText.Text);
                visit.Symptoms = Normalize(SymptomsText.Text);
                visit.Diagnosis = Normalize(DiagnosisText.Text);
                visit.TreatmentPlanNotes = Normalize(TreatmentPlanText.Text);
                visit.FinalTreatment = Normalize(FinalTreatmentText.Text);
                if (_selectedImagePaths.Count > 0)
                {
                    string patientName = string.IsNullOrWhiteSpace(visit.Patient?.FullName) ? "بدون_اسم" : visit.Patient.FullName;
                    List<string> newImagePaths = SaveVisitImages(patientName, visit.Id, _selectedImagePaths);
                    visit.AttachedImagePathsJson = JsonSerializer.Serialize(_existingImagePaths.Concat(newImagePaths));
                }

                db.VisitEditHistories.Add(new VisitEditHistory
                {
                    VisitId = visit.Id,
                    EditedAt = editedAt,
                    EditedBy = string.IsNullOrWhiteSpace(LoginSessionStore.CurrentUsername) ? "غير معروف" : LoginSessionStore.CurrentUsername,
                    ChangesJson = JsonSerializer.Serialize(changes)
                });
                await db.SaveChangesAsync();
                AppRuntimeCache.AddOrUpdateVisit(visit);

                DialogResult = true;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"تعذر حفظ تعديل الزيارة:\n{ex.Message}", "خطأ في الحفظ", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private static string? Normalize(string value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        private static List<string> DeserializeAttachmentPaths(string? json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return new();

            try
            {
                return JsonSerializer.Deserialize<List<string>>(json)?
                    .Where(path => !string.IsNullOrWhiteSpace(path))
                    .ToList() ?? new();
            }
            catch (JsonException ex)
            {
                throw new InvalidDataException("تعذر قراءة مرفقات الزيارة الحالية.", ex);
            }
        }

        private static string FormatAttachmentList(IEnumerable<string> paths)
        {
            List<string> names = paths.Select(GetAttachmentName).ToList();
            return names.Count == 0 ? "لا توجد صور" : $"{names.Count}: {string.Join("، ", names)}";
        }

        private static string FormatAttachmentNames(IEnumerable<string?> names)
        {
            List<string> list = names.Where(name => !string.IsNullOrWhiteSpace(name)).Cast<string>().ToList();
            return list.Count == 0 ? "لا توجد صور" : $"{list.Count}: {string.Join("، ", list)}";
        }

        private static string GetAttachmentName(string path)
        {
            string normalized = path.Replace('/', Path.DirectorySeparatorChar);
            return Path.GetFileName(normalized);
        }

        private static List<string> SaveVisitImages(string patientName, int visitId, IEnumerable<string> selectedImagePaths)
        {
            DateTime imageTimestamp = AppClock.Now;
            string root = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "MyClinicApp",
                "PatientVisitImages");
            Directory.CreateDirectory(root);

            string folderName = $"{SanitizePathSegment(patientName)}_visit_{visitId}_{imageTimestamp:yyyyMMdd_HHmmss_fff}";
            string folderPath = Path.Combine(root, folderName);
            Directory.CreateDirectory(folderPath);

            List<string> relativePaths = new();
            int sequence = 1;
            foreach (string source in selectedImagePaths.Where(File.Exists))
            {
                string fileName = $"{imageTimestamp:yyyyMMdd_HHmmss_fff}_{sequence:00}{Path.GetExtension(source)}";
                string destination = Path.Combine(folderPath, fileName);
                File.Copy(source, destination);
                relativePaths.Add(Path.Combine(folderName, fileName).Replace("\\", "/"));
                sequence++;
            }

            return relativePaths;
        }

        private static string SanitizePathSegment(string value)
        {
            char[] invalidCharacters = Path.GetInvalidFileNameChars();
            string sanitized = string.Concat(value.Trim().Select(character => invalidCharacters.Contains(character) ? '_' : character));
            return string.IsNullOrWhiteSpace(sanitized) ? "بدون_اسم" : sanitized;
        }

        private static string FormatValue<T>(T? value) => value is null ? "غير محدد" : Convert.ToString(value, CultureInfo.CurrentCulture) ?? "غير محدد";
        private static string FormatBoolean(bool value) => value ? "نعم" : "لا";
        private static void AddChange(List<VisitEditChange> changes, string field, string before, string after)
        {
            if (!string.Equals(before, after, StringComparison.Ordinal))
                changes.Add(new VisitEditChange { Field = field, Before = before, After = after });
        }

        private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
    }
}