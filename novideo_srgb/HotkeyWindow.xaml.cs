using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Input;
using FormsKeys = System.Windows.Forms.Keys;
using FormsKeysConverter = System.Windows.Forms.KeysConverter;

namespace novideo_srgb
{
    public partial class HotkeyWindow
    {
        public const uint ModAlt = 0x0001;
        public const uint ModControl = 0x0002;
        public const uint ModShift = 0x0004;
        public const uint ModWin = 0x0008;

        private readonly FormsKeysConverter _keysConverter = new FormsKeysConverter();
        private readonly EditableHotkey _combinedHotkey;
        private readonly List<EditableHotkey> _monitorHotkeys;
        private EditableHotkey _currentHotkey;
        private Key _selectedKey;
        private bool _loadingHotkey;

        public HotkeyWindow(bool useCombinedHotkey, uint currentModifiers, uint currentKey,
            IEnumerable<MonitorHotkey> monitorHotkeys)
        {
            InitializeComponent();

            _combinedHotkey = new EditableHotkey
            {
                Name = "All monitors",
                Modifiers = currentModifiers,
                Key = currentKey,
            };
            _monitorHotkeys = monitorHotkeys
                .Select(x => new EditableHotkey
                {
                    Path = x.Path,
                    Name = x.Name,
                    Modifiers = x.HotkeyModifiers,
                    Key = x.HotkeyKey,
                })
                .ToList();

            MonitorComboBox.ItemsSource = _monitorHotkeys;
            if (_monitorHotkeys.Count > 0)
            {
                MonitorComboBox.SelectedIndex = 0;
            }

            CombinedHotkeyCheckBox.IsChecked = useCombinedHotkey;
            ApplyHotkeyMode();
        }

        public bool UseCombinedHotkey { get; private set; }

        public uint HotkeyModifiers { get; private set; }

        public uint HotkeyKey { get; private set; }

        public List<MonitorHotkey> MonitorHotkeys { get; private set; }

        private void CombinedHotkeyCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            if (!IsInitialized)
            {
                return;
            }

            CommitCurrentHotkey();
            ApplyHotkeyMode();
        }

        private void MonitorComboBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (_loadingHotkey)
            {
                return;
            }

