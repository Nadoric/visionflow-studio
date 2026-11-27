using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using HalconVisionStudio.Shared;

namespace HalconVisionStudio
{
    /// <summary>
    /// 使用 Windows 原生标题栏和窗口按钮，内容区采用玻璃主题。
    /// </summary>
    public partial class MainWindow : Window
    {
        private const int ImmersiveDarkModeAttribute = 20;
        private const int WindowCornerPreferenceAttribute = 33;
        private const int CaptionColorAttribute = 35;
        private const int TextColorAttribute = 36;
        private const int SystemBackdropTypeAttribute = 38;

        public MainWindow(MainViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;

            SourceInitialized += OnSourceInitialized;
            Closed += OnWindowClosed;
            AppThemeManager.ThemeChanged += OnThemeChanged;
        }

        private void OnSourceInitialized(object sender, EventArgs e)
        {
            ApplySystemAppearance();
        }

        private void OnThemeChanged(object sender, bool isDark)
        {
            ApplySystemAppearance();
        }

        private void OnWindowClosed(object sender, EventArgs e)
        {
            AppThemeManager.ThemeChanged -= OnThemeChanged;
        }

        private void ApplySystemAppearance()
        {
            // 较早的 Windows 版本继续使用 XAML 玻璃渐变，无需透明分层窗口，
            // 避免影响 HALCON 的原生显示句柄和硬件加速。
            if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000))
            {
                return;
            }

            var handle = new WindowInteropHelper(this).Handle;
            if (handle == IntPtr.Zero)
            {
                return;
            }

            var darkMode = AppThemeManager.IsDarkTheme ? 1 : 0;
            DwmSetWindowAttribute(handle, ImmersiveDarkModeAttribute, ref darkMode, sizeof(int));

            var roundedCorners = 2;
            DwmSetWindowAttribute(
                handle, WindowCornerPreferenceAttribute, ref roundedCorners, sizeof(int));

            if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22621))
            {
                // DWMSBT_TRANSIENTWINDOW：使用系统提供的 Acrylic 背景。
                var acrylicBackdrop = 3;
                DwmSetWindowAttribute(
                    handle, SystemBackdropTypeAttribute, ref acrylicBackdrop, sizeof(int));
            }

            ApplySystemColor(handle, CaptionColorAttribute, "Glass.Caption");
            ApplySystemColor(handle, TextColorAttribute, "Theme.Vision.Text.Primary");
        }

        private void ApplySystemColor(IntPtr handle, int attribute, string resourceKey)
        {
            if (TryFindResource(resourceKey) is not SolidColorBrush brush)
            {
                return;
            }

            // COLORREF 使用 0x00BBGGRR 字节顺序。原生标题栏只接受纯色。
            var color = brush.Color;
            var colorRef = color.R | (color.G << 8) | (color.B << 16);
            DwmSetWindowAttribute(handle, attribute, ref colorRef, sizeof(int));
        }

        private void ToggleThemeButton_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            AppThemeManager.ToggleTheme();
        }

        [DllImport("dwmapi.dll", PreserveSig = true)]
        private static extern int DwmSetWindowAttribute(
            IntPtr handle,
            int attribute,
            ref int value,
            int valueSize);
    }
}
