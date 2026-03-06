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
        private Key _selectedKey;

        public HotkeyWindow(uint currentModifiers, uint currentKey)
        {
            InitializeComponent();

            CtrlCheckBox.IsChecked = (currentModifiers & ModControl) != 0;
            AltCheckBox.IsChecked = (currentModifiers & ModAlt) != 0;
            ShiftCheckBox.IsChecked = (currentModifiers & ModShift) != 0;
            WinCheckBox.IsChecked = (currentModifiers & ModWin) != 0;

            _selectedKey = currentKey == 0 ? Key.None : KeyInterop.KeyFromVirtualKey((int)currentKey);
            UpdateHotkeyText();
        }

        public uint HotkeyModifiers { get; private set; }

        public uint HotkeyKey { get; private set; }

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
            HotkeyModifiers = 0;
            if (CtrlCheckBox.IsChecked == true) HotkeyModifiers |= ModControl;
            if (AltCheckBox.IsChecked == true) HotkeyModifiers |= ModAlt;
            if (ShiftCheckBox.IsChecked == true) HotkeyModifiers |= ModShift;
            if (WinCheckBox.IsChecked == true) HotkeyModifiers |= ModWin;

            HotkeyKey = _selectedKey == Key.None ? 0u : (uint)KeyInterop.VirtualKeyFromKey(_selectedKey);
            DialogResult = true;
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
    }
}
