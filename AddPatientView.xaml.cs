// AddPatientView.xaml.cs
using MyClinic.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Win32;
using System;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace MyClinic
{
    public partial class AddPatientView : UserControl
    {
        private const int MaxVisitImages = 5;
        private static string VisitDoctorName => string.IsNullOrWhiteSpace(LoginSessionStore.CurrentUsername)
            ? "د."
            : $"د. {LoginSessionStore.CurrentUsername}";
        private readonly List<PrescriptionItem> _prescriptionItems = new();
        private readonly List<string> _selectedImagePaths = new();
        private readonly ObservableCollection<PatientLookupSuggestion> _patientSuggestions = new();
        private readonly ObservableCollection<VisitHistoryItem> _previousVisits = new();
        private readonly ObservableCollection<TreatmentSelectionItem> _treatmentSelectionItems = new();
        
        // Dynamic Medicine Autofill Collections
        private readonly ObservableCollection<string> _recentMedicines = new();
        private List<string> _allMedicines = new();
        private static readonly string MedicinesFilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), 
            "MyClinicApp", 
            "Medicines.json");

        private readonly Dictionary<int, IReadOnlyList<VisitAttachmentItem>> _attachmentCache = new();
        
        private PatientLookupSnapshot? _selectedPatientLookup;
        private bool _isApplyingPatientLookup;

        public AddPatientView()
        {
            InitializeComponent();
            ItemsPatientSuggestions.ItemsSource = _patientSuggestions;
            ItemsPreviousVisits.ItemsSource = _previousVisits;
            ItemsRecentMedicines.ItemsSource = _recentMedicines; // Bind Recent drugs
            TreatmentItemsControl.ItemsSource = _treatmentSelectionItems;

            LoadMedicines(); // Loads local JSON record of previously entered medicines
            LoadTreatments(); // Load treatments from database

            UpdateRemainingVisitAmount();
            UpdateFemaleDetailsVisibility();
            UpdatePregnancyMonthVisibility();
            RefreshPrescriptionList();
            RefreshSelectedImagesList();
        }

        #region Medicine Persistence & Autofill Logic
        
        private void LoadMedicines()
        {
            try
            {
                if (File.Exists(MedicinesFilePath))
                {
                    string json = File.ReadAllText(MedicinesFilePath);
                    var list = JsonSerializer.Deserialize<List<string>>(json);
                    if (list != null)
                    {
                        _allMedicines = list.Distinct(StringComparer.CurrentCultureIgnoreCase).ToList();
                        UpdateRecentMedicinesUI();
                    }
                }
            }
            catch { /* Keep going if error */ }
        }

        private void SaveMedicines()
        {
            try
            {
                File.WriteAllText(MedicinesFilePath, JsonSerializer.Serialize(_allMedicines));
            }
            catch { /* Keep going if error */ }
        }

        private void AddToMedicines(string medicine)
        {
            if (string.IsNullOrWhiteSpace(medicine)) return;
            medicine = medicine.Trim();

            // Move this medicine to the top (Most recent)
            _allMedicines.RemoveAll(m => string.Equals(m, medicine, StringComparison.CurrentCultureIgnoreCase));
            _allMedicines.Insert(0, medicine);

            UpdateRecentMedicinesUI();
            SaveMedicines();
        }

        private void UpdateRecentMedicinesUI()
        {
            _recentMedicines.Clear();
            foreach (var m in _allMedicines.Take(10))
            {
                _recentMedicines.Add(m);
            }
        }

        private void BtnRecentMedicine_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement { Tag: string medicine })
            {
                TxtPrescriptionMedicine.Text = medicine;
                TxtPrescriptionMedicine.Focus();
                TxtPrescriptionMedicine.CaretIndex = medicine.Length;
                PopupMedicineSuggestions.IsOpen = false;
            }
        }

        private void TxtPrescriptionMedicine_TextChanged(object sender, TextChangedEventArgs e)
        {
            string query = TxtPrescriptionMedicine.Text.Trim();
            if (string.IsNullOrWhiteSpace(query))
            {
                PopupMedicineSuggestions.IsOpen = false;
                return;
            }

            var matches = _allMedicines
                .Where(m => m.Contains(query, StringComparison.CurrentCultureIgnoreCase))
                .Take(10)
                .ToList();

            // Only show suggestions popup if there are results AND we didn't just auto-fill exactly the result.
            if (matches.Any() && !matches.Any(m => string.Equals(m, query, StringComparison.CurrentCultureIgnoreCase)))
            {
                LstMedicineSuggestions.ItemsSource = matches;
                PopupMedicineSuggestions.IsOpen = true;
            }
            else
            {
                PopupMedicineSuggestions.IsOpen = false;
            }
        }

        private void LstMedicineSuggestions_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (LstMedicineSuggestions.SelectedItem is string medicine)
            {
                TxtPrescriptionMedicine.Text = medicine;
                TxtPrescriptionMedicine.Focus();
                TxtPrescriptionMedicine.CaretIndex = medicine.Length;
                PopupMedicineSuggestions.IsOpen = false;
            }
        }

        #endregion

        #region Treatment Selection Logic

        private async void LoadTreatments()
        {
            try
            {
                using var db = new AppDbContext();
                var treatments = await db.TreatmentCosts
                    .OrderBy(t => t.TreatmentName)
                    .ToListAsync();

                _treatmentSelectionItems.Clear();
                foreach (var treatment in treatments)
                {
                    _treatmentSelectionItems.Add(new TreatmentSelectionItem
                    {
                        TreatmentId = treatment.Id,
                        TreatmentName = treatment.TreatmentName,
                        Cost = treatment.Cost,
                        Currency = treatment.Currency,
                        IsSelected = false,
                        Quantity = 1
                    });
                }
            }
            catch (Exception)
            {
                // If treatments fail to load, continue without them
            }
        }

        private void TreatmentCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            CalculateTotalCost();
        }

        private void TreatmentQuantity_TextChanged(object sender, TextChangedEventArgs e)
        {
            CalculateTotalCost();
        }

        private void CalculateTotalCost()
        {
            decimal total = 0;
            foreach (var item in _treatmentSelectionItems)
            {
                if (item.IsSelected)
                {
                    int quantity = item.Quantity > 0 ? item.Quantity : 1;
                    decimal cost = item.Cost;
                    
                    // Convert USD to SYP if needed
                    if (item.Currency == "USD")
                    {
                        try
                        {
                            using var db = new AppDbContext();
                            var settings = db.AppSettings.FirstOrDefault();
                            if (settings != null)
                            {
                                cost = cost * settings.UsdToSypRate;
                            }
                        }
                        catch (Exception)
                        {
                            // If conversion fails, use original cost
                        }
                    }
                    
                    total += cost * quantity;
                }
            }

            TxtCurrentCost.Text = total.ToString("0.##", CultureInfo.CurrentCulture);
            UpdateRemainingVisitAmount();
        }

        private List<SelectedTreatment> GetSelectedTreatments()
        {
            var selected = new List<SelectedTreatment>();
            foreach (var item in _treatmentSelectionItems)
            {
                if (item.IsSelected)
                {
                    selected.Add(new SelectedTreatment
                    {
                        TreatmentId = item.TreatmentId,
                        TreatmentName = item.TreatmentName,
                        Cost = item.Cost,
                        Currency = item.Currency,
                        Quantity = item.Quantity > 0 ? item.Quantity : 1
                    });
                }
            }
            return selected;
        }

        #endregion

        private void TextBox_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
        {
            if (sender is TextBox textBox)
            {
                textBox.SelectAll();
            }
        }

        private void TextBox_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is TextBox textBox && !textBox.IsKeyboardFocusWithin)
            {
                e.Handled = true;
                textBox.Focus();
            }
        }

        private void FinancialInput_TextChanged(object sender, TextChangedEventArgs e)
        {
            UpdateRemainingVisitAmount();
        }

        private void ChkSmoker_CheckedChanged(object sender, RoutedEventArgs e)
        {
            if (PanelSmokerDetails != null)
            {
                PanelSmokerDetails.Visibility = ChkSmoker.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        private void CmbGender_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdateFemaleDetailsVisibility();
        }

        private void ChkPregnant_CheckedChanged(object sender, RoutedEventArgs e)
        {
            UpdatePregnancyMonthVisibility();
        }

        private void BtnAdult_Click(object sender, RoutedEventArgs e)
        {
            AdultChartControl.Visibility = Visibility.Visible;
            ChildChartControl.Visibility = Visibility.Collapsed;

            BtnAdult.Background = Brushes.White;
            BtnAdult.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2563EB"));

            BtnChild.Background = Brushes.Transparent;
            BtnChild.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#64748B"));
        }

        private void BtnChild_Click(object sender, RoutedEventArgs e)
        {
            AdultChartControl.Visibility = Visibility.Collapsed;
            ChildChartControl.Visibility = Visibility.Visible;

            BtnChild.Background = Brushes.White;
            BtnChild.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2563EB"));

            BtnAdult.Background = Brushes.Transparent;
            BtnAdult.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#64748B"));
        }

        private void PatientLookup_TextChanged(object sender, TextChangedEventArgs e)
        {
            RefreshPatientSuggestions();
        }

        private void BtnApplyPatientSuggestion_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement { Tag: PatientLookupSuggestion suggestion })
            {
                ApplyPatientSuggestion(suggestion.Snapshot);
            }
        }

        private async void BtnOpenPreviousVisit_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement { Tag: VisitHistoryItem visit } || _selectedPatientLookup is null)
            {
                return;
            }

            ObservableCollection<VisitAttachmentItem> attachmentCollection = new();

            bool alreadyCached = _attachmentCache.TryGetValue(visit.VisitId, out IReadOnlyList<VisitAttachmentItem>? cachedItems);
            if (alreadyCached && cachedItems is not null)
            {
                foreach (VisitAttachmentItem item in cachedItems)
                {
                    attachmentCollection.Add(item);
                }
            }

            VisitDetailsViewModel viewModel = new()
            {
                PatientName = _selectedPatientLookup.DisplayName,
                DoctorName = VisitDoctorName,
                VisitDateTimeText = visit.VisitDateTimeText,
                BloodTypeText = NormalizeBloodTypeLabel(_selectedPatientLookup.BloodType),
                AllergiesText = NormalizeOrFallback(_selectedPatientLookup.Allergies, "غير محددة"),
                ChronicDiseasesText = NormalizeOrFallback(_selectedPatientLookup.ChronicDiseases, "غير محددة"),
                SmokingText = BuildSmokingSummary(_selectedPatientLookup),
                PregnancyText = visit.PregnancySummary,
                BloodPressureText = visit.BloodPressureText,
                HeartRateText = visit.HeartRateText,
                TemperatureText = visit.TemperatureText,
                RespiratoryRateText = visit.RespiratoryRateText,
                WeightText = visit.WeightText,
                HeightText = visit.HeightText,
                BmiText = visit.BmiText,
                SymptomsText = visit.SymptomsText,
                DiagnosisText = visit.DiagnosisText,
                ChartModeText = visit.ChartModeText,
                Teeth = visit.ToothIds,
                TreatmentPlanText = visit.TreatmentPlanText,
                PrescriptionItems = visit.PrescriptionItems,
                SelectedTreatments = visit.SelectedTreatments,
                AttachmentItems = attachmentCollection
            };

            VisitDetailsModal.DataContext = viewModel;
            VisitDetailsOverlay.Visibility = Visibility.Visible;

            if (!alreadyCached && visit.AttachmentPaths.Count > 0)
            {
                List<VisitAttachmentItem> items = await Task.Run(
                    () => LoadAttachmentItems(visit.AttachmentPaths).ToList());

                _attachmentCache[visit.VisitId] = items;

                if (ReferenceEquals(VisitDetailsModal.DataContext, viewModel))
                {
                    foreach (VisitAttachmentItem item in items)
                    {
                        viewModel.AttachmentItems.Add(item);
                    }
                }
            }
        }

        private void CloseVisitDetails_Click(object sender, RoutedEventArgs e)
        {
            HideVisitDetailsOverlay();
        }

        private void VisitDetailsDimmer_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            HideVisitDetailsOverlay();
        }

        private void OpenVisitAttachment_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement { Tag: VisitAttachmentItem attachment })
            {
                return;
            }

            if (!File.Exists(attachment.FilePath))
            {
                MessageBox.Show("تعذر العثور على الملف المطلوب.", "المرفقات", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = attachment.FilePath,
                    UseShellExecute = true
                });
            }
            catch (Exception)
            {
                MessageBox.Show("تعذر فتح الملف حالياً.", "المرفقات", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void RefreshPatientSuggestions()
        {
            if (_isApplyingPatientLookup || TxtPhoneNumber is null || TxtFullName is null)
            {
                return;
            }

            string phoneQuery = TxtPhoneNumber.Text.Trim();
            string nameQuery = TxtFullName.Text.Trim();

            if (phoneQuery.Length == 0 && nameQuery.Length < 2)
            {
                ClearPatientSuggestions();

                if (!MatchesSelectedPatient(phoneQuery, nameQuery))
                {
                    _selectedPatientLookup = null;
                    ClearPreviousVisits();
                }

                return;
            }

            using var db = new AppDbContext();
            List<PatientLookupSnapshot> candidates = db.Patients
                .AsNoTracking()
                .Select(patient => new PatientLookupSnapshot
                {
                    Id = patient.Id,
                    PhoneNumber = patient.PhoneNumber,
                    FullName = patient.FullName,
                    Age = patient.Age,
                    Gender = patient.Gender,
                    BloodType = patient.BloodType,
                    IsSmoker = patient.IsSmoker,
                    SmokingType = patient.SmokingType,
                    SmokingFrequency = patient.SmokingFrequency,
                    Allergies = patient.Allergies,
                    ChronicDiseases = patient.ChronicDiseases,
                    VisitCount = patient.Visits.Count(),
                    LatestVisitDate = patient.Visits.Max(visit => (DateTime?)visit.VisitDate)
                })
                .ToList();

            List<PatientLookupSuggestion> suggestions = candidates
                .Select(snapshot => CreatePatientSuggestion(snapshot, phoneQuery, nameQuery))
                .Where(suggestion => suggestion is not null)
                .Cast<PatientLookupSuggestion>()
                .OrderByDescending(suggestion => suggestion.Score)
                .ThenByDescending(suggestion => suggestion.Snapshot.LatestVisitDate)
                .ThenBy(suggestion => suggestion.DisplayName, StringComparer.CurrentCulture)
                .Take(6)
                .ToList();

            _patientSuggestions.Clear();
            foreach (PatientLookupSuggestion suggestion in suggestions)
            {
                _patientSuggestions.Add(suggestion);
            }

            bool hasSuggestions = suggestions.Count > 0;
            PatientSuggestionsPanel.Visibility = hasSuggestions ? Visibility.Visible : Visibility.Collapsed;
            TxtPatientSuggestionsHint.Text = hasSuggestions
                ? "اختر المريض المناسب لتعبئة البيانات تلقائياً."
                : "لا توجد نتائج مطابقة حالياً.";

            if (!hasSuggestions && !MatchesSelectedPatient(phoneQuery, nameQuery))
            {
                _selectedPatientLookup = null;
                ClearPreviousVisits();
            }
        }

        private void ApplyPatientSuggestion(PatientLookupSnapshot snapshot)
        {
            _isApplyingPatientLookup = true;

            try
            {
                _selectedPatientLookup = snapshot;
                TxtPhoneNumber.Text = snapshot.PhoneNumber;
                TxtFullName.Text = snapshot.FullName ?? string.Empty;
                TxtAge.Text = snapshot.Age?.ToString(CultureInfo.CurrentCulture) ?? string.Empty;

                TrySelectComboBoxItemByText(CmbGender, snapshot.Gender);
                if (string.IsNullOrWhiteSpace(snapshot.BloodType))
                {
                    CmbBloodType.SelectedIndex = 0;
                }
                else
                {
                    TrySelectComboBoxItemByText(CmbBloodType, snapshot.BloodType);
                }

                TxtAllergies.Text = snapshot.Allergies ?? string.Empty;
                TxtChronicDiseases.Text = snapshot.ChronicDiseases ?? string.Empty;

                ChkSmoker.IsChecked = snapshot.IsSmoker;
                TxtSmokingType.Text = snapshot.SmokingType ?? string.Empty;
                TxtSmokingFrequency.Text = snapshot.SmokingFrequency ?? string.Empty;

                LoadPreviousVisits(snapshot);
                ClearPatientSuggestions();
            }
            finally
            {
                _isApplyingPatientLookup = false;
            }
        }

        private async void LoadPreviousVisits(PatientLookupSnapshot snapshot)
        {
            using var db = new AppDbContext();

            // 1. جلب الزيارات الخاصة بالمريض
            var rawVisits = db.Visits
                .AsNoTracking()
                .Where(visit => visit.PatientId == snapshot.Id)
                .Include(visit => visit.ToothRecords)
                .OrderByDescending(visit => visit.VisitDate)
                .ToList();

            // 2. حساب الإجماليات المالية السابقة
            decimal totalCost = rawVisits.Sum(v => (decimal)v.CurrentCost);
            decimal totalPaid = rawVisits.Sum(v => (decimal)v.TodayPaid);
            decimal totalRemaining = totalCost - totalPaid;

            // 3. تحديث وإظهار شريط الإجماليات
            if (TxtTotalPatientCost != null) TxtTotalPatientCost.Text = totalCost.ToString("0.##", CultureInfo.CurrentCulture);
            if (TxtTotalPatientPaid != null) TxtTotalPatientPaid.Text = totalPaid.ToString("0.##", CultureInfo.CurrentCulture);
            if (TxtTotalPatientRemaining != null) TxtTotalPatientRemaining.Text = totalRemaining.ToString("0.##", CultureInfo.CurrentCulture);
            if (PatientFinancialHistoryPanel != null) PatientFinancialHistoryPanel.Visibility = Visibility.Visible;

            // 4. إكمال الكود الأصلي
            List<VisitHistoryItem> visits = rawVisits.Select(MapVisit).ToList();

            _previousVisits.Clear();
            foreach (VisitHistoryItem visit in visits)
            {
                _previousVisits.Add(visit);
            }

            if (visits.Count == 0)
            {
                ClearPreviousVisits();
                return;
            }

            if (PreviousVisitsPanel != null) PreviousVisitsPanel.Visibility = Visibility.Visible;
            if (TxtPreviousVisitsCount != null) TxtPreviousVisitsCount.Text = visits.Count.ToString(CultureInfo.CurrentCulture);
            if (TxtPreviousVisitsHint != null)
            {
                TxtPreviousVisitsHint.Text = visits.Count == 1
                    ? "تم العثور على زيارة سابقة واحدة. اضغط عليها لعرض التفاصيل."
                    : $"تم العثور على {visits.Count} زيارات سابقة. اضغط على أي زيارة لعرض التفاصيل.";
            }

            List<VisitHistoryItem> needsLoad = visits
                .Where(v => v.AttachmentPaths.Count > 0 && !_attachmentCache.ContainsKey(v.VisitId))
                .ToList();

            if (needsLoad.Count > 0)
            {
                using SemaphoreSlim sem = new(3);
                await Task.WhenAll(needsLoad.Select(async v =>
                {
                    await sem.WaitAsync();
                    try
                    {
                        IReadOnlyList<VisitAttachmentItem> items =
                            await Task.Run(() => LoadAttachmentItems(v.AttachmentPaths));
                        _attachmentCache[v.VisitId] = items;
                    }
                    finally
                    {
                        sem.Release();
                    }
                }));
            }
        }

        private void ClearPatientSuggestions()
        {
            _patientSuggestions.Clear();
            PatientSuggestionsPanel.Visibility = Visibility.Collapsed;
            TxtPatientSuggestionsHint.Text = "اختر المريض المناسب لتعبئة البيانات تلقائياً.";
        }

        private void ClearPreviousVisits()
        {
            _previousVisits.Clear();
            if (PreviousVisitsPanel != null) PreviousVisitsPanel.Visibility = Visibility.Collapsed;
            if (TxtPreviousVisitsCount != null) TxtPreviousVisitsCount.Text = "0";
            if (TxtPreviousVisitsHint != null) TxtPreviousVisitsHint.Text = "اضغط على أي زيارة لعرض تفاصيلها.";
            
            // إخفاء شريط السجل المالي عند عدم وجود مريض
            if (PatientFinancialHistoryPanel != null)
            {
                PatientFinancialHistoryPanel.Visibility = Visibility.Collapsed;
            }
            
            HideVisitDetailsOverlay();
        }

        private void HideVisitDetailsOverlay()
        {
            VisitDetailsOverlay.Visibility = Visibility.Collapsed;
            
            if (VisitDetailsModal.DataContext is VisitDetailsViewModel vm)
            {
                vm.AttachmentItems.Clear();
            }

            VisitDetailsModal.DataContext = null;
        }

        private bool MatchesSelectedPatient(string phoneQuery, string nameQuery)
        {
            if (_selectedPatientLookup is null)
            {
                return false;
            }

            // لضمان أنه عند تغيير الاسم أو الرقم بعد التحديد، يتم فك الارتباط لإنشاء فرد جديد من العائلة
            bool matchesPhone = string.IsNullOrWhiteSpace(phoneQuery) ||
                _selectedPatientLookup.PhoneNumber.Contains(phoneQuery, StringComparison.CurrentCultureIgnoreCase) ||
                phoneQuery.Contains(_selectedPatientLookup.PhoneNumber, StringComparison.CurrentCultureIgnoreCase);

            bool matchesName = string.IsNullOrWhiteSpace(nameQuery) ||
                _selectedPatientLookup.DisplayName.Contains(nameQuery, StringComparison.CurrentCultureIgnoreCase) ||
                nameQuery.Contains(_selectedPatientLookup.DisplayName, StringComparison.CurrentCultureIgnoreCase);

            // يجب أن يتطابق الاثنان معاً لكي يتم اعتبارهما نفس المريض المحدد
            return matchesPhone && matchesName;
        }

        private PatientLookupSuggestion? CreatePatientSuggestion(PatientLookupSnapshot snapshot, string phoneQuery, string nameQuery)
        {
            int score = 0;
            string matchReason = "مطابقة";

            if (!string.IsNullOrWhiteSpace(phoneQuery))
            {
                if (string.Equals(snapshot.PhoneNumber, phoneQuery, StringComparison.CurrentCultureIgnoreCase))
                {
                    score += 220;
                    matchReason = "مطابقة رقم";
                }
                else if (snapshot.PhoneNumber.StartsWith(phoneQuery, StringComparison.CurrentCultureIgnoreCase))
                {
                    score += 140;
                    matchReason = "رقم قريب";
                }
                else if (snapshot.PhoneNumber.Contains(phoneQuery, StringComparison.CurrentCultureIgnoreCase))
                {
                    score += 90;
                    matchReason = "جزء من الرقم";
                }
            }

            if (!string.IsNullOrWhiteSpace(nameQuery) && !string.IsNullOrWhiteSpace(snapshot.FullName))
            {
                if (string.Equals(snapshot.FullName, nameQuery, StringComparison.CurrentCultureIgnoreCase))
                {
                    score += 200;
                    matchReason = string.IsNullOrWhiteSpace(phoneQuery) ? "مطابقة اسم" : matchReason;
                }
                else if (snapshot.FullName.StartsWith(nameQuery, StringComparison.CurrentCultureIgnoreCase))
                {
                    score += 120;
                    matchReason = string.IsNullOrWhiteSpace(phoneQuery) ? "اسم قريب" : matchReason;
                }
                else if (snapshot.FullName.Contains(nameQuery, StringComparison.CurrentCultureIgnoreCase))
                {
                    score += 80;
                    matchReason = string.IsNullOrWhiteSpace(phoneQuery) ? "جزء من الاسم" : matchReason;
                }
            }

            if (score == 0)
            {
                return null;
            }

            return new PatientLookupSuggestion
            {
                Snapshot = snapshot,
                Score = score,
                MatchReason = matchReason,
                DisplayName = snapshot.DisplayName,
                SecondaryLine = $"{snapshot.PhoneNumber} - {snapshot.AgeLabel} - {snapshot.GenderLabel}",
                MetaLine = $"{snapshot.VisitCountLabel} - {snapshot.LatestVisitLabel}"
            };
        }

        private static void TrySelectComboBoxItemByText(ComboBox comboBox, string? text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return;
            }

            ComboBoxItem? item = comboBox.Items
                .OfType<ComboBoxItem>()
                .FirstOrDefault(existingItem => string.Equals(existingItem.Content?.ToString(), text, StringComparison.CurrentCultureIgnoreCase));

            if (item is not null)
            {
                comboBox.SelectedItem = item;
            }
        }

        private async void BtnSaveVisit_Click(object sender, RoutedEventArgs e)
        {
            string phoneNumber = TxtPhoneNumber.Text.Trim();
            if (string.IsNullOrWhiteSpace(phoneNumber))
            {
                MessageBox.Show("رقم الهاتف مطلوب لحفظ بيانات المريض.", "بيانات ناقصة", MessageBoxButton.OK, MessageBoxImage.Warning);
                TxtPhoneNumber.Focus();
                return;
            }

            if (!TryParseNullableInt(TxtAge.Text, out int? age))
            {
                MessageBox.Show("يرجى إدخال عمر صحيح أو ترك الحقل فارغاً.", "قيمة غير صحيحة", MessageBoxButton.OK, MessageBoxImage.Warning);
                TxtAge.Focus();
                TxtAge.SelectAll();
                return;
            }

            if (!TryParseMoney(TxtCurrentCost.Text, out decimal currentCost) || currentCost < 0)
            {
                MessageBox.Show("يرجى إدخال تكلفة صحيحة للزيارة.", "قيمة مالية غير صحيحة", MessageBoxButton.OK, MessageBoxImage.Warning);
                TxtCurrentCost.Focus();
                TxtCurrentCost.SelectAll();
                return;
            }

            if (!TryParseMoney(TxtTodayPaid.Text, out decimal todayPaid) || todayPaid < 0)
            {
                MessageBox.Show("يرجى إدخال دفعة صحيحة لليوم.", "قيمة مالية غير صحيحة", MessageBoxButton.OK, MessageBoxImage.Warning);
                TxtTodayPaid.Focus();
                TxtTodayPaid.SelectAll();
                return;
            }

            decimal remainingAmount = Math.Max(0, currentCost - todayPaid);

            bool isFemale = CmbGender.SelectedIndex == 1;
            bool isPregnant = isFemale && ChkPregnant.IsChecked == true;

            try
            {
                using var db = new AppDbContext();

                Patient? patient = null;

                // تحديث المريض الحالي فقط إذا قام المستخدم باختياره بشكل صريح من قائمة المقترحات
                if (_selectedPatientLookup != null)
                {
                    patient = db.Patients.FirstOrDefault(existingPatient => existingPatient.Id == _selectedPatientLookup.Id);
                }

                // إنشاء مريض جديد إذا لم يتم اختيار أحد (يسمح بإضافة أفراد العائلة بنفس رقم الهاتف)
                if (patient is null)
                {
                    patient = new Patient
                    {
                        PhoneNumber = phoneNumber
                    };

                    db.Patients.Add(patient);
                }
                else
                {
                    // تحديث رقم الهاتف في حال قام المستخدم بتصحيح خطأ إملائي بسيط بعد الاختيار
                    patient.PhoneNumber = phoneNumber;
                }

                patient.FullName = NullIfWhiteSpace(TxtFullName.Text);
                patient.Age = age;
                patient.Gender = GetSelectedComboBoxText(CmbGender);
                patient.BloodType = NormalizeBloodType(GetSelectedComboBoxText(CmbBloodType));
                patient.IsSmoker = ChkSmoker.IsChecked == true;
                patient.SmokingType = patient.IsSmoker ? NullIfWhiteSpace(TxtSmokingType.Text) : null;
                patient.SmokingFrequency = patient.IsSmoker ? NullIfWhiteSpace(TxtSmokingFrequency.Text) : null;
                patient.Allergies = NullIfWhiteSpace(TxtAllergies.Text);
                patient.ChronicDiseases = NullIfWhiteSpace(TxtChronicDiseases.Text);

                // Snapshot the current USD→SYP rate so historic payment details
                // are never retroactively affected by future rate changes.
                double usdToSypRateSnapshot = 15000;
                var appSettings = db.AppSettings.FirstOrDefault();
                if (appSettings != null)
                    usdToSypRateSnapshot = (double)appSettings.UsdToSypRate;

                Visit visit = new()
                {
                    Patient = patient,
                    VisitDate = DateTime.Now.AddHours(7),
                    IsPregnant = isPregnant,
                    IsNursing = isFemale && ChkNursing.IsChecked == true,
                    PregnancyMonth = isPregnant ? CmbPregnancyMonth.SelectedIndex + 1 : null,
                    BloodPressure = NullIfWhiteSpace(TxtBloodPressure.Text),
                    HeartRate = NullIfWhiteSpace(TxtHeartRate.Text),
                    Temperature = NullIfWhiteSpace(TxtTemperature.Text),
                    RespiratoryRate = NullIfWhiteSpace(TxtRespiratoryRate.Text),
                    Weight = NullIfWhiteSpace(TxtWeight.Text),
                    Height = NullIfWhiteSpace(TxtHeight.Text),
                    Symptoms = NullIfWhiteSpace(TxtSymptoms.Text),
                    Diagnosis = NullIfWhiteSpace(TxtDiagnosis.Text),
                    PrescriptionJson = _prescriptionItems.Count > 0 ? JsonSerializer.Serialize(_prescriptionItems) : null,
                    TreatmentPlanNotes = NullIfWhiteSpace(TxtTreatmentPlanNotes.Text),
                    SelectedTreatmentsJson = GetSelectedTreatments().Count > 0 ? JsonSerializer.Serialize(GetSelectedTreatments()) : null,
                    CurrentCost = (double)currentCost,
                    TodayPaid = (double)todayPaid,
                    RemainingAmount = (double)remainingAmount,
                    UsdToSypRateSnapshot = usdToSypRateSnapshot,
                    ChartMode = AdultChartControl.Visibility == Visibility.Visible ? "Adult" : "Child",
                    ToothRecords = GetSelectedToothIds()
                        .Select(toothId => new ToothRecord
                        {
                            ToothId = toothId
                        })
                        .ToList()
                };

                db.Visits.Add(visit);
                await db.SaveChangesAsync(); // أو db.SaveChanges();
                

                if (_selectedImagePaths.Count > 0)
                {
                    string safePatientName = string.IsNullOrWhiteSpace(patient.FullName) ? "بدون_اسم" : patient.FullName;
                    visit.AttachedImagePathsJson = SaveVisitImages(safePatientName, visit.Id, _selectedImagePaths);
                    await db.SaveChangesAsync();
                    
                }

                // Update the global runtime cache so the Dashboard stays accurate
                AppRuntimeCache.AddOrUpdateVisit(visit);

                // IMMEDIATELY notify the MainWindow that Financials need a refresh
                if (Window.GetWindow(this) is MainWindow mainWindow)
                {
                    mainWindow.InvalidateFinancialRecords();
                }

                // إضافة هذا السطر فوراً بعد نجاح عملية الحفظ لإشعار الواجهة المالية بوجود تحديث
                GlobalEvents.NotifyFinancialRecordAdded();

                // --- الكود الجديد: إرسال تنبيه لتحديث سجل المرضى ---
                GlobalEvents.NotifyPatientRecordAdded(); 

                // ... باقي كود تفريغ الحقول وإظهار رسالة النجاح ...
                MessageBox.Show("تم حفظ المريض والزيارة بنجاح.", "تم الحفظ", MessageBoxButton.OK, MessageBoxImage.Information);
                ResetForm();
                NavigateToDashboard();
            }
            catch (Exception ex)
            {
                // ... معالجة الأخطاء ...
                MessageBox.Show($"حدث خطأ أثناء حفظ البيانات:\n{ex.Message}", "خطأ", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            ResetForm();
            NavigateToDashboard();
        }

        private void UpdateRemainingVisitAmount()
        {
            decimal currentCost = ParseDecimalOrZero(TxtCurrentCost?.Text);
            decimal todayPaid = ParseDecimalOrZero(TxtTodayPaid?.Text);
            decimal remainingAmount = Math.Max(0, currentCost - todayPaid);

            if (TxtRemainingAmount != null)
            {
                TxtRemainingAmount.Text = remainingAmount.ToString("0.##", CultureInfo.CurrentCulture);
            }
        }

        private void UpdateFemaleDetailsVisibility()
        {
            if (CardFemaleDetails == null || CmbGender == null)
            {
                return;
            }

            bool isFemale = CmbGender.SelectedIndex == 1;
            CardFemaleDetails.Visibility = isFemale ? Visibility.Visible : Visibility.Collapsed;

            if (!isFemale)
            {
                if (ChkPregnant != null)
                {
                    ChkPregnant.IsChecked = false;
                }

                if (ChkNursing != null)
                {
                    ChkNursing.IsChecked = false;
                }
            }

            UpdatePregnancyMonthVisibility();
        }

        private void UpdatePregnancyMonthVisibility()
        {
            if (PanelPregnancyMonth != null)
            {
                PanelPregnancyMonth.Visibility = ChkPregnant?.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        private List<string> GetSelectedToothIds()
        {
            IEnumerable<string> selectedTeeth = AdultChartControl.Visibility == Visibility.Visible
                ? AdultChartControl.GetSelectedTeeth()
                : ChildChartControl.GetSelectedTeeth();

            return selectedTeeth
                .Distinct(StringComparer.Ordinal)
                .ToList();
        }

        private static string? GetSelectedComboBoxText(ComboBox comboBox)
        {
            return (comboBox.SelectedItem as ComboBoxItem)?.Content?.ToString();
        }

        private static string? NormalizeBloodType(string? bloodType)
        {
            return bloodType == "غير محدد" ? null : bloodType;
        }

        private static string? NullIfWhiteSpace(string? value)
        {
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }

        private static bool TryParseNullableInt(string? value, out int? result)
        {
            result = null;

            if (string.IsNullOrWhiteSpace(value))
            {
                return true;
            }

            if (int.TryParse(value.Trim(), out int parsedValue))
            {
                result = parsedValue;
                return true;
            }

            return false;
        }

        private static bool TryParseMoney(string? value, out decimal result)
        {
            result = 0;

            if (string.IsNullOrWhiteSpace(value))
            {
                return true;
            }

            string trimmedValue = value.Trim();

            return decimal.TryParse(trimmedValue, NumberStyles.Number, CultureInfo.CurrentCulture, out result) ||
                   decimal.TryParse(trimmedValue, NumberStyles.Number, CultureInfo.InvariantCulture, out result);
        }

        private static decimal ParseDecimalOrZero(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return 0;
            }

            string trimmedValue = value.Trim();

            return decimal.TryParse(trimmedValue, NumberStyles.Number, CultureInfo.CurrentCulture, out decimal currentCultureValue)
                ? currentCultureValue
                : decimal.TryParse(trimmedValue, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal invariantValue)
                    ? invariantValue
                    : 0;
        }

        private void BtnAddPrescription_Click(object sender, RoutedEventArgs e)
        {
            string medicineName = TxtPrescriptionMedicine.Text.Trim();
            if (string.IsNullOrWhiteSpace(medicineName))
            {
                MessageBox.Show("يرجى إدخال اسم الدواء قبل إضافته إلى الوصفة.", "الوصفة الطبية", MessageBoxButton.OK, MessageBoxImage.Warning);
                TxtPrescriptionMedicine.Focus();
                return;
            }

            // Storing the drug to dictionary for AutoFill logic
            AddToMedicines(medicineName);

            _prescriptionItems.Add(new PrescriptionItem
            {
                MedicineName = medicineName,
                Dosage = NullIfWhiteSpace(TxtPrescriptionDosage.Text),
                FrequencyHours = NullIfWhiteSpace(TxtPrescriptionFrequency.Text),
                Timing = GetSelectedComboBoxText(CmbPrescriptionTiming),
                Notes = NullIfWhiteSpace(TxtPrescriptionNotes.Text)
            });

            TxtPrescriptionMedicine.Clear();
            TxtPrescriptionDosage.Clear();
            TxtPrescriptionFrequency.Clear();
            TxtPrescriptionNotes.Clear();
            CmbPrescriptionTiming.SelectedIndex = 0;
            PopupMedicineSuggestions.IsOpen = false;
            RefreshPrescriptionList();
        }

        private void BtnRemovePrescription_Click(object sender, RoutedEventArgs e)
        {
            if (LstPrescriptionEntries.SelectedIndex < 0 || LstPrescriptionEntries.SelectedIndex >= _prescriptionItems.Count)
            {
                MessageBox.Show("حدد عنصراً من الوصفة لحذفه.", "الوصفة الطبية", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            _prescriptionItems.RemoveAt(LstPrescriptionEntries.SelectedIndex);
            RefreshPrescriptionList();
        }

        private void BtnSelectImages_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog openFileDialog = new()
            {
                Filter = "Image Files|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.webp",
                Multiselect = true,
                Title = "اختر صور الزيارة"
            };

            if (openFileDialog.ShowDialog() != true)
            {
                return;
            }

            IEnumerable<string> newPaths = openFileDialog.FileNames
                .Where(File.Exists)
                .Distinct(StringComparer.OrdinalIgnoreCase);

            foreach (string path in newPaths)
            {
                if (_selectedImagePaths.Count >= MaxVisitImages)
                {
                    MessageBox.Show($"يمكن إرفاق {MaxVisitImages} صور كحد أقصى لكل زيارة.", "صور الزيارة", MessageBoxButton.OK, MessageBoxImage.Information);
                    break;
                }

                if (_selectedImagePaths.Contains(path, StringComparer.OrdinalIgnoreCase))
                {
                    continue;
                }

                _selectedImagePaths.Add(path);
            }

            RefreshSelectedImagesList();
        }

        private void BtnClearImages_Click(object sender, RoutedEventArgs e)
        {
            _selectedImagePaths.Clear();
            RefreshSelectedImagesList();
        }

        private void RefreshPrescriptionList()
        {
            if (LstPrescriptionEntries == null)
            {
                return;
            }

            LstPrescriptionEntries.ItemsSource = null;
            LstPrescriptionEntries.ItemsSource = _prescriptionItems.Select(FormatPrescriptionItem).ToList();
        }

        private void RefreshSelectedImagesList()
        {
            if (LstSelectedImages == null || TxtImageSelectionHint == null)
            {
                return;
            }

            LstSelectedImages.ItemsSource = null;
            LstSelectedImages.ItemsSource = _selectedImagePaths
                .Select(path => Path.GetFileName(path))
                .ToList();

            TxtImageSelectionHint.Text = _selectedImagePaths.Count == 0
                ? "لا توجد صور محددة بعد"
                : $"تم اختيار {_selectedImagePaths.Count} من {MaxVisitImages} صور";
        }

        private static string FormatPrescriptionItem(PrescriptionItem item)
        {
            List<string> parts = new()
            {
                item.MedicineName
            };

            if (!string.IsNullOrWhiteSpace(item.Dosage))
            {
                parts.Add(item.Dosage);
            }

            if (!string.IsNullOrWhiteSpace(item.FrequencyHours))
            {
                parts.Add($"كل {item.FrequencyHours} ساعة");
            }

            if (!string.IsNullOrWhiteSpace(item.Timing))
            {
                parts.Add(item.Timing);
            }

            if (!string.IsNullOrWhiteSpace(item.Notes))
            {
                parts.Add(item.Notes);
            }

            return string.Join(" | ", parts);
        }

        private static string? SaveVisitImages(string patientName, int visitId, IEnumerable<string> selectedImagePaths)
        {
            List<string> existingFiles = selectedImagePaths
                .Where(File.Exists)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (existingFiles.Count == 0)
            {
                return null;
            }

            string storageRoot = ResolveVisitImagesRoot();
            
            // استخدام اسم المريض بدلاً من رقم الهاتف لاسم المجلد
            string sanitizedName = SanitizePathSegment(patientName);
            string visitFolderName = $"{sanitizedName}_visit_{visitId}_{DateTime.Now.AddHours(7):yyyyMMdd_HHmmss}";
            string visitFolderPath = Path.Combine(storageRoot, visitFolderName);
            Directory.CreateDirectory(visitFolderPath);

            List<string> storedRelativePaths = new();
            int sequence = 1;

            foreach (string sourcePath in existingFiles)
            {
                string extension = Path.GetExtension(sourcePath);
                
                // استخدام التاريخ/الوقت مع أجزاء الثانية (_fff) ورقم تسلسلي لضمان عدم وجود أسماء مكررة أبداً
                string timestamp = DateTime.Now.AddHours(7).ToString("yyyyMMdd_HHmmss_fff");
                string destinationFileName = $"{timestamp}_{sequence:00}{extension}";
                string destinationPath = Path.Combine(visitFolderPath, destinationFileName);

                File.Copy(sourcePath, destinationPath, overwrite: true);
                storedRelativePaths.Add(Path.Combine(visitFolderName, destinationFileName).Replace("\\", "/"));
                sequence++;
            }

            return storedRelativePaths.Count > 0
                ? JsonSerializer.Serialize(storedRelativePaths)
                : null;
        }

        private static string ResolveVisitImagesRoot()
        {
            // توجيه مجلد الصور ليكون داخل المجلد الآمن الخاص بالعيادة
            string appDataFolder = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string clinicFolder = Path.Combine(appDataFolder, "MyClinicApp");
            string imagesFolder = Path.Combine(clinicFolder, "PatientVisitImages");

            // التأكد من أن مجلد الصور موجود، وإلا سيقوم بإنشائه
            if (!Directory.Exists(imagesFolder))
            {
                Directory.CreateDirectory(imagesFolder);
            }

            return imagesFolder;
        }

        private static string SanitizePathSegment(string value)
        {
            char[] invalidChars = Path.GetInvalidFileNameChars();
            return string.Concat(value.Trim().Select(character => invalidChars.Contains(character) ? '_' : character));
        }

        private static VisitHistoryItem MapVisit(Visit visit)
        {
            IReadOnlyList<string> attachmentPaths = ParseAttachmentPaths(visit.AttachedImagePathsJson);
            IReadOnlyList<string> toothIds = visit.ToothRecords?
                .Select(record => record.ToothId?.Trim())
                .Where(toothId => !string.IsNullOrWhiteSpace(toothId))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(toothId => toothId, StringComparer.CurrentCulture)
                .Cast<string>()
                .ToList()
                ?? (IReadOnlyList<string>)Array.Empty<string>();

            IReadOnlyList<PrescriptionDisplayItem> prescriptionItems = ParsePrescriptionItems(visit.PrescriptionJson);
            int attachmentCount = attachmentPaths.Count;
            int toothCount = toothIds.Count;

            string title = Truncate(
                FirstNonEmpty(
                    visit.Diagnosis,
                    visit.Symptoms,
                    "زيارة متابعة"),
                72);

            string details = Truncate(
                FirstNonEmpty(
                    ToLabelValue("خطة العلاج", visit.TreatmentPlanNotes),
                    toothCount > 0 ? $"تم تسجيل {toothCount} أسنان في مخطط الز الزيارة." : null,
                    ToLabelValue("الأعراض", visit.Symptoms),
                    ToLabelValue("الضغط", visit.BloodPressure),
                    "لا توجد ملاحظات إضافية."),
                120);

            return new VisitHistoryItem
            {
                VisitId = visit.Id,
                VisitDate = visit.VisitDate,
                VisitDateText = visit.VisitDate.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture),
                VisitDateTimeText = visit.VisitDate.ToString("dd/MM/yyyy - hh:mm tt", CultureInfo.InvariantCulture),
                Title = title,
                Details = details,
                HasAttachments = attachmentCount > 0,
                AttachmentLabel = attachmentCount > 0 ? BuildAttachmentLabel(attachmentCount) : string.Empty,
                PregnancySummary = BuildPregnancySummary(visit),
                BloodPressureText = NormalizeOrFallback(visit.BloodPressure, "--"),
                HeartRateText = FormatMetric(visit.HeartRate, "BPM"),
                TemperatureText = FormatMetric(visit.Temperature, "C°"),
                RespiratoryRateText = FormatMetric(visit.RespiratoryRate, "/min"),
                WeightText = FormatMetric(visit.Weight, "كغ"),
                HeightText = FormatMetric(visit.Height, "سم"),
                BmiText = ComputeBmiText(visit.Weight, visit.Height),
                SymptomsText = NormalizeOrFallback(visit.Symptoms, "لا توجد أعراض مسجلة"),
                DiagnosisText = NormalizeOrFallback(visit.Diagnosis, "لا يوجد تشخيص مسجل"),
                ChartModeText = FormatChartMode(visit.ChartMode),
                ToothIds = toothIds,
                TreatmentPlanText = NormalizeOrFallback(visit.TreatmentPlanNotes, "لا توجد خطة علاج أو ملاحظات إضافية."),
                PrescriptionItems = prescriptionItems,
                AttachmentPaths = attachmentPaths,
                SelectedTreatments = ParseSelectedTreatments(visit.SelectedTreatmentsJson)
            };
        }

        private static IReadOnlyList<SelectedTreatment> ParseSelectedTreatments(string? json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return Array.Empty<SelectedTreatment>();
            try
            {
                List<SelectedTreatment>? items = JsonSerializer.Deserialize<List<SelectedTreatment>>(json);
                return items?.Where(t => !string.IsNullOrWhiteSpace(t.TreatmentName)).ToList()
                       ?? (IReadOnlyList<SelectedTreatment>)Array.Empty<SelectedTreatment>();
            }
            catch (JsonException)
            {
                return Array.Empty<SelectedTreatment>();
            }
        }

        private static IReadOnlyList<VisitAttachmentItem> LoadAttachmentItems(IReadOnlyList<string> attachmentPaths)
        {
            return attachmentPaths
                .Select(CreateAttachmentItem)
                .Where(item => item is not null)
                .Cast<VisitAttachmentItem>()
                .ToList();
        }

        private static VisitAttachmentItem? CreateAttachmentItem(string filePath)
        {
            try
            {
                if (!File.Exists(filePath))
                {
                    return null;
                }

                BitmapImage image = new();
                image.BeginInit();
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.DecodePixelWidth = 320; 
                image.CreateOptions = BitmapCreateOptions.IgnoreColorProfile; 
                image.UriSource = new Uri(filePath, UriKind.Absolute);
                image.EndInit();
                image.Freeze();

                return new VisitAttachmentItem
                {
                    FilePath = filePath,
                    FileName = Path.GetFileName(filePath),
                    Thumbnail = image
                };
            }
            catch
            {
                return null;
            }
        }

        private static string BuildSmokingSummary(PatientLookupSnapshot patient)
        {
            if (!patient.IsSmoker)
            {
                return "غير مدخن";
            }

            List<string> parts = new() { "مدخن" };

            if (!string.IsNullOrWhiteSpace(patient.SmokingType))
            {
                parts.Add(patient.SmokingType.Trim());
            }

            if (!string.IsNullOrWhiteSpace(patient.SmokingFrequency))
            {
                parts.Add(patient.SmokingFrequency.Trim());
            }

            return string.Join(" - ", parts);
        }

        private static string BuildPregnancySummary(Visit visit)
        {
            List<string> parts = new();

            if (visit.IsPregnant)
            {
                parts.Add(visit.PregnancyMonth.HasValue ? $"حامل - الشهر {visit.PregnancyMonth.Value}" : "حامل");
            }

            if (visit.IsNursing)
            {
                parts.Add("مرضعة");
            }

            return parts.Count == 0 ? "غير مسجل" : string.Join(" - ", parts);
        }

        private static string FormatChartMode(string? chartMode)
        {
            return string.Equals(chartMode, "Child", StringComparison.OrdinalIgnoreCase)
                ? "أطفال (Child)"
                : "بالغين (Adult)";
        }

        private static string BuildAttachmentLabel(int attachmentCount)
        {
            return attachmentCount == 1 ? "1 مرفق" : $"{attachmentCount} مرفقات";
        }

        private static string FormatMetric(string? value, string suffix)
        {
            return string.IsNullOrWhiteSpace(value)
                ? "--"
                : $"{value.Trim()} {suffix}".Trim();
        }

        private static string ComputeBmiText(string? weightValue, string? heightValue)
        {
            if (!TryParseDecimal(weightValue, out decimal weight) ||
                !TryParseDecimal(heightValue, out decimal height) ||
                weight <= 0 ||
                height <= 0)
            {
                return "--";
            }

            decimal meters = height > 10 ? height / 100m : height;
            if (meters <= 0)
            {
                return "--";
            }

            decimal bmi = weight / (meters * meters);
            return bmi.ToString("0.0", CultureInfo.InvariantCulture);
        }

        private static bool TryParseDecimal(string? value, out decimal result)
        {
            result = 0;

            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            string trimmedValue = value.Trim();

            return decimal.TryParse(trimmedValue, NumberStyles.Number, CultureInfo.CurrentCulture, out result) ||
                   decimal.TryParse(trimmedValue, NumberStyles.Number, CultureInfo.InvariantCulture, out result);
        }

        private static IReadOnlyList<PrescriptionDisplayItem> ParsePrescriptionItems(string? prescriptionJson)
        {
            if (string.IsNullOrWhiteSpace(prescriptionJson))
            {
                return Array.Empty<PrescriptionDisplayItem>();
            }

            try
            {
                List<PrescriptionItem>? items = JsonSerializer.Deserialize<List<PrescriptionItem>>(prescriptionJson);
                if (items is null)
                {
                    return Array.Empty<PrescriptionDisplayItem>();
                }

                return items
                    .Where(item => !string.IsNullOrWhiteSpace(item.MedicineName))
                    .Select(item => new PrescriptionDisplayItem
                    {
                        MedicineName = item.MedicineName.Trim(),
                        InstructionText = BuildPrescriptionInstruction(item)
                    })
                    .ToList();
            }
            catch (JsonException)
            {
                return Array.Empty<PrescriptionDisplayItem>();
            }
        }

        private static string BuildPrescriptionInstruction(PrescriptionItem item)
        {
            List<string> parts = new();

            if (!string.IsNullOrWhiteSpace(item.Dosage))
            {
                parts.Add(item.Dosage.Trim());
            }

            if (!string.IsNullOrWhiteSpace(item.FrequencyHours))
            {
                parts.Add($"كل {item.FrequencyHours.Trim()} ساعات");
            }

            if (!string.IsNullOrWhiteSpace(item.Timing))
            {
                parts.Add(item.Timing.Trim());
            }

            if (!string.IsNullOrWhiteSpace(item.Notes))
            {
                parts.Add(item.Notes.Trim());
            }

            return parts.Count == 0 ? "بدون تعليمات إضافية" : string.Join(" - ", parts);
        }

        private static IReadOnlyList<string> ParseAttachmentPaths(string? attachedImagePathsJson)
        {
            if (string.IsNullOrWhiteSpace(attachedImagePathsJson))
            {
                return Array.Empty<string>();
            }

            try
            {
                List<string>? attachments = JsonSerializer.Deserialize<List<string>>(attachedImagePathsJson);
                if (attachments is null)
                {
                    return Array.Empty<string>();
                }

                return attachments
                    .Where(path => !string.IsNullOrWhiteSpace(path))
                    .Select(path => ResolveVisitImagePath(path.Trim()))
                    .ToList();
            }
            catch (JsonException)
            {
                return Array.Empty<string>();
            }
        }

        private static string ResolveVisitImagePath(string path)
        {
            if (Path.IsPathRooted(path))
            {
                return path;
            }

            string normalizedRelativePath = path
                .Replace('/', Path.DirectorySeparatorChar)
                .Replace('\\', Path.DirectorySeparatorChar);

            return Path.GetFullPath(Path.Combine(ResolveVisitImagesRoot(), normalizedRelativePath));
        }

        private static string NormalizeOrFallback(string? value, string fallback)
        {
            return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
        }

        private static string NormalizeBloodTypeLabel(string? bloodType)
        {
            return string.IsNullOrWhiteSpace(bloodType) ? "غير محدد" : bloodType.Trim();
        }

        private static string ToLabelValue(string label, string? value)
        {
            return string.IsNullOrWhiteSpace(value) ? string.Empty : $"{label}: {value.Trim()}";
        }

        private static string FirstNonEmpty(params string?[] values)
        {
            foreach (string? value in values)
            {
                if (!string.IsNullOrWhiteSpace(value))
                {
                    return value.Trim();
                }
            }

            return string.Empty;
        }

        private static string Truncate(string value, int maxLength)
        {
            if (value.Length <= maxLength)
            {
                return value;
            }

            return value[..(maxLength - 1)].TrimEnd() + "...";
        }

        private void ResetForm()
        {
            TxtPhoneNumber.Clear();
            TxtFullName.Clear();
            TxtAge.Text = "0";
            CmbGender.SelectedIndex = 0;
            CmbBloodType.SelectedIndex = 0;

            TxtCurrentCost.Text = "0";
            TxtTodayPaid.Text = "0";
            TxtRemainingAmount.Text = "0";

            ChkSmoker.IsChecked = false;
            TxtSmokingType.Clear();
            TxtSmokingFrequency.Clear();

            ChkPregnant.IsChecked = false;
            ChkNursing.IsChecked = false;
            CmbPregnancyMonth.SelectedIndex = 0;

            TxtAllergies.Clear();
            TxtChronicDiseases.Clear();

            TxtBloodPressure.Clear();
            TxtHeartRate.Clear();
            TxtTemperature.Clear();
            TxtRespiratoryRate.Clear();
            TxtWeight.Clear();
            TxtHeight.Clear();

            TxtSymptoms.Clear();
            TxtDiagnosis.Clear();
            TxtTreatmentPlanNotes.Clear();

            TxtPrescriptionMedicine.Clear();
            TxtPrescriptionDosage.Clear();
            TxtPrescriptionFrequency.Clear();
            TxtPrescriptionNotes.Clear();
            CmbPrescriptionTiming.SelectedIndex = 0;
            PopupMedicineSuggestions.IsOpen = false;
            _prescriptionItems.Clear();
            _selectedImagePaths.Clear();
            _selectedPatientLookup = null;
            ClearPatientSuggestions();
            ClearPreviousVisits();

            AdultChartControl.ClearSelection();
            ChildChartControl.ClearSelection();
            BtnAdult_Click(this, new RoutedEventArgs());

            UpdateRemainingVisitAmount();
            UpdateFemaleDetailsVisibility();
            UpdatePregnancyMonthVisibility();
            RefreshPrescriptionList();
            RefreshSelectedImagesList();
        }

        private sealed class PatientLookupSnapshot
        {
            public int Id { get; init; }
            public string PhoneNumber { get; init; } = string.Empty;
            public string? FullName { get; init; }
            public int? Age { get; init; }
            public string? Gender { get; init; }
            public string? BloodType { get; init; }
            public bool IsSmoker { get; init; }
            public string? SmokingType { get; init; }
            public string? SmokingFrequency { get; init; }
            public string? Allergies { get; init; }
            public string? ChronicDiseases { get; init; }
            public int VisitCount { get; init; }
            public DateTime? LatestVisitDate { get; init; }

            public string DisplayName => string.IsNullOrWhiteSpace(FullName) ? "بدون اسم" : FullName.Trim();
            public string AgeLabel => Age.HasValue ? $"{Age.Value} سنة" : "العمر غير محدد";
            public string GenderLabel => string.IsNullOrWhiteSpace(Gender) ? "الجنس غير محدد" : Gender.Trim();
            public string VisitCountLabel => VisitCount == 1 ? "1 زيارة" : $"{VisitCount} زيارات";
            public string LatestVisitLabel => LatestVisitDate.HasValue
                ? $"آخر زيارة {LatestVisitDate.Value.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture)}"
                : "لا توجد زيارات سابقة";
        }

        private sealed class PatientLookupSuggestion
        {
            public PatientLookupSnapshot Snapshot { get; init; } = null!;
            public int Score { get; init; }
            public string DisplayName { get; init; } = string.Empty;
            public string SecondaryLine { get; init; } = string.Empty;
            public string MetaLine { get; init; } = string.Empty;
            public string MatchReason { get; init; } = "مطابقة";
        }

        private void NavigateToDashboard()
        {
            if (Window.GetWindow(this) is MainWindow mainWindow)
            {
                mainWindow.InvalidatePatientRecords();
                mainWindow.InvalidateFinancialRecords();
                mainWindow.ShowDashboard();
            }
        }
    }
}
