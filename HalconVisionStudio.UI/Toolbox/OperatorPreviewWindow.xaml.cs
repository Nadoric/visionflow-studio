using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Navigation;
using HalconVisionStudio.Shared;

namespace HalconVisionStudio.UI.Controls
{
    public partial class OperatorPreviewWindow : Window
    {
        internal OperatorPreviewWindow(IHandleRegisterModel model)
        {
            InitializeComponent();
            DataContext = new OperatorPreview(model);
            NativeOperatorPicker.SelectedIndex = 0;
        }

        private void ReferenceLink_RequestNavigate(object sender, RequestNavigateEventArgs e)
        {
            e.Handled = true;

            try
            {
                Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri)
                {
                    UseShellExecute = true
                })?.Dispose();
            }
            catch (Win32Exception)
            {
                MessageBox.Show(this, "无法打开默认浏览器。", "HALCON 文档",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
    }
}
