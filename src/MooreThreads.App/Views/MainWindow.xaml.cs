using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using MooreThreadsUpScaler.ViewModels;

namespace MooreThreadsUpScaler.Views
{
    public partial class MainWindow : Window
    {
        private const int  HotkeyId    = 1;
        private const uint MOD_CONTROL = 0x0002;
        private const uint MOD_ALT     = 0x0001;
        private const uint VK_S        = 0x53;

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint mods, uint vk);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        private MainViewModel ViewModel => (MainViewModel)DataContext;

        public MainWindow()
        {
            InitializeComponent();
            DataContext = new MainViewModel();
            Closing += (_, _) => ViewModel.Dispose();
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            var helper = new WindowInteropHelper(this);
            RegisterHotKey(helper.Handle, HotkeyId, MOD_CONTROL | MOD_ALT, VK_S);
            HwndSource.FromHwnd(helper.Handle)?.AddHook(WndProc);
        }

        protected override void OnClosed(EventArgs e)
        {
            var helper = new WindowInteropHelper(this);
            UnregisterHotKey(helper.Handle, HotkeyId);
            base.OnClosed(e);
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            const int WM_HOTKEY = 0x0312;
            if (msg == WM_HOTKEY && wParam.ToInt32() == HotkeyId)
            {
                ViewModel.ToggleScalingCommand.Execute(null);
                handled = true;
            }
            return IntPtr.Zero;
        }

        private void ProfileListBox_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (ViewModel.SelectedProfile is null) return;
            
            var inputDialog = new InputDialog("Rename Profile", "Enter new profile name:", ViewModel.SelectedProfile.DisplayName);
            if (inputDialog.ShowDialog() == true && !string.IsNullOrWhiteSpace(inputDialog.Answer))
            {
                ViewModel.RenameSelectedProfile(inputDialog.Answer);
            }
        }
    }

    public class InputDialog : Window
    {
        public string Answer { get; private set; } = string.Empty;
        
        public InputDialog(string title, string prompt, string defaultValue = "")
        {
            Title = title;
            Width = 350;
            Height = 150;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ResizeMode = ResizeMode.NoResize;
            
            var stack = new System.Windows.Controls.StackPanel { Margin = new Thickness(15) };
            
            var label = new TextBlock { Text = prompt, Margin = new Thickness(0, 0, 0, 10) };
            stack.Children.Add(label);
            
            var textBox = new System.Windows.Controls.TextBox { Text = defaultValue, Margin = new Thickness(0, 0, 0, 15) };
            textBox.SelectAll();
            stack.Children.Add(textBox);
            
            var btnPanel = new System.Windows.Controls.StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, HorizontalAlignment = System.Windows.HorizontalAlignment.Right };
            
            var okBtn = new System.Windows.Controls.Button { Content = "OK", Width = 70, Margin = new Thickness(0, 0, 5, 0), IsDefault = true };
            okBtn.Click += (s, e) => { Answer = textBox.Text; DialogResult = true; };
            btnPanel.Children.Add(okBtn);
            
            var cancelBtn = new System.Windows.Controls.Button { Content = "Cancel", Width = 70 };
            cancelBtn.Click += (s, e) => { DialogResult = false; };
            btnPanel.Children.Add(cancelBtn);
            
            stack.Children.Add(btnPanel);
            
            Content = stack;
            
            Loaded += (s, e) => textBox.Focus();
        }
    }
}
