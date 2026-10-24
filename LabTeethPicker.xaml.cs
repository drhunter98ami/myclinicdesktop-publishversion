using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace MyClinic;

/// <summary>Uses the same anatomical controls as Add Patient, including both dentitions.</summary>
public partial class LabTeethPicker : UserControl
{
    public LabTeethPicker(IEnumerable<string> selected)
    {
        InitializeComponent();
        foreach (string tooth in selected) { AdultChart.SelectTooth(tooth); ChildChart.SelectTooth(tooth); }
        AddHandler(ToggleButton.CheckedEvent, new RoutedEventHandler(Selection_Changed));
        AddHandler(ToggleButton.UncheckedEvent, new RoutedEventHandler(Selection_Changed));
        ShowAdult(AdultChart.GetSelectedTeeth().Count > 0 || ChildChart.GetSelectedTeeth().Count == 0); UpdateSummary();
    }
    public IReadOnlyCollection<string> SelectedTeeth => AdultChart.GetSelectedTeeth().Concat(ChildChart.GetSelectedTeeth()).ToArray();
    void ShowAdult(bool adult)
    {
        AdultChart.Visibility = adult ? Visibility.Visible : Visibility.Collapsed; ChildChart.Visibility = adult ? Visibility.Collapsed : Visibility.Visible;
        AdultButton.FontWeight = adult ? FontWeights.Bold : FontWeights.Normal; ChildButton.FontWeight = adult ? FontWeights.Normal : FontWeights.Bold;
        AdultButton.BorderThickness = new(adult ? 2 : 1); ChildButton.BorderThickness = new(adult ? 1 : 2);
    }
    void Adult_Click(object sender, RoutedEventArgs e) => ShowAdult(true);
    void Child_Click(object sender, RoutedEventArgs e) => ShowAdult(false);
    void Clear_Click(object sender, RoutedEventArgs e) { AdultChart.ClearSelection(); ChildChart.ClearSelection(); UpdateSummary(); }
    void Selection_Changed(object sender, RoutedEventArgs e) => UpdateSummary();
    void UpdateSummary() => SelectionSummary.Text = SelectedTeeth.Count == 0 ? "اختر الأسنان من الرسم." : "الأسنان المختارة: " + string.Join("، ", SelectedTeeth);
}