            CommitCurrentHotkey();
            LoadHotkey(MonitorComboBox.SelectedItem as EditableHotkey);
        }

        private void HotkeyTextBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            var key = e.Key == Key.System ? e.SystemKey : e.Key;
            if (IsModifierKey(key))
            {
                e.Handled = true;
                return;
            }

            _selectedKey = key;
            UpdateHotkeyText();
            e.Handled = true;
        }

        private void HotkeyTextBox_GotKeyboardFocus(object sender, RoutedEventArgs e)
        {
            HotkeyTextBox.SelectAll();
        }

        private void ModifierCheckBox_Click(object sender, RoutedEventArgs e)
        {
            UpdateHotkeyText();
        }

        private void ClearButton_Click(object sender, RoutedEventArgs e)
        {
            CtrlCheckBox.IsChecked = false;
            AltCheckBox.IsChecked = false;
            ShiftCheckBox.IsChecked = false;
            WinCheckBox.IsChecked = false;
            _selectedKey = Key.None;
            UpdateHotkeyText();
        }

        private void OK_Click(object sender, RoutedEventArgs e)
        {
            CommitCurrentHotkey();

            UseCombinedHotkey = CombinedHotkeyCheckBox.IsChecked == true;
            HotkeyModifiers = _combinedHotkey.Modifiers;
            HotkeyKey = _combinedHotkey.Key;
            MonitorHotkeys = _monitorHotkeys
                .Select(x => new MonitorHotkey(x.Path, x.Name, x.Modifiers, x.Key))
                .ToList();

            DialogResult = true;
        }

        private void ApplyHotkeyMode()
        {
            var useCombinedHotkey = CombinedHotkeyCheckBox.IsChecked == true;
            MonitorComboBox.Visibility = useCombinedHotkey ? Visibility.Collapsed : Visibility.Visible;
            TargetTextBlock.Text = useCombinedHotkey
                ? "Press the combined hotkey, then choose any modifiers."
                : "Choose a monitor, then press its hotkey and modifiers.";

            if (useCombinedHotkey)
            {
                LoadHotkey(_combinedHotkey);
                return;
            }

            LoadHotkey(MonitorComboBox.SelectedItem as EditableHotkey);
        }

        private void LoadHotkey(EditableHotkey hotkey)
        {
            _loadingHotkey = true;
            try
            {
                _currentHotkey = hotkey;
                var enabled = hotkey != null;

                CtrlCheckBox.IsEnabled = enabled;
                AltCheckBox.IsEnabled = enabled;
                ShiftCheckBox.IsEnabled = enabled;
                WinCheckBox.IsEnabled = enabled;
                HotkeyTextBox.IsEnabled = enabled;

                var modifiers = hotkey == null ? 0 : hotkey.Modifiers;
                CtrlCheckBox.IsChecked = (modifiers & ModControl) != 0;
                AltCheckBox.IsChecked = (modifiers & ModAlt) != 0;
                ShiftCheckBox.IsChecked = (modifiers & ModShift) != 0;
                WinCheckBox.IsChecked = (modifiers & ModWin) != 0;

                _selectedKey = hotkey == null || hotkey.Key == 0
                    ? Key.None
                    : KeyInterop.KeyFromVirtualKey((int)hotkey.Key);
                UpdateHotkeyText();
            }
            finally
            {
                _loadingHotkey = false;
            }
        }

        private void CommitCurrentHotkey()
        {
            if (_currentHotkey == null)
            {
                return;
            }

            _currentHotkey.Modifiers = BuildModifiers();
            _currentHotkey.Key = _selectedKey == Key.None ? 0u : (uint)KeyInterop.VirtualKeyFromKey(_selectedKey);
        }

        private uint BuildModifiers()
        {
            uint modifiers = 0;
            if (CtrlCheckBox.IsChecked == true) modifiers |= ModControl;
            if (AltCheckBox.IsChecked == true) modifiers |= ModAlt;
            if (ShiftCheckBox.IsChecked == true) modifiers |= ModShift;
            if (WinCheckBox.IsChecked == true) modifiers |= ModWin;
            return modifiers;
        }

        private void UpdateHotkeyText()
        {
            HotkeyTextBox.Text = BuildHotkeyText();
        }

        private string BuildHotkeyText()
        {
            if (_selectedKey == Key.None)
            {
                return "Not set";
            }

            var builder = new StringBuilder();
            if (CtrlCheckBox.IsChecked == true) builder.Append("Ctrl + ");
            if (AltCheckBox.IsChecked == true) builder.Append("Alt + ");
            if (ShiftCheckBox.IsChecked == true) builder.Append("Shift + ");
            if (WinCheckBox.IsChecked == true) builder.Append("Win + ");

            var virtualKey = KeyInterop.VirtualKeyFromKey(_selectedKey);
            builder.Append(_keysConverter.ConvertToString((FormsKeys)virtualKey));
            return builder.ToString();
        }

        private static bool IsModifierKey(Key key)
        {
            return key == Key.LeftCtrl || key == Key.RightCtrl ||
                   key == Key.LeftAlt || key == Key.RightAlt ||
                   key == Key.LeftShift || key == Key.RightShift ||
                   key == Key.LWin || key == Key.RWin;
        }

        private sealed class EditableHotkey
        {
            public string Path { get; set; }

            public string Name { get; set; }

            public uint Modifiers { get; set; }

            public uint Key { get; set; }
        }

        public sealed class MonitorHotkey
        {
            public MonitorHotkey(string path, string name, uint hotkeyModifiers, uint hotkeyKey)
            {
                Path = path;
                Name = name;
                HotkeyModifiers = hotkeyModifiers;
                HotkeyKey = hotkeyKey;
            }

            public string Path { get; }

            public string Name { get; }

            public uint HotkeyModifiers { get; }

            public uint HotkeyKey { get; }
        }
    }
}
