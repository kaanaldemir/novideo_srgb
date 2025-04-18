﻿using System;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Windows;
using System.Collections.Generic;
using EDIDParser;
using EDIDParser.Descriptors;
using EDIDParser.Enums;
using NvAPIWrapper.Display;
using NvAPIWrapper.GPU;
using NvAPIWrapper.Native.Display;

namespace novideo_srgb
{
    public class Profile
    {
        public string Name { get; set; }
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
        
        public Profile(string name)
        {
            Name = name;
            ProfilePath = "";
            CustomGamma = 2.2;
            CustomPercentage = 100;
        }
        
        public Profile Clone()
        {
            return new Profile(Name)
            {
                UseIcc = UseIcc,
                ProfilePath = ProfilePath,
                CalibrateGamma = CalibrateGamma,
                SelectedGamma = SelectedGamma,
                CustomGamma = CustomGamma,
                CustomPercentage = CustomPercentage,
                DisableOptimization = DisableOptimization,
                Target = Target,
                DitherState = DitherState,
                DitherMode = DitherMode,
                DitherBits = DitherBits
            };
        }
    }

    public class MonitorData : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;

        private readonly GPUOutput _output;
        private bool _clamped;
        private int _bitDepth;
        private Novideo.DitherControl _dither;

        private MainViewModel _viewModel;
        
        public List<Profile> Profiles { get; private set; }
        private int _selectedProfileIndex;
        public int SelectedProfileIndex
        {
            get => _selectedProfileIndex;
            set
            {
                if (_selectedProfileIndex == value) return;
                _selectedProfileIndex = value;
                OnPropertyChanged();
                ReapplyClamp();
            }
        }

        public MonitorData(MainViewModel viewModel, int number, Display display, string path, bool hdrActive, bool clampSdr)
        {
            _viewModel = viewModel;
            Number = number;
            _output = display.Output;

            _bitDepth = 0;
            try
            {
                var bitDepth = display.DisplayDevice.CurrentColorData.ColorDepth;
                if (bitDepth == ColorDataDepth.BPC6)
                    _bitDepth = 6;
                else if (bitDepth == ColorDataDepth.BPC8)
                    _bitDepth = 8;
                else if (bitDepth == ColorDataDepth.BPC10)
                    _bitDepth = 10;
                else if (bitDepth == ColorDataDepth.BPC12)
                    _bitDepth = 12;
                else if (bitDepth == ColorDataDepth.BPC16)
                    _bitDepth = 16;
            }
            catch (Exception)
            {
            }

            Edid = Novideo.GetEDID(path, display);

            Name = Edid.Descriptors.OfType<StringDescriptor>()
                .FirstOrDefault(x => x.Type == StringDescriptorType.MonitorName)?.Value ?? "<no name>";

            Path = path;
            ClampSdr = clampSdr;
            HdrActive = hdrActive;

            var coords = Edid.DisplayParameters.ChromaticityCoordinates;
            EdidColorSpace = new Colorimetry.ColorSpace
            {
                Red = new Colorimetry.Point { X = Math.Round(coords.RedX, 3), Y = Math.Round(coords.RedY, 3) },
                Green = new Colorimetry.Point { X = Math.Round(coords.GreenX, 3), Y = Math.Round(coords.GreenY, 3) },
                Blue = new Colorimetry.Point { X = Math.Round(coords.BlueX, 3), Y = Math.Round(coords.BlueY, 3) },
                White = Colorimetry.D65
            };

            _dither = Novideo.GetDitherControl(_output);
            _clamped = Novideo.IsColorSpaceConversionActive(_output);
            
            // Initialize profiles
            Profiles = new List<Profile>
            {
                new Profile("Profile 1"),
                new Profile("Profile 2"),
                new Profile("Profile 3")
            };
            
            _selectedProfileIndex = 0;
        }

        public MonitorData(MainViewModel viewModel, int number, Display display, string path, bool hdrActive, bool clampSdr, bool useIcc, string profilePath,
            bool calibrateGamma, int selectedGamma, double customGamma, double customPercentage, int target, bool disableOptimization, 
            List<Profile> profiles = null, int selectedProfileIndex = 0) :
            this(viewModel, number, display, path, hdrActive, clampSdr)
        {
            if (profiles != null)
            {
                Profiles = profiles;
                _selectedProfileIndex = selectedProfileIndex;
            }
            else
            {
                // Apply to first profile if no profiles are provided
                CurrentProfile.UseIcc = useIcc;
                CurrentProfile.ProfilePath = profilePath;
                CurrentProfile.CalibrateGamma = calibrateGamma;
                CurrentProfile.SelectedGamma = selectedGamma;
                CurrentProfile.CustomGamma = customGamma;
                CurrentProfile.CustomPercentage = customPercentage;
                CurrentProfile.Target = target;
                CurrentProfile.DisableOptimization = disableOptimization;
                
                // Apply dither settings
                CurrentProfile.DitherState = _dither.state;
                CurrentProfile.DitherMode = _dither.mode;
                CurrentProfile.DitherBits = _dither.bits;
            }
        }

        public int Number { get; }
        public string Name { get; }
        public EDID Edid { get; }
        public string Path { get; }
        public bool ClampSdr { get; set; }
        public bool HdrActive { get; }

