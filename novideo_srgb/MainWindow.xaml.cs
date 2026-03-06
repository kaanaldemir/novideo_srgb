using Microsoft.Win32;
using System;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Interop;
using Application = System.Windows.Application;
using Button = System.Windows.Controls.Button;
using FormsContextMenu = System.Windows.Forms.ContextMenu;
using FormsMenuItem = System.Windows.Forms.MenuItem;
using MessageBox = System.Windows.Forms.MessageBox;
using NotifyIcon = System.Windows.Forms.NotifyIcon;

namespace novideo_srgb
{
    public partial class MainWindow
    {
        private const int WM_HOTKEY = 0x0312;
        private const int HotkeyId = 9000;
        private const uint ModNoRepeat = 0x4000;

        [DllImport("user32.dll")]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll")]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        private readonly MainViewModel _viewModel;
        private FormsContextMenu _contextMenu;
        private NotifyIcon _notifyIcon;
        private HwndSource _windowSource;
        private bool _hotkeyRegistered;

        public MainWindow()
        {
            if (Process.GetProcessesByName(Process.GetCurrentProcess().ProcessName).Length > 1)
            {
                MessageBox.Show("Already running!");
                Close();
                return;
            }

            InitializeComponent();
            _viewModel = (MainViewModel)DataContext;
            SystemEvents.DisplaySettingsChanged += _viewModel.OnDisplaySettingsChanged;
            SystemEvents.PowerModeChanged += _viewModel.OnPowerModeChanged;

            var args = Environment.GetCommandLineArgs().ToList();
            args.RemoveAt(0);

            if (args.Contains("-minimize"))
            {
                WindowState = WindowState.Minimized;
                Hide();
            }

            InitializeTrayIcon();
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);

            _windowSource = PresentationSource.FromVisual(this) as HwndSource;
            if (_windowSource != null)
            {
                _windowSource.AddHook(WndProc);
            }

            ApplyHotkeyRegistration(false);
        }

        protected override void OnStateChanged(EventArgs e)
        {
            if (WindowState == WindowState.Minimized)
            {
                Hide();
            }

            base.OnStateChanged(e);
        }

        private void AboutButton_Click(object sender, RoutedEventArgs e)
        {
            var window = new AboutWindow
            {
                Owner = this,
            };
            window.ShowDialog();
        }

        private void HotkeyButton_Click(object sender, RoutedEventArgs e)
        {
            var window = new HotkeyWindow(_viewModel.HotkeyModifiers, _viewModel.HotkeyKey)
            {
                Owner = this,
            };

            if (window.ShowDialog() != true)
            {
                return;
            }

            _viewModel.HotkeyModifiers = window.HotkeyModifiers;
            _viewModel.HotkeyKey = window.HotkeyKey;
            _viewModel.SaveConfig();
            ApplyHotkeyRegistration(true);
        }

        private void AdvancedButton_Click(object sender, RoutedEventArgs e)
        {
            if (Application.Current.Windows.Cast<Window>().Any(x => x is AdvancedWindow)) return;

            var monitor = ((FrameworkElement)sender).DataContext as MonitorData;
            if (monitor == null)
            {
                return;
            }

            var window = new AdvancedWindow(monitor)
            {
                Owner = this,
            };

            void CloseWindow(object o, EventArgs e2) => window.Close();

            SystemEvents.DisplaySettingsChanged += CloseWindow;
            try
            {
                if (window.ShowDialog() != true)
                {
                    return;
                }
            }
            finally
            {
                SystemEvents.DisplaySettingsChanged -= CloseWindow;
            }

            if (window.HasChanges)
            {
                monitor.ReapplySettings();
                _viewModel.SaveConfig();
            }
        }

        private void ProfileButton_Click(object sender, RoutedEventArgs e)
        {
            var button = sender as Button;
            var monitor = button?.DataContext as MonitorData;
            int profileIndex;
            if (button == null || monitor == null || !int.TryParse(button.Tag?.ToString(), out profileIndex))
            {
                return;
            }

            monitor.SelectProfile(profileIndex);
        }

        private void ReapplyButton_Click(object sender, RoutedEventArgs e)
        {
            ReapplyMonitorSettings();
        }

