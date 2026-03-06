using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using EDIDParser;
using EDIDParser.Descriptors;
using EDIDParser.Enums;
using NvAPIWrapper.Display;
using NvAPIWrapper.GPU;
using NvAPIWrapper.Native.Display;
using MessageBox = System.Windows.Forms.MessageBox;

namespace novideo_srgb
{
    public class MonitorData : INotifyPropertyChanged
    {
        private readonly GPUOutput _output;
        private readonly MainViewModel _viewModel;
        private readonly int _bitDepth;
        private bool _clamped;
        private Novideo.DitherControl _dither;
        private int _selectedProfileIndex;

        public MonitorData(MainViewModel viewModel, int number, Display display, string path, bool hdrActive,
            MonitorConfiguration configuration = null)
        {
            _viewModel = viewModel;
            Number = number;
            _output = display.Output;
            _bitDepth = GetBitDepth(display);

            Edid = Novideo.GetEDID(path, display);
            Name = Edid.Descriptors.OfType<StringDescriptor>()
                .FirstOrDefault(x => x.Type == StringDescriptorType.MonitorName)?.Value ?? "<no name>";

            Path = path;
            HdrActive = hdrActive;

            var coords = Edid.DisplayParameters.ChromaticityCoordinates;
            EdidColorSpace = new Colorimetry.ColorSpace
            {
                Red = new Colorimetry.Point { X = Math.Round(coords.RedX, 3), Y = Math.Round(coords.RedY, 3) },
                Green = new Colorimetry.Point { X = Math.Round(coords.GreenX, 3), Y = Math.Round(coords.GreenY, 3) },
                Blue = new Colorimetry.Point { X = Math.Round(coords.BlueX, 3), Y = Math.Round(coords.BlueY, 3) },
                White = Colorimetry.D65,
            };

            _dither = Novideo.GetDitherControl(_output);
            _clamped = Novideo.IsColorSpaceConversionActive(_output);

            Profiles = new ObservableCollection<MonitorProfile>();
            InitializeProfiles(configuration);

            ClampSdr = configuration != null && configuration.ClampSdr;
            _selectedProfileIndex = NormalizeProfileIndex(configuration != null ? configuration.SelectedProfileIndex : 0);
            RefreshProfileSelection();
        }

        public event PropertyChangedEventHandler PropertyChanged;

        public int Number { get; }

        public string Name { get; }

        public EDID Edid { get; }

        public string Path { get; }

        public bool ClampSdr { get; private set; }

        public bool HdrActive { get; }

        public ObservableCollection<MonitorProfile> Profiles { get; }

        public int SelectedProfileIndex => _selectedProfileIndex;

        public MonitorProfile CurrentProfile => Profiles[_selectedProfileIndex];

        public bool Clamped
        {
            get => _clamped;
            set => SetClampRequested(value);
        }

        public bool CanClamp => !HdrActive && (UseEdid && !EdidColorSpace.Equals(TargetColorSpace) || UseIcc && ProfilePath != "");

        public string GPU => _output.PhysicalGPU.FullName;

        public bool UseEdid
        {
            get => !UseIcc;
            set => UseIcc = !value;
        }

        public bool UseIcc
        {
            get => CurrentProfile.UseIcc;
            set
            {
                if (CurrentProfile.UseIcc == value) return;
                CurrentProfile.UseIcc = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(UseEdid));
                OnPropertyChanged(nameof(CanClamp));
            }
        }

        public string ProfilePath
        {
            get => CurrentProfile.ProfilePath;
            set
            {
                var normalizedPath = value ?? "";
                if (CurrentProfile.ProfilePath == normalizedPath) return;
                CurrentProfile.ProfilePath = normalizedPath;
                OnPropertyChanged();
                OnPropertyChanged(nameof(CanClamp));
            }
        }

        public bool CalibrateGamma
        {
            get => CurrentProfile.CalibrateGamma;
            set
            {
                if (CurrentProfile.CalibrateGamma == value) return;
                CurrentProfile.CalibrateGamma = value;
                OnPropertyChanged();
            }
        }

        public int SelectedGamma
        {
            get => CurrentProfile.SelectedGamma;
            set
            {
                if (CurrentProfile.SelectedGamma == value) return;
                CurrentProfile.SelectedGamma = value;
                OnPropertyChanged();
            }
        }

        public double CustomGamma
        {
            get => CurrentProfile.CustomGamma;
            set
            {
                if (CurrentProfile.CustomGamma == value) return;
                CurrentProfile.CustomGamma = value;
                OnPropertyChanged();
            }
        }

        public double CustomPercentage
        {
            get => CurrentProfile.CustomPercentage;
            set
            {
                if (CurrentProfile.CustomPercentage == value) return;
                CurrentProfile.CustomPercentage = value;
                OnPropertyChanged();
            }
        }

