using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;
using NvAPIWrapper.Display;

namespace novideo_srgb
{
    public class MainViewModel
    {
        private const uint SupportedHotkeyModifiers = HotkeyWindow.ModAlt | HotkeyWindow.ModControl |
                                                      HotkeyWindow.ModShift | HotkeyWindow.ModWin;

        private readonly string _configPath;
        private readonly string _startupName;
        private readonly RegistryKey _startupKey;
        private readonly string _startupValue;
        private int _displaySettingsSuppressionCount;

        public MainViewModel()
        {
            Monitors = new ObservableCollection<MonitorData>();
            _configPath = AppDomain.CurrentDomain.BaseDirectory + "config.xml";

            _startupName = "novideo_srgb";
            _startupKey = Registry.CurrentUser.CreateSubKey("SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Run");
            _startupValue = Application.ExecutablePath + " -minimize";

            UpdateMonitors();
        }

        public ObservableCollection<MonitorData> Monitors { get; }

        public uint HotkeyModifiers { get; set; }

        public uint HotkeyKey { get; set; }

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
                    _startupKey.DeleteValue(_startupName, false);
                }
            }
        }

        public void OnDisplaySettingsChanged(object sender, EventArgs e)
        {
            if (Volatile.Read(ref _displaySettingsSuppressionCount) > 0)
            {
                return;
            }

            UpdateMonitors();
        }

        public void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
        {
            if (e.Mode != PowerModes.Resume) return;
            OnDisplaySettingsChanged(null, null);
        }

        public IDisposable SuppressDisplaySettingsRefresh()
        {
            return new DisplaySettingsRefreshScope(this);
        }

        public void SaveConfig()
        {
            try
            {
                var configuration = new NovideoConfiguration
                {
                    HotkeyModifiers = HotkeyModifiers & SupportedHotkeyModifiers,
                    HotkeyKey = HotkeyKey,
                };

                foreach (var monitor in Monitors)
                {
                    configuration.Monitors.Add(monitor.ToConfiguration());
                }

                ConfigurationStore.Save(_configPath, configuration);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message + "\n\nTry extracting the program elsewhere.");
                Environment.Exit(1);
            }
        }

        private void UpdateMonitors()
        {
            Monitors.Clear();

            var configuration = ConfigurationStore.Load(_configPath);
            HotkeyModifiers = configuration.HotkeyModifiers & SupportedHotkeyModifiers;
            HotkeyKey = configuration.HotkeyKey;

            var hdrPaths = DisplayConfigManager.GetHdrDisplayPaths();
            var monitorConfigurations = configuration.Monitors
                .Where(x => !string.IsNullOrWhiteSpace(x.Path))
                .ToDictionary(x => x.Path, x => x);
            var windowsDisplays = WindowsDisplayAPI.Display.GetDisplays().ToList();

            var number = 1;
            foreach (var display in Display.GetDisplays())
            {
                var windowsDisplay = windowsDisplays.FirstOrDefault(x => x.DisplayName == display.Name);
                if (windowsDisplay == null)
                {
                    continue;
                }

                var path = windowsDisplay.DevicePath;
                var hdrActive = hdrPaths.Contains(path);

                MonitorConfiguration monitorConfiguration;
                monitorConfigurations.TryGetValue(path, out monitorConfiguration);

                var monitor = new MonitorData(this, number++, display, path, hdrActive, monitorConfiguration);
                Monitors.Add(monitor);
            }

            foreach (var monitor in Monitors)
            {
                monitor.ReapplySettings();
            }
        }

        private sealed class DisplaySettingsRefreshScope : IDisposable
        {
            private readonly MainViewModel _viewModel;

            public DisplaySettingsRefreshScope(MainViewModel viewModel)
            {
                _viewModel = viewModel;
                Interlocked.Increment(ref _viewModel._displaySettingsSuppressionCount);
            }

            public void Dispose()
            {
                Interlocked.Decrement(ref _viewModel._displaySettingsSuppressionCount);
            }
        }
    }
}