        private void InitializeTrayIcon()
        {
            _notifyIcon = new NotifyIcon
            {
                Text = "Novideo sRGB",
                Icon = Properties.Resources.icon,
                Visible = true,
            };

            _notifyIcon.MouseDoubleClick += delegate
            {
                Show();
                WindowState = WindowState.Normal;
                Activate();
            };

            _contextMenu = new FormsContextMenu();
            _contextMenu.Popup += delegate { UpdateContextMenu(); };
            _notifyIcon.ContextMenu = _contextMenu;

            _notifyIcon.MouseUp += delegate(object sender, MouseEventArgs args)
            {
                if (args.Button != MouseButtons.Left || args.Clicks != 1)
                {
                    return;
                }

                UpdateContextMenu();
                var showContextMenu = typeof(NotifyIcon).GetMethod("ShowContextMenu",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                if (showContextMenu != null)
                {
                    showContextMenu.Invoke(_notifyIcon, null);
                }
            };
        }

        private void UpdateContextMenu()
        {
            _contextMenu.MenuItems.Clear();

            foreach (var monitor in _viewModel.Monitors)
            {
                var monitorMenuItem = new FormsMenuItem(monitor.Name);
                _contextMenu.MenuItems.Add(monitorMenuItem);

                for (var i = 0; i < monitor.Profiles.Count; i++)
                {
                    var capturedIndex = i;
                    var profile = monitor.Profiles[capturedIndex];
                    var profileItem = new FormsMenuItem(profile.DisplayName)
                    {
                        Checked = monitor.ClampSdr && monitor.SelectedProfileIndex == capturedIndex,
                    };
                    profileItem.Click += delegate
                    {
                        monitor.SelectProfile(capturedIndex);
                        monitor.SetClampRequested(true);
                    };
                    monitorMenuItem.MenuItems.Add(profileItem);
                }

                monitorMenuItem.MenuItems.Add("-");

                var offItem = new FormsMenuItem("Off")
                {
                    Checked = !monitor.ClampSdr,
                };
                offItem.Click += delegate { monitor.SetClampRequested(false); };
                monitorMenuItem.MenuItems.Add(offItem);
            }

            _contextMenu.MenuItems.Add("-");

            var reapplyItem = new FormsMenuItem("Reapply");
            _contextMenu.MenuItems.Add(reapplyItem);
            reapplyItem.Click += delegate { ReapplyMonitorSettings(); };

            var exitItem = new FormsMenuItem("Exit");
            _contextMenu.MenuItems.Add(exitItem);
            exitItem.Click += delegate { Close(); };
        }

        private void ReapplyMonitorSettings()
        {
            foreach (var monitor in _viewModel.Monitors)
            {
                monitor.ReapplySettings();
            }
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WM_HOTKEY && wParam.ToInt32() == HotkeyId)
            {
                ToggleAllMonitorClamping();
                handled = true;
            }

            return IntPtr.Zero;
        }

        private void ToggleAllMonitorClamping()
        {
            var enableClamp = !_viewModel.Monitors.Any(x => x.ClampSdr);
            foreach (var monitor in _viewModel.Monitors)
            {
                monitor.SetClampRequested(enableClamp);
            }
        }

        private bool ApplyHotkeyRegistration(bool showResultMessage)
        {
            UnregisterGlobalHotkey();

            if (_viewModel.HotkeyKey == 0)
            {
                if (showResultMessage)
                {
                    MessageBox.Show("Global hotkey cleared.", "Novideo sRGB", MessageBoxButtons.OK,
                        MessageBoxIcon.Information);
                }

                return true;
            }

            var handle = new WindowInteropHelper(this).Handle;
            if (handle == IntPtr.Zero)
            {
                return false;
            }

            var modifiers = _viewModel.HotkeyModifiers | ModNoRepeat;
            _hotkeyRegistered = RegisterHotKey(handle, HotkeyId, modifiers, _viewModel.HotkeyKey);

            if (showResultMessage)
            {
                if (_hotkeyRegistered)
                {
                    MessageBox.Show("Global hotkey registered.", "Novideo sRGB", MessageBoxButtons.OK,
                        MessageBoxIcon.Information);
                }
                else
                {
                    MessageBox.Show("The selected hotkey could not be registered. It may already be in use.",
                        "Novideo sRGB", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }

            return _hotkeyRegistered;
        }

        private void UnregisterGlobalHotkey()
        {
            if (!_hotkeyRegistered)
            {
                return;
            }

            var handle = new WindowInteropHelper(this).Handle;
            if (handle != IntPtr.Zero)
            {
                UnregisterHotKey(handle, HotkeyId);
            }

            _hotkeyRegistered = false;
        }

        protected override void OnClosed(EventArgs e)
        {
            SystemEvents.DisplaySettingsChanged -= _viewModel.OnDisplaySettingsChanged;
            SystemEvents.PowerModeChanged -= _viewModel.OnPowerModeChanged;
            UnregisterGlobalHotkey();

            if (_windowSource != null)
            {
                _windowSource.RemoveHook(WndProc);
                _windowSource = null;
            }

            if (_notifyIcon != null)
            {
                _notifyIcon.Visible = false;
                _notifyIcon.Dispose();
                _notifyIcon = null;
            }

            base.OnClosed(e);
        }
    }
}
