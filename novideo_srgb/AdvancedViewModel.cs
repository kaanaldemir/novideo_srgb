using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using EDIDParser;

namespace novideo_srgb
{
    public class AdvancedViewModel : INotifyPropertyChanged
    {
        private readonly MonitorData _monitor;
        private string _profileName;
        private int _target;
        private bool _useIcc;
        private string _profilePath;
        private bool _calibrateGamma;
        private int _selectedGamma;
        private double _customGamma;
        private double _customPercentage;
        private bool _disableOptimization;
        private int _ditherState;
        private int _ditherMode;
        private int _ditherBits;

        public AdvancedViewModel()
        {
            _monitor = null;
            throw new NotSupportedException();
        }

        public AdvancedViewModel(MonitorData monitor, Novideo.DitherControl dither)
        {
            _monitor = monitor;
            _profileName = monitor.CurrentProfile.DisplayName;
            _target = monitor.Target;
            _useIcc = monitor.UseIcc;
            _profilePath = monitor.ProfilePath;
            _calibrateGamma = monitor.CalibrateGamma;
            _selectedGamma = monitor.SelectedGamma;
            _customGamma = monitor.CustomGamma;
            _customPercentage = monitor.CustomPercentage;
            _disableOptimization = monitor.DisableOptimization;
            _ditherBits = dither.bits;
            _ditherMode = dither.mode;
            _ditherState = dither.state;
        }

        public event PropertyChangedEventHandler PropertyChanged;

        public bool HasChanges { get; private set; }

        public ChromaticityCoordinates Coords => _monitor.Edid.DisplayParameters.ChromaticityCoordinates;

        public string ProfileName
        {
            get => _profileName;
            set
            {
                if (value == _profileName) return;
                _profileName = value;
                OnPropertyChanged();
            }
        }

        public bool UseEdid
        {
            get => !_useIcc;
            set
            {
                if (!value == _useIcc) return;
                _useIcc = !value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(UseIcc));
                OnPropertyChanged(nameof(EdidWarning));
            }
        }

        public bool UseIcc
        {
            get => _useIcc;
            set
            {
                if (value == _useIcc) return;
                _useIcc = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(UseEdid));
                OnPropertyChanged(nameof(EdidWarning));
            }
        }

        public string ProfilePath
        {
            get => _profilePath;
            set
            {
                if (value == _profilePath) return;
                _profilePath = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(ProfileFileName));
            }
        }

        public string ProfileFileName => string.IsNullOrEmpty(ProfilePath) ? "" : Path.GetFileName(ProfilePath);

        public bool CalibrateGamma
        {
            get => _calibrateGamma;
            set
            {
                if (value == _calibrateGamma) return;
                _calibrateGamma = value;
                OnPropertyChanged();
            }
        }

        public int SelectedGamma
        {
            get => _selectedGamma;
            set
            {
                if (value == _selectedGamma) return;
                _selectedGamma = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(UseCustomGamma));
            }
        }

        public Visibility UseCustomGamma =>
            SelectedGamma == 2 || SelectedGamma == 3 ? Visibility.Visible : Visibility.Collapsed;

        public double CustomGamma
        {
            get => _customGamma;
            set
            {
                if (value == _customGamma) return;
                _customGamma = value;
                OnPropertyChanged();
            }
        }

        public int Target
        {
            get => _target;
            set
            {
                if (value == _target) return;
                _target = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(EdidWarning));
            }
        }

        public Visibility HdrWarning => _monitor.HdrActive ? Visibility.Visible : Visibility.Collapsed;

        public Visibility EdidWarning => HdrWarning != Visibility.Visible && UseEdid &&
                                         Colorimetry.ColorSpaces[_target].Equals(_monitor.EdidColorSpace)
            ? Visibility.Visible
            : Visibility.Collapsed;

        public double CustomPercentage
        {
            get => _customPercentage;
            set
            {
                if (value == _customPercentage) return;
                _customPercentage = value;
                OnPropertyChanged();
            }
        }

        public bool DisableOptimization
        {
            get => _disableOptimization;
            set
            {
                if (value == _disableOptimization) return;
                _disableOptimization = value;
                OnPropertyChanged();
            }
        }

        public int DitherState
        {
            get => _ditherState;
            set
            {
                if (value == _ditherState) return;
                _ditherState = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(CustomDither));
                OnPropertyChanged(nameof(DitherMode));
                OnPropertyChanged(nameof(DitherBits));
            }
        }

        public int DitherMode
        {
            get => _ditherState == 0 ? -1 : _ditherMode;
            set
            {
                if (value == _ditherMode) return;
                _ditherMode = value;
                OnPropertyChanged();
            }
        }

        public int DitherBits
        {
            get => _ditherState == 0 ? -1 : _ditherBits;
            set
            {
                if (value == _ditherBits) return;
                _ditherBits = value;
                OnPropertyChanged();
            }
        }

        public bool CustomDither => DitherState == 1;

        public void ApplyChanges()
        {
            var currentProfile = _monitor.CurrentProfile;

            var existingProfileName = currentProfile.DisplayName;
            currentProfile.Name = _profileName;
            HasChanges |= existingProfileName != currentProfile.DisplayName;

            HasChanges |= _monitor.Target != _target;
            _monitor.Target = _target;
            HasChanges |= _monitor.UseIcc != _useIcc;
            _monitor.UseIcc = _useIcc;
            HasChanges |= _monitor.ProfilePath != _profilePath;
            _monitor.ProfilePath = _profilePath;
            HasChanges |= _monitor.CalibrateGamma != _calibrateGamma;
            _monitor.CalibrateGamma = _calibrateGamma;
            HasChanges |= _monitor.SelectedGamma != _selectedGamma;
            _monitor.SelectedGamma = _selectedGamma;
            HasChanges |= _monitor.CustomGamma != _customGamma;
            _monitor.CustomGamma = _customGamma;
            HasChanges |= _monitor.CustomPercentage != _customPercentage;
            _monitor.CustomPercentage = _customPercentage;
            HasChanges |= _monitor.DisableOptimization != _disableOptimization;
            _monitor.DisableOptimization = _disableOptimization;
            HasChanges |= currentProfile.DitherState != _ditherState;
            currentProfile.DitherState = _ditherState;
            HasChanges |= currentProfile.DitherMode != _ditherMode;
            currentProfile.DitherMode = _ditherMode;
            HasChanges |= currentProfile.DitherBits != _ditherBits;
            currentProfile.DitherBits = _ditherBits;
        }

        private void OnPropertyChanged([CallerMemberName] string name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}
