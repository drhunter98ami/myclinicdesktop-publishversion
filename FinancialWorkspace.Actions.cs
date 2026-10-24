using Microsoft.EntityFrameworkCore;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace MyClinic;

public partial class FinancialRecordsView
{
    private void FocusExpense_Click(object sender, RoutedEventArgs e) { FinanceTabs.SelectedIndex = 1; ExpenseEntrySection.IsExpanded = true; CmbExpenseCategory.Focus(); }
    private void FocusUpcoming_Click(object sender, RoutedEventArgs e) { FinanceTabs.SelectedIndex = 2; UpcomingEntrySection.IsExpanded = true; CmbUpcomingCategory.Focus(); }
    private void EditExpense_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not FinanceExpenseRow row) return;
        ExpenseEntrySection.IsExpanded = true;
        _expenseEditId = row.Id; CmbExpenseCategory.SelectedItem = row.Category; TxtExpenseDetails.Text = StripCategory(row.Description, row.Category);
        TxtExpenseAmount.Text = row.Amount.ToString(CultureInfo.InvariantCulture); DpExpenseDate.SelectedDate = row.Date.Date;
        ExpenseReasonHost.Visibility = BtnCancelExpenseEdit.Visibility = Visibility.Visible; BtnSaveExpense.Content = "حفظ التصحيح"; TxtExpenseReason.Focus();
    }
    private static string StripCategory(string description, string category) => description.StartsWith(category + " - ", StringComparison.Ordinal) ? description[(category.Length + 3)..] : description == category ? "" : description;
    private void CancelExpenseEdit_Click(object sender, RoutedEventArgs e) => ResetExpenseForm();
    private void ResetExpenseForm()
    {
        _expenseEditId = null; TxtExpenseDetails.Clear(); TxtExpenseAmount.Clear(); TxtExpenseReason.Clear(); CmbExpenseCategory.SelectedIndex = -1;
        DpExpenseDate.SelectedDate = AppClock.Today; ExpenseReasonHost.Visibility = BtnCancelExpenseEdit.Visibility = Visibility.Collapsed; BtnSaveExpense.Content = "إضافة مصروف";
    }
    private bool ValidAmount(TextBox input, out decimal amount)
    {
        if (FinanceMoney.TryParse(input.Text, out amount) && amount > 0) return true;
        ValidationAssist.Show(input, "أدخل مبلغاً موجباً بمنزلتين عشريتين كحد أقصى."); return false;
    }
    private async void SaveExpense_Click(object sender, RoutedEventArgs e)
    {
        if (_busy || !ValidAmount(TxtExpenseAmount, out decimal amount)) return;
        if (CmbExpenseCategory.SelectedItem is not string category) { ValidationAssist.Show(CmbExpenseCategory, "اختر الفئة."); return; }
        if (!ValidPastDate(DpExpenseDate, out var date)) return;
        if (_expenseEditId.HasValue && string.IsNullOrWhiteSpace(TxtExpenseReason.Text)) { ValidationAssist.Show(TxtExpenseReason, "أدخل سبب التصحيح."); return; }
        int? editId = _expenseEditId; string details = TxtExpenseDetails.Text.Trim(), reason = TxtExpenseReason.Text.Trim();
        if (editId.HasValue && !await ConfirmAsync("تصحيح مصروف", $"{details}\nالقيمة الجديدة: {FinanceMoney.Format(amount)}\nسيُحفظ التصحيح مع القيم السابقة وسببه في سجل العمليات.")) return;
        await MutateAsync(async db =>
        {
            int id = await new FinancialTransactions(db, LoginSessionStore.CurrentUsername).SaveExpenseAsync(editId, category, details, amount, date, reason);
            ResetExpenseForm(); await SavedAsync("تم حفظ المصروف."); FinanceMotion.Highlight(ExpenseItemsControl, id);
        });
    }
    private void EditUpcoming_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not FinanceUpcomingRow row || !row.Pending) return;
        UpcomingEntrySection.IsExpanded = true;
        _upcomingEditId = row.Id; CmbUpcomingCategory.SelectedItem = row.Category; TxtUpcomingDescription.Text = StripCategory(row.Description, row.Category);
        TxtUpcomingAmount.Text = row.Amount.ToString(CultureInfo.InvariantCulture); DpUpcomingDue.SelectedDate = row.Due;
        BtnSaveUpcoming.Content = "حفظ التعديل"; BtnCancelUpcomingEdit.Visibility = Visibility.Visible; TxtUpcomingDescription.Focus();
    }
    private void CancelUpcomingEdit_Click(object sender, RoutedEventArgs e) => ResetUpcomingForm();
    private void ResetUpcomingForm()
    {
        _upcomingEditId = null; CmbUpcomingCategory.SelectedIndex = -1; TxtUpcomingDescription.Clear(); TxtUpcomingAmount.Clear();
        DpUpcomingDue.SelectedDate = null; BtnSaveUpcoming.Content = "إضافة مستحق"; BtnCancelUpcomingEdit.Visibility = Visibility.Collapsed;
    }
    private async void SaveUpcoming_Click(object sender, RoutedEventArgs e)
    {
        if (_busy || !ValidAmount(TxtUpcomingAmount, out var amount)) return;
        if (CmbUpcomingCategory.SelectedItem is not string category) { ValidationAssist.Show(CmbUpcomingCategory, "اختر الفئة."); return; }
        if (string.IsNullOrWhiteSpace(TxtUpcomingDescription.Text)) { ValidationAssist.Show(TxtUpcomingDescription, "أدخل التفاصيل."); return; }
        if (!TryReadDate(DpUpcomingDue, false, out var due)) return;
        int? editId = _upcomingEditId; string details = TxtUpcomingDescription.Text.Trim();
        await MutateAsync(async db =>
        {
            int id = await new FinancialTransactions(db, LoginSessionStore.CurrentUsername).SaveUpcomingAsync(editId, category, details, amount, due);
            ResetUpcomingForm(); await SavedAsync("تم حفظ المستحق."); FinanceMotion.Highlight(UpcomingItemsControl, id);
        });
    }
    private async void PayUpcoming_Click(object sender, RoutedEventArgs e) => await CompleteUpcomingAsync(sender, true);
    private async void CancelUpcoming_Click(object sender, RoutedEventArgs e) => await CompleteUpcomingAsync(sender, false);
    private async Task CompleteUpcomingAsync(object sender, bool pay)
    {
        if (_busy || (sender as FrameworkElement)?.DataContext is not FinanceUpcomingRow row || !row.Pending) return;
        string text = $"{row.Description}\n{FinanceMoney.Format(row.Amount)}\n" + (pay ? "سيُسجّل المبلغ كمصروف بتاريخ اليوم وتبقى الدفعة في سجل المسددات." : "ستبقى الدفعة في سجل الملغاة ولن يُسجّل مصروف.");
        if (!await ConfirmAsync(pay ? "تسجيل سداد مستحق" : "إلغاء مستحق", text)) return;
        await MutateAsync(async db =>
        {
            bool changed = await new FinancialTransactions(db, LoginSessionStore.CurrentUsername).CompleteUpcomingAsync(row.Id, pay, pay ? "" : "إلغاء من شاشة المستحقات");
            await SavedAsync(changed ? (pay ? "تم السداد وإضافة المصروف." : "تم الإلغاء مع الاحتفاظ بالسجل.") : "تمت معالجة هذا المستحق مسبقاً؛ حُدّثت القائمة.");
            if (changed) FinanceMotion.Fade(UpcomingItemsControl);
        });
    }
    private void OpenPatient_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is FinanceVisitRow visit) OpenPatient(visit.PatientId);
        else if ((sender as FrameworkElement)?.DataContext is FinanceAccount account) OpenPatient(account.PatientId);
    }
    private void OpenPayment_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not FinanceAccount account) return;
        OpenPatient(account.PatientId); TxtAddPaymentAmount.Focus();
    }
    private void SearchPatient_Click(object sender, RoutedEventArgs e) { OpenPatient(null); TxtPatientSearchInput.Focus(); }
    private void OpenPatient(int? patientId)
    {
        _patientReturnFocus = Keyboard.FocusedElement;
        PatientPanel.Visibility = Visibility.Visible; ResizePatientPanel(); FinanceMotion.Slide(PatientPanel);
        if (patientId.HasValue) SelectPatient(patientId.Value);
    }
    private void SelectPatient(int id, bool clearInputs = true)
    {
        _selectedAccount = _accounts.FirstOrDefault(a => a.PatientId == id);
        if (_selectedAccount == null) { ClosePatient_Click(this, new()); return; }
        var account = _selectedAccount;
        PatientIdentity.Text = $"{account.PatientName} · {account.Phone}";
        PatientIdentity.Inlines.Clear(); PatientIdentity.Inlines.Add(new System.Windows.Documents.Run(account.PatientName));
        PatientIdentity.Inlines.Add(new System.Windows.Documents.Run(" · "));
        PatientIdentity.Inlines.Add(new System.Windows.Documents.Run(account.Phone) { FlowDirection = FlowDirection.LeftToRight });
        TxtPatientTotalCost.Text = FinanceMoney.Format(account.Cost); TxtPatientTotalPaid.Text = FinanceMoney.Format(account.Paid);
        TxtPatientTotalForgiven.Text = FinanceMoney.Format(account.Forgiven); TxtPatientTotalRemaining.Text = BalanceLabel(account.Balance);
        TxtPatientTotalRemaining.SetResourceReference(System.Windows.Controls.TextBlock.ForegroundProperty, account.Balance > 0 ? "FinanceNegative" : "FinancePositive");
        SetItems(PatientHistoryItemsControl, account.Rows.ToList(), "patient|" + id);
        EmptyPatientHistoryText.Visibility = account.Rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        EmptyPatientHistoryText.Text = "لا توجد عمليات سابقة. يمكنك تسجيل دفعة أدناه.";
        PatientEntryForm.IsEnabled = true;
        if (clearInputs) { TxtAddPaymentAmount.Clear(); TxtPaymentReason.Clear(); DpPaymentDate.SelectedDate = AppClock.Today; }
        LstPatientSuggestions.Visibility = Visibility.Collapsed; UpdatePaymentPreview();
    }
    private void ClosePatient_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        PatientPanel.Visibility = Visibility.Collapsed; PatientColumn.Width = new(0); MainShell.Visibility = Visibility.Visible;
        _searchDelay.Stop(); _patientReturnFocus?.Focus();
    }
    private void PatientSearch_Changed(object sender, TextChangedEventArgs e)
    { if (_initialized) { _searchDelay.Stop(); _searchDelay.Start(); } }
    private sealed record PatientSuggestion(int Id, string Label);
    private void UpdateSuggestions()
    {
        string query = PatientRecordSearch.Normalize(TxtPatientSearchInput.Text);
        var matches = query.Length == 0 ? new List<PatientSuggestion>() : _patients.Where(p => PatientRecordSearch.Normalize($"{p.FullName} {p.PhoneNumber}").Contains(query)).OrderBy(p => p.FullName).Take(15).Select(p => new PatientSuggestion(p.Id, $"{p.FullName} · {p.PhoneNumber}")).ToList();
        LstPatientSuggestions.ItemsSource = matches; LstPatientSuggestions.Visibility = query.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        PatientSearchHint.Text = query.Length == 0 ? "" : matches.Count == 0 ? "لا يوجد مريض مطابق للاسم أو الهاتف." : $"{matches.Count} نتيجة · اختر المريض لفتح حسابه";
    }
    private void PatientSuggestion_Selected(object sender, SelectionChangedEventArgs e)
    { if (LstPatientSuggestions.SelectedItem is PatientSuggestion patient) { _searchDelay.Stop(); SelectPatient(patient.Id); PatientSearchHint.Text = ""; TxtAddPaymentAmount.Focus(); } }
    private void PaymentPreview_Changed(object sender, TextChangedEventArgs e) { if (_initialized) UpdatePaymentPreview(); }
    private void UpdatePaymentPreview()
    {
        if (_selectedAccount == null) { PaymentPreview.Text = ""; return; }
        FinanceMoney.TryParse(TxtAddPaymentAmount.Text, out var amount);
        PaymentPreview.Text = $"قبل العملية: {BalanceLabel(_selectedAccount.Balance)}  ←  بعد العملية: {BalanceLabel(_selectedAccount.Balance - Math.Max(0, amount))}";
    }
    private static string BalanceLabel(decimal balance) => balance < 0 ? $"رصيد دائن للمريض: {FinanceMoney.Format(-balance)}" : $"مستحق: {FinanceMoney.Format(balance)}";
    private async void RecordPayment_Click(object sender, RoutedEventArgs e) => await RecordPatientEntryAsync(false);
    private async void RecordForgiveness_Click(object sender, RoutedEventArgs e) => await RecordPatientEntryAsync(true);
    private async Task RecordPatientEntryAsync(bool forgiveness)
    {
        if (_busy || _selectedAccount == null || !ValidAmount(TxtAddPaymentAmount, out var amount)) return;
        if (!ValidPastDate(DpPaymentDate, out var date)) return;
        if (forgiveness && string.IsNullOrWhiteSpace(TxtPaymentReason.Text)) { ValidationAssist.Show(TxtPaymentReason, "سبب المسامحة مطلوب."); return; }
        if (forgiveness && amount > Math.Max(0, _selectedAccount.Balance)) { ValidationAssist.Show(TxtAddPaymentAmount, "المسامحة لا تتجاوز المبلغ المستحق."); return; }
        int patientId = _selectedAccount.PatientId; decimal expectedBalance = _selectedAccount.Balance; string reason = TxtPaymentReason.Text.Trim();
        if (!await ConfirmAsync(forgiveness ? "تسجيل مسامحة" : "تسجيل دفعة", $"{_selectedAccount.PatientName}\n{FinanceMoney.Format(amount)} · {date:dd/MM/yyyy}\n{BalanceLabel(_selectedAccount.Balance)}\nبعد العملية: {BalanceLabel(_selectedAccount.Balance - amount)}\n{reason}")) return;
        await MutateAsync(async db =>
        {
            int id = await new FinancialTransactions(db, LoginSessionStore.CurrentUsername).AddPatientEntryAsync(patientId, amount, forgiveness, date, reason, expectedBalance);
            TxtAddPaymentAmount.Clear(); TxtPaymentReason.Clear();
            await SavedAsync(forgiveness ? "تم تسجيل المسامحة." : "تم تسجيل الدفعة.");
            FinanceMotion.Highlight(PatientHistoryItemsControl, id); FinanceMotion.Highlight(IncomeItemsControl, id);
        });
    }
    private bool ValidPastDate(DatePicker picker, out DateTime date)
    {
        date = default;
        if (!TryReadDate(picker, true, out var selected)) return false;
        date = selected!.Value;
        if (date == default || date > AppClock.Today) { ValidationAssist.Show(picker, "اختر تاريخاً صحيحاً حتى اليوم."); return false; }
        date = date == AppClock.Today ? AppClock.Now : date; return true;
    }
    private bool TryReadDate(DatePicker picker, bool required, out DateTime? date)
    {
        date = null;
        string text = picker.Text.Trim();
        if (text.Length == 0 && !required) return true;
        text = string.Concat(text.Select(c => c is >= '٠' and <= '٩' ? (char)('0' + c - '٠') : c is >= '۰' and <= '۹' ? (char)('0' + c - '۰') : c));
        if (DateTime.TryParse(text, CultureInfo.GetCultureInfo("en-GB"), DateTimeStyles.AllowWhiteSpaces, out var parsed))
        { date = parsed.Date; return true; }
        ValidationAssist.Show(picker, "اختر تاريخاً صحيحاً بالتنسيق يوم/شهر/سنة."); return false;
    }
    private async Task MutateAsync(Func<AppDbContext, Task> action)
    {
        if (_busy) return;
        _busy = true; MainShell.IsEnabled = PatientPanel.IsEnabled = false; ShowStatus("جارٍ الحفظ…");
        try { using var db = new AppDbContext(); await action(db); }
        catch (ArgumentException ex) { ShowStatus(ex.Message, true); }
        catch (InvalidOperationException ex) { ShowStatus(ex.Message, true, true); }
        catch (Exception) { ShowStatus("تعذر حفظ العملية. بقيت القيم المدخلة لتتمكن من إعادة المحاولة.", true, true); }
        finally { _busy = false; MainShell.IsEnabled = PatientPanel.IsEnabled = true; }
    }
    private async Task SavedAsync(string message)
    {
        RequestRefresh(); await EnsureDataCurrentAsync();
        ShowStatus(_loadError ? message + " تعذر تحديث العرض؛ أعد تحميله قبل المتابعة." : message, _loadError, _loadError);
        UiFeedback.Notify(this, message);
        GlobalEvents.OnFinancialRecordAdded -= FinancialChanged; GlobalEvents.OnPatientRecordAdded -= FinancialChanged;
        try { GlobalEvents.NotifyFinancialRecordAdded(); GlobalEvents.NotifyPatientRecordAdded(); }
        finally { if (IsLoaded) { GlobalEvents.OnFinancialRecordAdded += FinancialChanged; GlobalEvents.OnPatientRecordAdded += FinancialChanged; } }
    }
    private void ShowStatus(string message, bool error = false, bool retry = false)
    {
        StatusText.Text = message; StatusHost.Visibility = Visibility.Visible; RetryButton.Visibility = retry ? Visibility.Visible : Visibility.Collapsed;
        System.Windows.Automation.AutomationProperties.SetLiveSetting(StatusText, System.Windows.Automation.AutomationLiveSetting.Polite);
        System.Windows.Automation.Peers.UIElementAutomationPeer.CreatePeerForElement(StatusText)?.RaiseAutomationEvent(System.Windows.Automation.Peers.AutomationEvents.LiveRegionChanged);
        StatusHost.SetResourceReference(Border.BorderBrushProperty, error ? "SettingsDeleteBtnBorder" : "SettingsCardBorder");
    }
    private async void Retry_Click(object sender, RoutedEventArgs e) { RequestRefresh(); await EnsureDataCurrentAsync(); }
    private Task<bool> ConfirmAsync(string title, string text)
    {
        if (_confirmation != null) return Task.FromResult(false);
        _confirmationFocus = Keyboard.FocusedElement; _confirmation = new(TaskCreationOptions.RunContinuationsAsynchronously);
        ConfirmationTitle.Text = title; ConfirmationText.Text = text; ConfirmationOverlay.Visibility = Visibility.Visible;
        MainShell.IsEnabled = PatientPanel.IsEnabled = false; FinanceMotion.Fade(ConfirmationOverlay); ConfirmCancelButton.Focus(); return _confirmation.Task;
    }
    private void FinishConfirmation(bool accepted)
    {
        var pending = _confirmation; _confirmation = null; ConfirmationOverlay.Visibility = Visibility.Collapsed;
        MainShell.IsEnabled = PatientPanel.IsEnabled = !_busy; _confirmationFocus?.Focus(); pending?.TrySetResult(accepted);
    }
    private void ConfirmAccept_Click(object sender, RoutedEventArgs e) => FinishConfirmation(true);
    private void ConfirmCancel_Click(object sender, RoutedEventArgs e) => FinishConfirmation(false);
    private void Finance_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        { if (_confirmation != null) FinishConfirmation(false); else if (PatientPanel.Visibility == Visibility.Visible) ClosePatient_Click(this, new()); e.Handled = true; }
        else if (e.Key == Key.F && Keyboard.Modifiers == ModifierKeys.Control) { WorkspaceSearch.Focus(); WorkspaceSearch.SelectAll(); e.Handled = true; }
        else if (e.Key == Key.Down && TxtPatientSearchInput.IsKeyboardFocused && LstPatientSuggestions.Items.Count > 0)
        { LstPatientSuggestions.SelectedIndex = 0; e.Handled = true; }
    }
    private void Finance_SizeChanged(object sender, SizeChangedEventArgs e) { if (_initialized) { ResizePatientPanel(); UpdateRangeButtons(); } }
    private void ResizePatientPanel()
    {
        if (PatientPanel.Visibility != Visibility.Visible) return;
        bool docked = ActualWidth >= 1250; PatientColumn.Width = new(docked ? Math.Min(600, ActualWidth * 0.44) : ActualWidth);
        PatientSummary.Columns = (docked ? Math.Min(600, ActualWidth * 0.44) : ActualWidth) > 740 ? 4 : 2;
        MainShell.Visibility = docked ? Visibility.Visible : Visibility.Collapsed;
    }
}