        public bool DisableOptimization
        {
            get => CurrentProfile.DisableOptimization;
            set
            {
                if (CurrentProfile.DisableOptimization == value) return;
                CurrentProfile.DisableOptimization = value;
                OnPropertyChanged();
            }
        }

        public int Target
        {
            get => CurrentProfile.Target;
            set
            {
                if (CurrentProfile.Target == value) return;
                CurrentProfile.Target = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(CanClamp));
            }
        }

        public Colorimetry.ColorSpace EdidColorSpace { get; }

        public Novideo.DitherControl DitherControl
        {
            get
            {
                return new Novideo.DitherControl
                {
                    state = CurrentProfile.DitherState,
                    bits = CurrentProfile.DitherBits,
                    mode = CurrentProfile.DitherMode,
                    bitsCaps = _dither.bitsCaps,
                    modeCaps = _dither.modeCaps,
                };
            }
        }

        public string DitherString
        {
            get
            {
                string[] types =
                {
                    "SpatialDynamic",
                    "SpatialStatic",
                    "SpatialDynamic2x2",
                    "SpatialStatic2x2",
                    "Temporal",
                };

                if (_dither.state == 2)
                {
                    return "Disabled (forced)";
                }

                if (_dither.state == 0 && _dither.bits == 0 && _dither.mode == 0)
                {
                    return "Disabled (default)";
                }

                var bits = (6 + 2 * _dither.bits).ToString();
                return bits + " bit " + types[_dither.mode] + " (" + (_dither.state == 0 ? "default" : "forced") + ")";
            }
        }

        public int BitDepth => _bitDepth;

        private Colorimetry.ColorSpace TargetColorSpace => Colorimetry.ColorSpaces[Target];

        public void ApplyDither(int state, int bits, int mode)
        {
            CurrentProfile.DitherState = state;
            CurrentProfile.DitherBits = bits;
            CurrentProfile.DitherMode = mode;
            _viewModel.SaveConfig();
            ApplyStoredDither();
        }

        public void ReapplySettings()
        {
            ApplyStoredDither();
            ApplyRequestedClampState();
        }

        public void SelectProfile(int profileIndex)
        {
            var normalizedIndex = NormalizeProfileIndex(profileIndex);
            if (_selectedProfileIndex == normalizedIndex)
            {
                return;
            }

            _selectedProfileIndex = normalizedIndex;
            RefreshProfileSelection();
            NotifyCurrentProfileChanged();
            _viewModel.SaveConfig();
            ReapplySettings();
        }

        public void SetClampRequested(bool requestedClamp)
        {
            if (ClampSdr == requestedClamp && _clamped == (CanClamp && requestedClamp))
            {
                return;
            }

            ClampSdr = requestedClamp;
            _viewModel.SaveConfig();
            ApplyRequestedClampState();
        }

        public MonitorConfiguration ToConfiguration()
        {
            var configuration = new MonitorConfiguration
            {
                Path = Path,
                ClampSdr = ClampSdr,
                SelectedProfileIndex = _selectedProfileIndex,
            };

            foreach (var profile in Profiles)
            {
                configuration.Profiles.Add(profile.CloneForIndex(configuration.Profiles.Count));
            }

            return configuration;
        }

        private void UpdateClamp(bool doClamp)
        {
            if (_clamped)
            {
                Novideo.DisableColorSpaceConversion(_output);
            }

            if (!doClamp) return;

            if (_clamped) Thread.Sleep(100);
            if (UseEdid)
            {
                Novideo.SetColorSpaceConversion(_output, Colorimetry.RGBToRGB(TargetColorSpace, EdidColorSpace));
            }
            else if (UseIcc)
            {
                var profile = ICCMatrixProfile.FromFile(ProfilePath);
                if (CalibrateGamma)
                {
                    var trcBlack = Matrix.FromValues(new[,]
                    {
                        { profile.trcs[0].SampleAt(0) },
                        { profile.trcs[1].SampleAt(0) },
                        { profile.trcs[2].SampleAt(0) },
                    });
                    var black = (profile.matrix * trcBlack)[1];

                    ToneCurve gamma;
                    switch (SelectedGamma)
                    {
                        case 0:
                            gamma = new SrgbEOTF(black);
                            break;
                        case 1:
                            gamma = new GammaToneCurve(2.4, black, 0);
                            break;
                        case 2:
                            gamma = new GammaToneCurve(CustomGamma, black, CustomPercentage / 100);
                            break;
                        case 3:
                            gamma = new GammaToneCurve(CustomGamma, black, CustomPercentage / 100, true);
                            break;
                        case 4:
                            gamma = new LstarEOTF(black);
                            break;
                        default:
                            throw new NotSupportedException("Unsupported gamma type " + SelectedGamma);
                    }

                    Novideo.SetColorSpaceConversion(_output, profile, TargetColorSpace, gamma, DisableOptimization);
                }
                else
                {
                    Novideo.SetColorSpaceConversion(_output, profile, TargetColorSpace);
                }
            }
        }

        private void HandleClampException(Exception exception)
        {
            MessageBox.Show(exception.Message);
            _clamped = Novideo.IsColorSpaceConversionActive(_output);
            ClampSdr = _clamped;
            _viewModel.SaveConfig();
            OnPropertyChanged(nameof(Clamped));
            OnPropertyChanged(nameof(CanClamp));
        }

        private void ApplyRequestedClampState()
        {
            try
            {
                var clamped = CanClamp && ClampSdr;
                if (!clamped && !_clamped)
                {
                    OnPropertyChanged(nameof(Clamped));
                    OnPropertyChanged(nameof(CanClamp));
                    return;
                }

                using (_viewModel.SuppressDisplaySettingsRefresh())
                {
                    UpdateClamp(clamped);
                    _clamped = clamped;
                }

                OnPropertyChanged(nameof(Clamped));
                OnPropertyChanged(nameof(CanClamp));
            }
            catch (Exception exception)
            {
                HandleClampException(exception);
            }
        }

        private void ApplyStoredDither()
        {
            try
            {
                var desiredDither = DitherControl;
                if (_dither.state == desiredDither.state &&
                    _dither.bits == desiredDither.bits &&
                    _dither.mode == desiredDither.mode)
                {
                    return;
                }

                using (_viewModel.SuppressDisplaySettingsRefresh())
                {
                    Novideo.SetDitherControl(_output, desiredDither.state, desiredDither.bits, desiredDither.mode);
                    _dither = Novideo.GetDitherControl(_output);
                }

                OnPropertyChanged(nameof(DitherString));
            }
            catch (Exception exception)
            {
                MessageBox.Show(exception.Message);
            }
        }

        private static int GetBitDepth(Display display)
        {
            try
            {
                var bitDepth = display.DisplayDevice.CurrentColorData.ColorDepth;
                switch (bitDepth)
                {
                    case ColorDataDepth.BPC6:
                        return 6;
                    case ColorDataDepth.BPC8:
                        return 8;
                    case ColorDataDepth.BPC10:
                        return 10;
                    case ColorDataDepth.BPC12:
                        return 12;
                    case ColorDataDepth.BPC16:
                        return 16;
                    default:
                        return 0;
                }
            }
            catch (Exception)
            {
                return 0;
            }
        }

        private void InitializeProfiles(MonitorConfiguration configuration)
        {
            if (configuration != null)
            {
                foreach (var profile in configuration.Profiles.Take(3))
                {
                    Profiles.Add(profile.CloneForIndex(Profiles.Count));
                }
            }

            if (Profiles.Count == 0)
            {
                Profiles.Add(new MonitorProfile(0)
                {
                    DitherState = _dither.state,
                    DitherBits = _dither.bits,
                    DitherMode = _dither.mode,
                });
            }

            while (Profiles.Count < 3)
            {
                var clone = Profiles[0].CloneForIndex(Profiles.Count);
                clone.Name = MonitorProfile.GetDefaultName(Profiles.Count);
                Profiles.Add(clone);
            }
        }

        private int NormalizeProfileIndex(int profileIndex)
        {
            if (profileIndex < 0)
            {
                return 0;
            }

            if (profileIndex >= Profiles.Count)
            {
                return Profiles.Count - 1;
            }

            return profileIndex;
        }

        private void RefreshProfileSelection()
        {
            for (var i = 0; i < Profiles.Count; i++)
            {
                Profiles[i].IsSelected = i == _selectedProfileIndex;
            }
        }

        private void NotifyCurrentProfileChanged()
        {
            OnPropertyChanged(nameof(SelectedProfileIndex));
            OnPropertyChanged(nameof(CurrentProfile));
            OnPropertyChanged(nameof(UseEdid));
            OnPropertyChanged(nameof(UseIcc));
            OnPropertyChanged(nameof(ProfilePath));
            OnPropertyChanged(nameof(CalibrateGamma));
            OnPropertyChanged(nameof(SelectedGamma));
            OnPropertyChanged(nameof(CustomGamma));
            OnPropertyChanged(nameof(CustomPercentage));
            OnPropertyChanged(nameof(DisableOptimization));
            OnPropertyChanged(nameof(Target));
            OnPropertyChanged(nameof(CanClamp));
            OnPropertyChanged(nameof(DitherControl));
            OnPropertyChanged(nameof(DitherString));
        }

        private void OnPropertyChanged([CallerMemberName] string name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}
