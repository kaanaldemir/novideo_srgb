using Microsoft.Win32;
using System;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Forms;
using Application = System.Windows.Application;
using MessageBox = System.Windows.Forms.MessageBox;
using System.Reflection;
using System.Runtime.InteropServices;
using System.ComponentModel;
using System.Windows.Interop;

namespace novideo_srgb
{
    public partial class MainWindow : Window
    {
        // Windows API for hotkeys
        [DllImport("user32.dll")]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll")]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        // Modifiers
        private const uint MOD_ALT = 0x0001;
        private const uint MOD_CONTROL = 0x0002;
        private const uint MOD_SHIFT = 0x0004;
        private const uint MOD_WIN = 0x0008;
        private const uint MOD_NOREPEAT = 0x4000;

        // Hotkey ID
        private const int HOTKEY_ID = 9000;

        // Windows message for hotkey
        private const int WM_HOTKEY = 0x0312;

        private readonly MainViewModel _viewModel;

        private ContextMenu _contextMenu;
        
        // Store the current hotkey configuration
        private uint _hotkeyModifiers;
        private uint _hotkeyKey;
        private bool _hotkeyRegistered = false;

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
            
            // Add mouse click handler - single click will open the menu
            notifyIcon.MouseClick += (sender, args) => 
            {
                // Only respond to left clicks, as right clicks show the context menu automatically
                if (args.Button == MouseButtons.Left)
                {
                    UpdateContextMenu();
                    // No need to explicitly call Show - just trigger the popup through the tray icon
                    MethodInfo method = typeof(NotifyIcon).GetMethod("ShowContextMenu", 
                        BindingFlags.Instance | BindingFlags.NonPublic);
                    if (method != null)
                        method.Invoke(notifyIcon, null);
                }
            };

            Closed += delegate { notifyIcon.Dispose(); };
        }

        private void UpdateContextMenu()
        {
            _contextMenu.MenuItems.Clear();

            foreach (var monitor in _viewModel.Monitors)
            {
                var monitorMenuItem = new MenuItem();
                _contextMenu.MenuItems.Add(monitorMenuItem);
                monitorMenuItem.Text = monitor.Name;
                
                for (int i = 0; i < monitor.Profiles.Count; i++)
                {
                    var profile = monitor.Profiles[i];
                    var profileItem = new MenuItem();
                    monitorMenuItem.MenuItems.Add(profileItem);
                    profileItem.Text = profile.Name;
                    profileItem.Checked = monitor.Clamped && monitor.SelectedProfileIndex == i;
                    profileItem.Enabled = monitor.CanClamp;
                    
                    int capturedIndex = i;
                    profileItem.Click += (sender, args) => 
                    {
                        monitor.SelectedProfileIndex = capturedIndex;
                        monitor.Clamped = true;
                    };
                }
                
                var offItem = new MenuItem();
                monitorMenuItem.MenuItems.Add(offItem);
                offItem.Text = "Off";
                offItem.Checked = !monitor.Clamped;
                offItem.Click += (sender, args) => monitor.Clamped = false;
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

        // Override WndProc to capture hotkey messages
        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            var source = PresentationSource.FromVisual(this) as HwndSource;
            source?.AddHook(WndProc);
            
            // Load and register saved hotkey
            LoadHotkey();
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
            
            // Only register if we have a valid key
            if (_hotkeyKey == 0) return false;
            
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

        private void HotkeyButton_Click(object sender, RoutedEventArgs e)
        {
            var hotkeyWindow = new HotkeyWindow(_hotkeyModifiers, _hotkeyKey)
            {
                Owner = this
            };

            if (hotkeyWindow.ShowDialog() == true)
            {
                // Unregister existing hotkey
                UnregisterGlobalHotkey();
                
                // Save new hotkey
                _hotkeyModifiers = hotkeyWindow.HotkeyModifiers;
                _hotkeyKey = hotkeyWindow.HotkeyKey;
                
                // Register new hotkey
                if (RegisterGlobalHotkey())
                {
                    // Save hotkey to settings
                    _viewModel.HotkeyModifiers = _hotkeyModifiers;
                    _viewModel.HotkeyKey = _hotkeyKey;
                    _viewModel.SaveConfig();
                    MessageBox.Show($"Hotkey registered successfully!", "Success", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                else
                {
                    MessageBox.Show($"Failed to register hotkey. It might be in use by another application.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        private void LoadHotkey()
        {
            // Load hotkey from settings
            _hotkeyModifiers = _viewModel.HotkeyModifiers;
            _hotkeyKey = _viewModel.HotkeyKey;
            
            // Ensure we have the NOREPEAT flag
            if (_hotkeyModifiers != 0)
            {
                _hotkeyModifiers |= MOD_NOREPEAT;
            }
            
            // Register hotkey if we have one
            if (_hotkeyKey != 0)
            {
                RegisterGlobalHotkey();
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);
            
            // Unregister hotkey when application closes
            UnregisterGlobalHotkey();
        }
    }
}