        private void UpdateClamp(bool doClamp)
        {
            if (_clamped)
            {
                Novideo.DisableColorSpaceConversion(_output);
            }

            if (!doClamp) return;

            if (_clamped) Thread.Sleep(100);
            if (UseEdid)
                Novideo.SetColorSpaceConversion(_output, Colorimetry.RGBToRGB(TargetColorSpace, EdidColorSpace));
            else if (UseIcc)
            {
                var profile = ICCMatrixProfile.FromFile(ProfilePath);
                if (CalibrateGamma)
                {
                    var trcBlack = Matrix.FromValues(new[,]
                    {
                        { profile.trcs[0].SampleAt(0) },
                        { profile.trcs[1].SampleAt(0) },
                        { profile.trcs[2].SampleAt(0) }
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

        private void HandleClampException(Exception e)
        {
            MessageBox.Show(e.Message);
            _clamped = Novideo.IsColorSpaceConversionActive(_output);
            ClampSdr = _clamped;
            _viewModel.SaveConfig();
            OnPropertyChanged(nameof(Clamped));
        }
        
        public bool Clamped
        {
            set
            {
                try
                {
                    UpdateClamp(value);
                    ClampSdr = value;
                    _viewModel.SaveConfig();
                }
                catch (Exception e)
                {
                    HandleClampException(e);
                    return;
                }

                _clamped = value;
                OnPropertyChanged();
            }
            get => _clamped;
        }

        public void ReapplyClamp()
        {
            try
            {
                var clamped = CanClamp && ClampSdr;
                UpdateClamp(clamped);
                _clamped = clamped;
                OnPropertyChanged(nameof(CanClamp));
            }
            catch (Exception e)
            {
                HandleClampException(e);
            }
        }

        public bool CanClamp => !HdrActive && (UseEdid && !EdidColorSpace.Equals(TargetColorSpace) || UseIcc && ProfilePath != "");

        public string GPU => _output.PhysicalGPU.FullName;

        public bool UseEdid
        {
            get => !CurrentProfile.UseIcc;
            set
            {
                CurrentProfile.UseIcc = !value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(UseIcc));
            }
        }

        public bool UseIcc 
        {
            get => CurrentProfile.UseIcc;
            set
            {
                CurrentProfile.UseIcc = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(UseEdid));
            }
        }

        public string ProfilePath
        {
            get => CurrentProfile.ProfilePath;
            set
            {
                CurrentProfile.ProfilePath = value;
                OnPropertyChanged();
            }
        }

        public bool CalibrateGamma
        {
            get => CurrentProfile.CalibrateGamma;
            set
            {
                CurrentProfile.CalibrateGamma = value;
                OnPropertyChanged();
            }
        }

        public int SelectedGamma
        {
            get => CurrentProfile.SelectedGamma;
            set
            {
                CurrentProfile.SelectedGamma = value;
                OnPropertyChanged();
            }
        }

        public double CustomGamma
        {
            get => CurrentProfile.CustomGamma;
            set
            {
                CurrentProfile.CustomGamma = value;
                OnPropertyChanged();
            }
        }

        public double CustomPercentage
        {
            get => CurrentProfile.CustomPercentage;
            set
            {
                CurrentProfile.CustomPercentage = value;
                OnPropertyChanged();
            }
        }

        public int Target
        {
            get => CurrentProfile.Target;
            set
            {
                CurrentProfile.Target = value;
                OnPropertyChanged();
            }
        }

        public bool DisableOptimization
        {
            get => CurrentProfile.DisableOptimization;
            set
            {
                CurrentProfile.DisableOptimization = value;
                OnPropertyChanged();
            }
        }

        public Colorimetry.ColorSpace EdidColorSpace { get; }

        private Colorimetry.ColorSpace TargetColorSpace => Colorimetry.ColorSpaces[Target];

        public Novideo.DitherControl DitherControl
        {
            get
            {
                // Create a DitherControl from the current profile
                var control = new Novideo.DitherControl
                {
                    state = CurrentProfile.DitherState,
                    bits = CurrentProfile.DitherBits,
                    mode = CurrentProfile.DitherMode,
                    bitsCaps = _dither.bitsCaps,
                    modeCaps = _dither.modeCaps
                };
                
                return control;
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
                    "Temporal"
                };
                if (_dither.state == 2)
                {
                    return "Disabled (forced)";
                }
                if (_dither.state == 0 & _dither.bits == 0 && _dither.mode == 0)
                {
                    return "Disabled (default)";
                }
                var bits = (6 + 2 * _dither.bits).ToString();
                return bits + " bit " + types[_dither.mode] + " (" + (_dither.state == 0 ? "default" : "forced") + ")";
            }
        }

        public int BitDepth => _bitDepth;

        public void ApplyDither(int state, int bits, int mode)
        {
            try
            {
                Novideo.SetDitherControl(_output, state, bits, mode);
                _dither = Novideo.GetDitherControl(_output);
                
                // Save settings to current profile
                CurrentProfile.DitherState = state;
                CurrentProfile.DitherBits = bits;
                CurrentProfile.DitherMode = mode;
                
                OnPropertyChanged(nameof(DitherString));
                _viewModel.SaveConfig();
            }
            catch (Exception e)
            {
                MessageBox.Show(e.Message);
            }
        }

        private void OnPropertyChanged([CallerMemberName] string name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }

        public Profile CurrentProfile => Profiles[_selectedProfileIndex];

        public void SetProfileName(int profileIndex, string name)
        {
            if (profileIndex >= 0 && profileIndex < Profiles.Count)
            {
                Profiles[profileIndex].Name = name;
                OnPropertyChanged("Profiles");
            }
        }

        public string GetProfileName(int profileIndex)
        {
            if (profileIndex >= 0 && profileIndex < Profiles.Count)
            {
                return Profiles[profileIndex].Name;
            }
            return "Unknown";
        }
    }
}