using System;
using System.Text;
using System.Windows;
using System.Windows.Input;
using System.Windows.Controls;
using System.Windows.Media;
using MessageBox = System.Windows.MessageBox;

namespace novideo_srgb
{
    public class HotkeyWindow : Window
    {
        public uint HotkeyModifiers { get; private set; }
        public uint HotkeyKey { get; private set; }
        
        private const uint MOD_ALT = 0x0001;
        private const uint MOD_CONTROL = 0x0002;
        private const uint MOD_SHIFT = 0x0004;
        private const uint MOD_WIN = 0x0008;
        private const uint MOD_NOREPEAT = 0x4000;
        
        private Key _selectedKey;
        private string _keyName;
        
        private TextBox _hotkeyTextBox;
        private CheckBox _modAltCheckbox;
        private CheckBox _modCtrlCheckbox;
        private CheckBox _modShiftCheckbox;
        
        public HotkeyWindow(uint currentModifiers, uint currentKey)
        {
            // Configure the window
            Title = "Configure Hotkey";
            Width = 400;
            Height = 225;
            ResizeMode = ResizeMode.NoResize;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            
            HotkeyModifiers = currentModifiers;
            HotkeyKey = currentKey;
            
            // Create the main layout
            var mainStackPanel = new StackPanel { Margin = new Thickness(10) };
            Content = mainStackPanel;
            
            // Create the instruction text
            var instructionText = new TextBlock
            {
                Text = "Press the key combination you want to use to toggle clamping:",
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 10)
            };
            mainStackPanel.Children.Add(instructionText);
            
            // Create the hotkey textbox
            _hotkeyTextBox = new TextBox
            {
                Height = 30,
                IsReadOnly = true,
                Text = "Click here and press a key...",
                HorizontalContentAlignment = HorizontalAlignment.Center,
                VerticalContentAlignment = VerticalAlignment.Center,
                FontWeight = FontWeights.Bold
            };
            _hotkeyTextBox.GotFocus += HotkeyTextBox_GotFocus;
            _hotkeyTextBox.KeyDown += HotkeyTextBox_KeyDown;
            mainStackPanel.Children.Add(_hotkeyTextBox);
            
            // Create the modifier checkboxes
            _modAltCheckbox = new CheckBox
            {
                Content = "Alt",
                Margin = new Thickness(0, 10, 0, 0),
                IsChecked = (currentModifiers & MOD_ALT) != 0
            };
            _modAltCheckbox.Checked += Modifier_Checked;
            _modAltCheckbox.Unchecked += Modifier_Checked;
            mainStackPanel.Children.Add(_modAltCheckbox);
            
            _modCtrlCheckbox = new CheckBox
            {
                Content = "Ctrl",
                Margin = new Thickness(0, 5, 0, 0),
                IsChecked = (currentModifiers & MOD_CONTROL) != 0
            };
            _modCtrlCheckbox.Checked += Modifier_Checked;
            _modCtrlCheckbox.Unchecked += Modifier_Checked;
            mainStackPanel.Children.Add(_modCtrlCheckbox);
            
            _modShiftCheckbox = new CheckBox
            {
                Content = "Shift",
                Margin = new Thickness(0, 5, 0, 0),
                IsChecked = (currentModifiers & MOD_SHIFT) != 0
            };
            _modShiftCheckbox.Checked += Modifier_Checked;
            _modShiftCheckbox.Unchecked += Modifier_Checked;
            mainStackPanel.Children.Add(_modShiftCheckbox);
            
            // Create the button panel
            var buttonPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 10, 0, 0)
            };
            mainStackPanel.Children.Add(buttonPanel);
            
            // Create OK button
            var okButton = new Button
            {
                Content = "OK",
                Width = 75,
                IsDefault = true
            };
            okButton.Click += OkButton_Click;
            buttonPanel.Children.Add(okButton);
            
            // Create Cancel button
            var cancelButton = new Button
            {
                Content = "Cancel",
                Width = 75,
                Margin = new Thickness(10, 0, 0, 0),
                IsCancel = true
            };
            buttonPanel.Children.Add(cancelButton);
            
            UpdateTextDisplay();
        }
        
        private void HotkeyTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            // We're only interested in the actual key, not the modifiers
            if (e.Key == Key.System || e.Key == Key.LeftAlt || e.Key == Key.RightAlt ||
                e.Key == Key.LeftCtrl || e.Key == Key.RightCtrl ||
                e.Key == Key.LeftShift || e.Key == Key.RightShift ||
                e.Key == Key.LWin || e.Key == Key.RWin)
            {
                return;
            }
            
            _selectedKey = e.Key;
            // Convert WPF Key to Windows virtual key code
            HotkeyKey = (uint)KeyInterop.VirtualKeyFromKey(_selectedKey);
            _keyName = _selectedKey.ToString();
            
            UpdateTextDisplay();
            e.Handled = true;
        }
        
        private void HotkeyTextBox_GotFocus(object sender, RoutedEventArgs e)
        {
            if (_hotkeyTextBox.Text == "Click here and press a key...")
            {
                _hotkeyTextBox.Text = "";
            }
        }
        
        private void Modifier_Checked(object sender, RoutedEventArgs e)
        {
            // Update modifiers based on checkboxes
            HotkeyModifiers = 0;
            
            if (_modAltCheckbox.IsChecked == true) HotkeyModifiers |= MOD_ALT;
            if (_modCtrlCheckbox.IsChecked == true) HotkeyModifiers |= MOD_CONTROL;
            if (_modShiftCheckbox.IsChecked == true) HotkeyModifiers |= MOD_SHIFT;
            
            // Always add MOD_NOREPEAT to prevent multiple triggers
            HotkeyModifiers |= MOD_NOREPEAT;
            
            UpdateTextDisplay();
        }
        
        private void UpdateTextDisplay()
        {
            if (HotkeyKey == 0)
            {
                _hotkeyTextBox.Text = "Click here and press a key...";
                return;
            }
            
            StringBuilder sb = new StringBuilder();
            
            if ((HotkeyModifiers & MOD_CONTROL) != 0) sb.Append("Ctrl + ");
            if ((HotkeyModifiers & MOD_ALT) != 0) sb.Append("Alt + ");
            if ((HotkeyModifiers & MOD_SHIFT) != 0) sb.Append("Shift + ");
            
            sb.Append(_keyName ?? KeyInterop.KeyFromVirtualKey((int)HotkeyKey).ToString());
            
            _hotkeyTextBox.Text = sb.ToString();
        }
        
        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            if (HotkeyKey == 0)
            {
                MessageBox.Show("Please select a key for the hotkey combination.", "Missing Key", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            
            DialogResult = true;
        }
    }
} 