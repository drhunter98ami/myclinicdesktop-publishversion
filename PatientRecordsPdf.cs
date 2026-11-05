using Microsoft.Win32;
using System.IO;
using System.Windows;

namespace MyClinic;

public partial class PatientRecordsView
{
    private void ExportMedicalHistory_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedPatient is null) return;
        if (_visitsLoading || RetryVisitsButton.Visibility == Visibility.Visible)
        {
            MessageBox.Show("أعد تحميل سجل الزيارات بنجاح قبل تصدير الملف الطبي.", "تصدير الملف الطبي", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dialog = new SaveFileDialog
        {
            Title = "تصدير الملف الطبي للمريض",
            FileName = $"الملف-الطبي-{_selectedPatient.Id}-{AppClock.Today:yyyyMMdd}.pdf",
            Filter = "ملف PDF (*.pdf)|*.pdf",
            AddExtension = true,
            OverwritePrompt = true
        };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;

        try
        {
            File.WriteAllBytes(dialog.FileName, ClinicPdfReports.PatientHistory(_selectedPatient, _allPatientVisits));
            MessageBox.Show("تم تصدير الملف الطبي بنجاح.", "تصدير الملف الطبي", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"تعذر تصدير الملف الطبي حالياً.\n{ex.Message}", "تصدير الملف الطبي", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
