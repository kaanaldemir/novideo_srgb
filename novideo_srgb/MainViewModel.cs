using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using System.Xml.Linq;
using Microsoft.Win32;
using NvAPIWrapper.Display;
using System.Windows.Input;

namespace novideo_srgb
{
    public class MainViewModel
    {
        public ObservableCollection<MonitorData> Monitors { get; }
        public ICommand SelectProfileCommand { get; }

        private string _configPath;

        private string _startupName;
        private RegistryKey _startupKey;
        private string _startupValue;

        public MainViewModel()
        {
            Monitors = new ObservableCollection<MonitorData>();
            SelectProfileCommand = new RelayCommand(SelectProfile);
            _configPath = AppDomain.CurrentDomain.BaseDirectory + "config.xml";

            _startupName = "novideo_srgb";
            _startupKey = Registry.CurrentUser.OpenSubKey
                ("SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Run", true);
            _startupValue = Application.ExecutablePath + " -minimize";

            UpdateMonitors();
        }

        public bool? RunAtStartup
        {
            get
            {
                var keyValue = _startupKey.GetValue(_startupName);

                if (keyValue == null)
                {
                    return false;
                }

                if ((string)keyValue == _startupValue)
                {
                    return true;
                }

                return null;
            }
            set
            {
                if (value == true)
                {
                    _startupKey.SetValue(_startupName, _startupValue);
                }
                else
                {
                    _startupKey.DeleteValue(_startupName);
                }
            }
        }

        private void UpdateMonitors()
        {
            Monitors.Clear();
            List<XElement> config = null;
            if (File.Exists(_configPath))
            {
                config = XElement.Load(_configPath).Descendants("monitor").ToList();
            }

            var hdrPaths = DisplayConfigManager.GetHdrDisplayPaths();

            var number = 1;
            foreach (var display in Display.GetDisplays())
            {
                var displays = WindowsDisplayAPI.Display.GetDisplays();
                var path = displays.First(x => x.DisplayName == display.Name).DevicePath;

                var hdrActive = hdrPaths.Contains(path);

                var settings = config?.FirstOrDefault(x => (string)x.Attribute("path") == path);
                MonitorData monitor;
                if (settings != null)
                {
                    // Load profiles if available
                    var profileElements = settings.Elements("profile").ToList();
                    var profiles = new List<Profile>();
                    
                    if (profileElements.Count > 0)
                    {
                        // Load profiles from config
                        foreach (var profileElement in profileElements)
                        {
                            var profile = new Profile((string)profileElement.Attribute("name"))
                            {
                                UseIcc = (bool)profileElement.Attribute("use_icc"),
                                ProfilePath = (string)profileElement.Attribute("icc_path"),
                                CalibrateGamma = (bool)profileElement.Attribute("calibrate_gamma"),
                                SelectedGamma = (int)profileElement.Attribute("selected_gamma"),
                                CustomGamma = (double)profileElement.Attribute("custom_gamma"),
                                CustomPercentage = (double)profileElement.Attribute("custom_percentage"),
                                Target = (int)profileElement.Attribute("target"),
                                DisableOptimization = (bool)profileElement.Attribute("disable_optimization"),
                                DitherState = (int)profileElement.Attribute("dither_state"),
                                DitherMode = (int)profileElement.Attribute("dither_mode"),
                                DitherBits = (int)profileElement.Attribute("dither_bits")
                            };
                            profiles.Add(profile);
                        }
                        
                        monitor = new MonitorData(this, number++, display, path, hdrActive, 
                            (bool)settings.Attribute("clamp_sdr"),
                            false, "", false, 0, 2.2, 100, 0, false,
                            profiles: profiles,
                            selectedProfileIndex: (int)settings.Attribute("selected_profile"));
                    }
                    else
                    {
                        // Backward compatibility: create a single profile from monitor settings
                        monitor = new MonitorData(this, number++, display, path, hdrActive,
                            (bool)settings.Attribute("clamp_sdr"),
                            (bool)settings.Attribute("use_icc"),
                            (string)settings.Attribute("icc_path"),
                            (bool)settings.Attribute("calibrate_gamma"),
                            (int)settings.Attribute("selected_gamma"),
                            (double)settings.Attribute("custom_gamma"),
                            (double)settings.Attribute("custom_percentage"),
                            (int)settings.Attribute("target"),
                            (bool)settings.Attribute("disable_optimization"));
                    }
                }
                else
                {
                    monitor = new MonitorData(this, number++, display, path, hdrActive, false);
                }

                Monitors.Add(monitor);
            }

            foreach (var monitor in Monitors)
            {
                monitor.ReapplyClamp();
            }
        }

        public void OnDisplaySettingsChanged(object sender, EventArgs e)
        {
            UpdateMonitors();
        }

        public void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
        {
            if (e.Mode != PowerModes.Resume) return;
            OnDisplaySettingsChanged(null, null);
        }

        public void SaveConfig()
        {
            try
            {
                var xElem = new XElement("monitors",
                    Monitors.Select(x =>
                    {
                        var monitorElement = new XElement("monitor", 
                            new XAttribute("path", x.Path),
                            new XAttribute("clamp_sdr", x.ClampSdr),
                            new XAttribute("selected_profile", x.SelectedProfileIndex));
                        
                        // Add profiles as child elements
                        foreach (var profile in x.Profiles)
                        {
                            monitorElement.Add(new XElement("profile",
                                new XAttribute("name", profile.Name),
                                new XAttribute("use_icc", profile.UseIcc),
                                new XAttribute("icc_path", profile.ProfilePath),
                                new XAttribute("calibrate_gamma", profile.CalibrateGamma),
                                new XAttribute("selected_gamma", profile.SelectedGamma),
                                new XAttribute("custom_gamma", profile.CustomGamma),
                                new XAttribute("custom_percentage", profile.CustomPercentage),
                                new XAttribute("target", profile.Target),
                                new XAttribute("disable_optimization", profile.DisableOptimization),
                                new XAttribute("dither_state", profile.DitherState),
                                new XAttribute("dither_mode", profile.DitherMode),
                                new XAttribute("dither_bits", profile.DitherBits)));
                        }
                        
                        return monitorElement;
                    }));
                xElem.Save(_configPath);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message + "\n\nTry extracting the program elsewhere.");
                Environment.Exit(1);
            }
        }

        private void SelectProfile(object parameter)
        {
            if (parameter is object[] values && values.Length == 2)
            {
                if (int.TryParse(values[0].ToString(), out var profileIndex) && values[1] is MonitorData monitor)
                {
                    monitor.SelectedProfileIndex = profileIndex;
                    SaveConfig();
                }
            }
        }
    }
}