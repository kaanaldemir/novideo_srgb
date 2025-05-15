﻿using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using System.Xml.Linq;
using Microsoft.Win32;
using NvAPIWrapper.Display;
<<<<<<< Updated upstream
=======
using System.Windows.Input;
using System.Threading;
>>>>>>> Stashed changes

namespace novideo_srgb
{
    public class MainViewModel
    {
        public ObservableCollection<MonitorData> Monitors { get; }

        private string _configPath;

        private string _startupName;
        private RegistryKey _startupKey;
        private string _startupValue;

        public MainViewModel()
        {
            Monitors = new ObservableCollection<MonitorData>();
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

            // Get all displays first and sort them by path to ensure consistent ordering
            var allDisplays = Display.GetDisplays().ToList();
            var allWindowsDisplays = WindowsDisplayAPI.Display.GetDisplays().ToList();
            
            // Create a dictionary mapping display names to their device paths
            var displayPathMap = allWindowsDisplays.ToDictionary(
                x => x.DisplayName,
                x => x.DevicePath
            );
            
            // Process displays in a consistent order
            var number = 1;
            foreach (var display in allDisplays)
            {
                if (!displayPathMap.TryGetValue(display.Name, out var path))
                    continue;

                var hdrActive = hdrPaths.Contains(path);

                var settings = config?.FirstOrDefault(x => (string)x.Attribute("path") == path);
                MonitorData monitor;
                if (settings != null)
                {
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
                        new XElement("monitor", new XAttribute("path", x.Path),
                            new XAttribute("clamp_sdr", x.ClampSdr),
                            new XAttribute("use_icc", x.UseIcc),
                            new XAttribute("icc_path", x.ProfilePath),
                            new XAttribute("calibrate_gamma", x.CalibrateGamma),
                            new XAttribute("selected_gamma", x.SelectedGamma),
                            new XAttribute("custom_gamma", x.CustomGamma),
                            new XAttribute("custom_percentage", x.CustomPercentage),
                            new XAttribute("target", x.Target),
                            new XAttribute("disable_optimization", x.DisableOptimization))));
                xElem.Save(_configPath);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message + "\n\nTry extracting the program elsewhere.");
                Environment.Exit(1);
            }
        }
    }
}