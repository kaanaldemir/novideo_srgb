using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace novideo_srgb
{
    public class MonitorProfile : INotifyPropertyChanged
    {
        private string _name;
        private bool _isSelected;

        public event PropertyChangedEventHandler PropertyChanged;

        public MonitorProfile(int index)
        {
            Index = index;
            _name = GetDefaultName(index);
            ProfilePath = "";
            CustomGamma = 2.2;
            CustomPercentage = 100;
        }

        public int Index { get; }

        public string Name
        {
            get => _name;
            set
            {
                var normalizedName = NormalizeName(value, Index);
                if (_name == normalizedName) return;
                _name = normalizedName;
                OnPropertyChanged();
                OnPropertyChanged(nameof(DisplayName));
            }
        }

        public string DisplayName => string.IsNullOrWhiteSpace(_name) ? GetDefaultName(Index) : _name;

        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected == value) return;
                _isSelected = value;
                OnPropertyChanged();
            }
        }

        public bool UseIcc { get; set; }

        public string ProfilePath { get; set; }

        public bool CalibrateGamma { get; set; }

        public int SelectedGamma { get; set; }

        public double CustomGamma { get; set; }

        public double CustomPercentage { get; set; }

        public bool DisableOptimization { get; set; }

        public int Target { get; set; }

        public int DitherState { get; set; }

        public int DitherMode { get; set; }

        public int DitherBits { get; set; }

        public MonitorProfile CloneForIndex(int index)
        {
            return new MonitorProfile(index)
            {
                Name = DisplayName,
                UseIcc = UseIcc,
                ProfilePath = ProfilePath ?? "",
                CalibrateGamma = CalibrateGamma,
                SelectedGamma = SelectedGamma,
                CustomGamma = CustomGamma,
                CustomPercentage = CustomPercentage,
                DisableOptimization = DisableOptimization,
                Target = Target,
                DitherState = DitherState,
                DitherMode = DitherMode,
                DitherBits = DitherBits,
            };
        }

        public static string GetDefaultName(int index)
        {
            return "Profile " + (index + 1);
        }

        private static string NormalizeName(string value, int index)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return GetDefaultName(index);
            }

            return value.Trim();
        }

        private void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
