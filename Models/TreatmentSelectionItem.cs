using System.ComponentModel;
using System.Collections.ObjectModel;
using System.Linq;

namespace MyClinic.Models
{
    public class TreatmentSelectionItem : INotifyPropertyChanged
    {
        private bool _isSelected;
        private int _quantity;
        private string? _toothId;
        private int _canalCount;

        public int TreatmentId { get; set; }
        public string TreatmentName { get; set; } = string.Empty;
        public decimal Cost { get; set; }
        public string Currency { get; set; } = "SYP";
        public string CurrencySymbol => Currency == "USD" ? "$" : "S.P";

        public string? ToothId
        {
            get => _toothId;
            set { _toothId = value; OnPropertyChanged(nameof(ToothId)); }
        }

        public bool IsRootCanal => TreatmentName.Replace(" ", string.Empty).Contains("سحبعصب", System.StringComparison.OrdinalIgnoreCase);

        public int CanalCount
        {
            get => _canalCount;
            set
            {
                _canalCount = Math.Max(0, value);
                EnsureCanals();
                OnPropertyChanged(nameof(CanalCount));
                OnPropertyChanged(nameof(CanalSummary));
            }
        }

        public ObservableCollection<CanalMeasurement> Canals { get; } = new();
        public ObservableCollection<TreatmentToothDetail> ToothDetails { get; } = new();

        public string CanalSummary => Canals.Count == 0
            ? string.Empty
            : string.Join("، ", Canals.Select(c => string.IsNullOrWhiteSpace(c.Name) ? "قناة" : c.Name));

        public void ConfigureCanals(IEnumerable<string> names)
        {
            Canals.Clear();
            foreach (var name in names.Take(Math.Max(1, CanalCount)))
                Canals.Add(new CanalMeasurement { Name = name });
            while (Canals.Count < Math.Max(1, CanalCount))
                Canals.Add(new CanalMeasurement { Name = $"قناة {Canals.Count + 1}" });
            OnPropertyChanged(nameof(CanalSummary));
        }

        private void EnsureCanals()
        {
            if (!IsRootCanal) return;
            while (Canals.Count < Math.Max(1, CanalCount))
                Canals.Add(new CanalMeasurement { Name = $"قناة {Canals.Count + 1}" });
            while (Canals.Count > CanalCount)
                Canals.RemoveAt(Canals.Count - 1);
        }

