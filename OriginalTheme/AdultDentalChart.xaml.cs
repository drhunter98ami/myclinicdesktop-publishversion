using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace MyClinic.OriginalTheme
{
    public partial class AdultDentalChart : UserControl
    {
        public AdultDentalChart()
        {
            InitializeComponent();
        }

        public HashSet<string> SelectedTeeth => new(GetSelectedTeeth());

        public IReadOnlyCollection<string> GetSelectedTeeth()
        {
            return FindVisualChildren<ToggleButton>(this)
                .Where(button => button.IsChecked == true)
                .Select(button => button.Content?.ToString())
                .Where(content => !string.IsNullOrWhiteSpace(content))
                .Cast<string>()
                .ToArray();
        }

        public bool SelectTooth(string toothId)
        {
            ToggleButton? toothButton = FindVisualChildren<ToggleButton>(this)
                .FirstOrDefault(button => string.Equals(button.Content?.ToString(), toothId, StringComparison.Ordinal));
            if (toothButton is null)
                return false;

            toothButton.IsChecked = true;
            return true;
        }

        public bool DeselectTooth(string toothId)
        {
            ToggleButton? toothButton = FindVisualChildren<ToggleButton>(this)
                .FirstOrDefault(button => string.Equals(button.Content?.ToString(), toothId, StringComparison.Ordinal));
            if (toothButton is null)
                return false;

            toothButton.IsChecked = false;
            return true;
        }

        public void ClearSelection()
        {
            foreach (ToggleButton button in FindVisualChildren<ToggleButton>(this))
            {
                button.IsChecked = false;
            }
        }

        private static IEnumerable<T> FindVisualChildren<T>(DependencyObject parent) where T : DependencyObject
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                DependencyObject child = VisualTreeHelper.GetChild(parent, i);

                if (child is T typedChild)
                {
                    yield return typedChild;
                }

                foreach (T nestedChild in FindVisualChildren<T>(child))
                {
                    yield return nestedChild;
                }
            }
        }
    }
}
