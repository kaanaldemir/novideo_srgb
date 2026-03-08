using Microsoft.Win32;
using System;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
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
        private const int WM_KEYDOWN = 0x0100;
        private const int WM_KEYUP = 0x0101;
        private const int WM_SYSKEYDOWN = 0x0104;
        private const int WM_SYSKEYUP = 0x0105;
        private const int HotkeyId = 9000;
        private const int WhKeyboardLl = 13;
        private const uint ModNoRepeat = 0x4000;
        private const int VkLeftControl = 0xA2;
        private const int VkRightControl = 0xA3;
        private const int VkLeftMenu = 0xA4;
        private const int VkRightMenu = 0xA5;
        private const int VkLeftShift = 0xA0;
        private const int VkRightShift = 0xA1;
        private const int VkLeftWin = 0x5B;
        private const int VkRightWin = 0x5C;
        private const long HotkeyDuplicateSuppressionTicks = TimeSpan.TicksPerMillisecond * 150;

        private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

        [StructLayout(LayoutKind.Sequential)]
        private struct KbdLlHookStruct
        {
            public uint vkCode;
            public uint scanCode;
            public uint flags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [DllImport("user32.dll")]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll")]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod,
            uint dwThreadId);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll")]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int vKey);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr GetModuleHandle(string lpModuleName);

        private readonly MainViewModel _viewModel;
        private readonly LowLevelKeyboardProc _keyboardHookProc;
        private FormsContextMenu _contextMenu;
        private NotifyIcon _notifyIcon;
        private HwndSource _windowSource;
        private bool _hotkeyRegistered;
        private IntPtr _keyboardHookHandle;
        private bool _keyboardHookArmed;
        private long _lastHotkeyTriggerTicks;

        public MainWindow()
        {
            if (Process.GetProcessesByName(Process.GetCurrentProcess().ProcessName).Length > 1)
            {
                MessageBox.Show("Already running!");
                Close();
                return;
            }

            _keyboardHookProc = KeyboardHookProc;
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
                HandleHotkeyTrigger();
                handled = true;
            }

            return IntPtr.Zero;
        }

        private IntPtr KeyboardHookProc(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0 && _hotkeyRegistered && _viewModel.HotkeyKey != 0)
            {
                var message = wParam.ToInt32();
                var hookData = Marshal.PtrToStructure<KbdLlHookStruct>(lParam);

                if (message == WM_KEYDOWN || message == WM_SYSKEYDOWN)
                {
                    if (hookData.vkCode == _viewModel.HotkeyKey && !_keyboardHookArmed &&
                        AreExactHotkeyModifiersPressed())
                    {
                        _keyboardHookArmed = true;
                        HandleHotkeyTrigger();
                    }
                }
                else if (message == WM_KEYUP || message == WM_SYSKEYUP)
                {
                    if (hookData.vkCode == _viewModel.HotkeyKey)
                    {
                        _keyboardHookArmed = false;
                    }
                }
            }

            return CallNextHookEx(_keyboardHookHandle, nCode, wParam, lParam);
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
            if (_hotkeyRegistered)
            {
                InstallKeyboardHook();
            }

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
            UninstallKeyboardHook();

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

        private void InstallKeyboardHook()
        {
            if (_keyboardHookHandle != IntPtr.Zero)
            {
                return;
            }

            _keyboardHookArmed = false;

            var module = Process.GetCurrentProcess().MainModule;
            var moduleHandle = module == null ? IntPtr.Zero : GetModuleHandle(module.ModuleName);
            _keyboardHookHandle = SetWindowsHookEx(WhKeyboardLl, _keyboardHookProc, moduleHandle, 0);
        }

        private void UninstallKeyboardHook()
        {
            _keyboardHookArmed = false;

            if (_keyboardHookHandle == IntPtr.Zero)
            {
                return;
            }

            UnhookWindowsHookEx(_keyboardHookHandle);
            _keyboardHookHandle = IntPtr.Zero;
        }

        private void HandleHotkeyTrigger()
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action(HandleHotkeyTrigger));
                return;
            }

            if (!TryClaimHotkeyTrigger())
            {
                return;
            }

            ToggleAllMonitorClamping();
        }

        private bool TryClaimHotkeyTrigger()
        {
            var now = DateTime.UtcNow.Ticks;
            while (true)
            {
                var lastTrigger = Interlocked.Read(ref _lastHotkeyTriggerTicks);
                if (now - lastTrigger < HotkeyDuplicateSuppressionTicks)
                {
                    return false;
                }

                if (Interlocked.CompareExchange(ref _lastHotkeyTriggerTicks, now, lastTrigger) == lastTrigger)
                {
                    return true;
                }
            }
        }

        private bool AreExactHotkeyModifiersPressed()
        {
            return IsModifierPressed(HotkeyWindow.ModControl) == HasHotkeyModifier(HotkeyWindow.ModControl) &&
                   IsModifierPressed(HotkeyWindow.ModAlt) == HasHotkeyModifier(HotkeyWindow.ModAlt) &&
                   IsModifierPressed(HotkeyWindow.ModShift) == HasHotkeyModifier(HotkeyWindow.ModShift) &&
                   IsModifierPressed(HotkeyWindow.ModWin) == HasHotkeyModifier(HotkeyWindow.ModWin);
        }

        private bool HasHotkeyModifier(uint modifier)
        {
            return (_viewModel.HotkeyModifiers & modifier) != 0;
        }

        private static bool IsModifierPressed(uint modifier)
        {
            switch (modifier)
            {
                case HotkeyWindow.ModControl:
                    return IsVirtualKeyDown(VkLeftControl) || IsVirtualKeyDown(VkRightControl);
                case HotkeyWindow.ModAlt:
                    return IsVirtualKeyDown(VkLeftMenu) || IsVirtualKeyDown(VkRightMenu);
                case HotkeyWindow.ModShift:
                    return IsVirtualKeyDown(VkLeftShift) || IsVirtualKeyDown(VkRightShift);
                case HotkeyWindow.ModWin:
                    return IsVirtualKeyDown(VkLeftWin) || IsVirtualKeyDown(VkRightWin);
                default:
                    return false;
            }
        }

        private static bool IsVirtualKeyDown(int virtualKey)
        {
            return (GetAsyncKeyState(virtualKey) & 0x8000) != 0;
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
