using Microsoft.Win32;
using System;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Forms;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using Application = System.Windows.Application;
using MessageBox = System.Windows.Forms.MessageBox;

namespace novideo_srgb
{
    public partial class MainWindow
    {
        // Hotkey constants and P/Invoke declarations
        private const int WM_HOTKEY = 0x0312;
        private const int HOTKEY_ID = 9000;
        
        [DllImport("user32.dll")]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
        
        [DllImport("user32.dll")]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
        
        // Modifiers
        private const uint MOD_ALT = 0x0001;
        private const uint MOD_CONTROL = 0x0002;
        private const uint MOD_SHIFT = 0x0004;
        private const uint MOD_WIN = 0x0008;
        
        // Default hotkey is Ctrl+Alt+S
        private uint _hotkeyModifiers = MOD_CONTROL | MOD_ALT;
        private uint _hotkeyKey = (uint)Keys.S;
        private bool _hotkeyRegistered = false;
        private HwndSource _source;

        private readonly MainViewModel _viewModel;

        private ContextMenu _contextMenu;

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

        // Override WndProc to capture hotkey messages
        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            _source = PresentationSource.FromVisual(this) as HwndSource;
            _source?.AddHook(WndProc);
            
            // Register hotkey when the window is initialized
            RegisterGlobalHotkey();
        }
        
        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            // Handle WM_HOTKEY message
            if (msg == WM_HOTKEY && wParam.ToInt32() == HOTKEY_ID)
            {
                ToggleAllMonitorClamping();
                handled = true;
            }
            return IntPtr.Zero;
        }
        
        private void ToggleAllMonitorClamping()
        {
            bool anyEnabled = _viewModel.Monitors.Any(m => m.Clamped);
            
            foreach (var monitor in _viewModel.Monitors)
            {
                // If any are enabled, disable all; otherwise enable all
                monitor.Clamped = !anyEnabled;
            }
        }
        
        // Register the hotkey
        private bool RegisterGlobalHotkey()
        {
            if (_hotkeyRegistered)
            {
                UnregisterGlobalHotkey();
            }
            
            var hwnd = new WindowInteropHelper(this).Handle;
            _hotkeyRegistered = RegisterHotKey(hwnd, HOTKEY_ID, _hotkeyModifiers, _hotkeyKey);
            
            return _hotkeyRegistered;
        }
        
        // Unregister the hotkey
        private void UnregisterGlobalHotkey()
        {
            if (!_hotkeyRegistered) return;
            
            var hwnd = new WindowInteropHelper(this).Handle;
            UnregisterHotKey(hwnd, HOTKEY_ID);
            _hotkeyRegistered = false;
        }
        
        protected override void OnClosed(EventArgs e)
        {
            UnregisterGlobalHotkey();
            base.OnClosed(e);
        }

        protected override void OnStateChanged(EventArgs e)
        {
            if (WindowState == WindowState.Minimized)
            {
                Hide();
            }

            base.OnStateChanged(e);
        }

        private void AboutButton_Click(object sender, RoutedEventArgs o)
        {
            var window = new AboutWindow
            {
                Owner = this
            };
            window.ShowDialog();
        }

        private void AdvancedButton_Click(object sender, RoutedEventArgs e)
        {
            if (Application.Current.Windows.Cast<Window>().Any(x => x is AdvancedWindow)) return;
            var monitor = ((FrameworkElement)sender).DataContext as MonitorData;
            var window = new AdvancedWindow(monitor)
            {
                Owner = this
            };

            void CloseWindow(object o, EventArgs e2) => window.Close();

            SystemEvents.DisplaySettingsChanged += CloseWindow;
            if (window.ShowDialog() == false) return;
            SystemEvents.DisplaySettingsChanged -= CloseWindow;

            if (window.ChangedCalibration)
            {
                _viewModel.SaveConfig();
                monitor?.ReapplyClamp();
            }

            if (window.ChangedDither)
            {
                monitor?.ApplyDither(window.DitherState.SelectedIndex, Math.Max(window.DitherBits.SelectedIndex, 0),
                    Math.Max(window.DitherMode.SelectedIndex, 0));
            }
        }

        private void ReapplyButton_Click(object sender, RoutedEventArgs e)
        {
            ReapplyMonitorSettings();
        }

        private void InitializeTrayIcon()
        {
            var notifyIcon = new NotifyIcon
            {
                Text = "Novideo sRGB",
                Icon = Properties.Resources.icon,
                Visible = true
            };

            notifyIcon.MouseDoubleClick +=
                delegate
                {
                    Show();
                    WindowState = WindowState.Normal;
                };

            _contextMenu = new ContextMenu();

            _contextMenu.Popup += delegate { UpdateContextMenu(); };

            notifyIcon.ContextMenu = _contextMenu;

            Closed += delegate { notifyIcon.Dispose(); };
        }

        private void UpdateContextMenu()
        {
            _contextMenu.MenuItems.Clear();

            foreach (var monitor in _viewModel.Monitors)
            {
                var item = new MenuItem();
                _contextMenu.MenuItems.Add(item);
                item.Text = monitor.Name;
                item.Checked = monitor.Clamped;
                item.Enabled = monitor.CanClamp;
                item.Click += (sender, args) => monitor.Clamped = !monitor.Clamped;
            }

            _contextMenu.MenuItems.Add("-");

            var reapplyItem = new MenuItem();
            _contextMenu.MenuItems.Add(reapplyItem);
            reapplyItem.Text = "Reapply";
            reapplyItem.Click += delegate { ReapplyMonitorSettings(); };

            var exitItem = new MenuItem();
            _contextMenu.MenuItems.Add(exitItem);
            exitItem.Text = "Exit";
            exitItem.Click += delegate { Close(); };
        }

        private void ReapplyMonitorSettings()
        {
            foreach (var monitor in _viewModel.Monitors)
            {
                monitor.ReapplyClamp();
            }
        }
    }
}