        private void EnsureToothDetails()
        {
            int desired = Math.Max(1, _quantity);
            while (ToothDetails.Count < desired)
                ToothDetails.Add(new TreatmentToothDetail { IsRootCanal = IsRootCanal });
            while (ToothDetails.Count > desired)
                ToothDetails.RemoveAt(ToothDetails.Count - 1);
        }

        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                _isSelected = value;
                OnPropertyChanged(nameof(IsSelected));
            }
        }

        public int Quantity
        {
            get => _quantity;
            set
            {
                _quantity = value;
                EnsureToothDetails();
                OnPropertyChanged(nameof(Quantity));
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public class SelectedTreatment
    {
        private List<CanalMeasurement> _canals = new();
        private List<TreatmentToothDetail> _toothDetails = new();

        public int TreatmentId { get; set; }
        public string TreatmentName { get; set; } = string.Empty;
        public decimal Cost { get; set; }
        public string Currency { get; set; } = "SYP";
        public int Quantity { get; set; }
        public string? ToothId { get; set; }
        public int CanalCount { get; set; }
        public List<CanalMeasurement> Canals
        {
            get => _canals;
            set => _canals = value ?? new();
        }

        public List<TreatmentToothDetail> ToothDetails
        {
            get => _toothDetails;
            set => _toothDetails = value ?? new();
        }
        public string ToothDisplay => ToothDetails is { Count: > 0 }
            ? string.Join("، ", ToothDetails.Where(d => d is not null).Select(d => string.IsNullOrWhiteSpace(d.ToothId) ? "السن غير محدد" : $"السن {d.ToothId}"))
            : (string.IsNullOrWhiteSpace(ToothId) ? "السن غير محدد" : $"السن {ToothId}");
        public bool HasToothDetails => ToothDetails.Count > 0;
        public string DetailsDisplay
        {
            get
            {
                IEnumerable<TreatmentToothDetail> teeth = ToothDetails.Where(d => d is not null);
                if (!teeth.Any())
                    return string.IsNullOrWhiteSpace(ToothId) ? "السن غير محدد" : $"السن {ToothId}";

                return string.Join(Environment.NewLine + Environment.NewLine, teeth.Select(tooth =>
                {
                    string label = string.IsNullOrWhiteSpace(tooth.ToothId) ? "السن غير محدد" : $"السن {tooth.ToothId}";
                    string count = $"عدد الأقنية: {tooth.CanalCount}";
                    string canals = tooth.CanalDisplay;
                    return string.IsNullOrWhiteSpace(canals)
                        ? $"{label}{Environment.NewLine}{count}"
                        : $"{label}{Environment.NewLine}{count}{Environment.NewLine}{canals}";
                }));
            }
        }
        public string CanalDisplay => ToothDetails.Where(d => d is not null).SelectMany(d => d.Canals ?? new()).Any()
            ? string.Join("، ", ToothDetails.Where(d => d is not null).SelectMany(d => d.Canals ?? new()).Where(c => c is not null).Select(c => string.IsNullOrWhiteSpace(c.Name) ? $"{c.WorkingLength} مم" : $"{c.Name}: {c.WorkingLength} مم"))
            : Canals.Count == 0 ? string.Empty : string.Join("، ", Canals.Where(c => c is not null).Select(c => string.IsNullOrWhiteSpace(c.Name) ? $"{c.WorkingLength} مم" : $"{c.Name}: {c.WorkingLength} مم"));
    }

    public class TreatmentToothDetail : INotifyPropertyChanged
    {
        private string? _toothId;
        private int _canalCount;

        public string? ToothId
        {
            get => _toothId;
            set
            {
                if (_toothId == value) return;
                _toothId = value;
                OnPropertyChanged(nameof(ToothId));
                OnPropertyChanged(nameof(ToothLabel));
            }
        }

        public bool IsRootCanal { get; set; }
        public int CanalCount
        {
            get => _canalCount;
            set
            {
                int normalized = Math.Max(0, value);
                if (_canalCount == normalized) return;
                _canalCount = normalized;
                OnPropertyChanged(nameof(CanalCount));
            }
        }
        // A setter is required so System.Text.Json can restore the canal
        // measurements saved inside a visit's SelectedTreatmentsJson.
        private ObservableCollection<CanalMeasurement> _canals = new();
        public ObservableCollection<CanalMeasurement> Canals
        {
            get => _canals;
            set => _canals = value ?? new();
        }
        public string ToothLabel => string.IsNullOrWhiteSpace(ToothId) ? "السن غير محدد" : $"السن {ToothId}";
        public string CanalCountDisplay => $"عدد الأقنية: {CanalCount}";
        public string CanalDisplay => Canals is null || Canals.Count == 0 ? "" : string.Join("، ", Canals.Where(c => c is not null).Select(c => string.IsNullOrWhiteSpace(c.Name) ? $"طول العمل: {c.WorkingLength} مم" : $"{c.Name}: {c.WorkingLength} مم"));
        public event PropertyChangedEventHandler? PropertyChanged;
        public void Changed(string propertyName) => OnPropertyChanged(propertyName);
        private void OnPropertyChanged(string propertyName) => PropertyChanged?.Invoke(this, new(propertyName));
    }

    public class CanalMeasurement : INotifyPropertyChanged
    {
        private string _name = string.Empty;
        private string _workingLength = string.Empty;
        public bool IsExtraCanal { get; set; }
        public bool IsReadOnly => !IsExtraCanal;

        public string Name { get => _name; set { _name = value; PropertyChanged?.Invoke(this, new(nameof(Name))); } }
        public string WorkingLength { get => _workingLength; set { _workingLength = value; PropertyChanged?.Invoke(this, new(nameof(WorkingLength))); } }
        public event PropertyChangedEventHandler? PropertyChanged;
    }
}
