using System.Windows.Controls;
using HalconVisionStudio.Views.Communication;
using HalconVisionStudio.Views.Logic;
using HalconVisionStudio.Views.Logs;

namespace HalconVisionStudio.Views.Components;

public partial class MainMenuBar : UserControl
{
    private CommunicationManagerWindow communicationManagerWindow;
    private SystemSettingsWindow systemSettingsWindow;
    private VariableManagerWindow variableManagerWindow;
    private ExecutionLogWindow executionLogWindow;

    public MainMenuBar()
    {
        InitializeComponent();
    }

    /// <summary>打开全局变量管理窗口（已打开时置前）。</summary>
    private void OnOpenVariableManager(object sender, System.Windows.RoutedEventArgs e)
    {
        if (variableManagerWindow == null || !variableManagerWindow.IsLoaded)
        {
            variableManagerWindow = new VariableManagerWindow
            {
                Owner = System.Windows.Window.GetWindow(this)
            };
            variableManagerWindow.Show();
        }
        else
        {
            variableManagerWindow.Activate();
        }
    }

    /// <summary>打开执行日志窗口（已打开时置前）。</summary>
    private void OnOpenExecutionLog(object sender, System.Windows.RoutedEventArgs e)
    {
        if (executionLogWindow == null || !executionLogWindow.IsLoaded)
        {
            executionLogWindow = new ExecutionLogWindow
            {
                Owner = System.Windows.Window.GetWindow(this)
            };
            executionLogWindow.Show();
        }
        else
        {
            executionLogWindow.Activate();
        }
    }

    /// <summary>打开系统设置窗口（已打开时置前）。</summary>
    private void OnOpenSystemSettings(object sender, System.Windows.RoutedEventArgs e)
    {
        if (systemSettingsWindow == null || !systemSettingsWindow.IsLoaded)
        {
            systemSettingsWindow = new SystemSettingsWindow
            {
                Owner = System.Windows.Window.GetWindow(this),
                DataContext = DataContext
            };
            systemSettingsWindow.Show();
        }
        else
        {
            systemSettingsWindow.Activate();
        }
    }

    /// <summary>打开通讯驱动管理窗口（已打开时置前）。</summary>
    private void OnOpenCommunicationManager(object sender, System.Windows.RoutedEventArgs e)
    {
        if (communicationManagerWindow == null || !communicationManagerWindow.IsLoaded)
        {
            communicationManagerWindow = new CommunicationManagerWindow
            {
                Owner = System.Windows.Window.GetWindow(this)
            };
            communicationManagerWindow.Show();
        }
        else
        {
            communicationManagerWindow.Activate();
        }
    }
    /// <summary>打开快捷键帮助说明。</summary>
    private void OnOpenShortcutsHelp(object sender, System.Windows.RoutedEventArgs e)
    {
        var msg = "【全局常用快捷键指南】\n\n" +
                  "• 方案操作：\n" +
                  "   Ctrl + N : 新建方案\n" +
                  "   Ctrl + O : 打开方案\n" +
                  "   Ctrl + S : 保存方案\n" +
                  "   Ctrl + Shift + S : 方案另存为\n\n" +
                  "• 编辑与画布：\n" +
                  "   Ctrl + Z : 撤销\n" +
                  "   Ctrl + Y : 重做\n" +
                  "   Delete : 删除选中节点\n" +
                  "   Back : 删除选中连线\n" +
                  "   Ctrl + L : 自动布局并居中\n" +
                  "   左键拖动空白处 / 中键拖动 : 平移画布\n" +
                  "   Shift + 左键拖动空白处 : 框选节点\n" +
                  "   Ctrl + + / - : 放大 / 缩小画布\n" +
                  "   Ctrl + 0 : 还原 100% 缩放\n\n" +
                  "• 流程与执行：\n" +
                  "   Ctrl + T : 新建工作流\n" +
                  "   F5 : 运行当前流程\n" +
                  "   F6 : 单步执行选中节点\n" +
                  "   F7 : 连续循环运行\n" +
                  "   Shift + F5 : 停止流程\n\n" +
                  "• AI 智能助手：\n" +
                  "   Ctrl + Space : 展开 / 收起 AI 抽屉";
        System.Windows.MessageBox.Show(msg, "快捷键指南 - Vision Studio", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
    }

    /// <summary>打开关于对话框。</summary>
    private void OnOpenAboutDialog(object sender, System.Windows.RoutedEventArgs e)
    {
        const string description = """
            VisionFlow
            基于 HALCON 的可视化机器视觉工作台

            从本地图像开始，拖拽算子并连线，搭建缺陷检测、尺寸测量和模板匹配流程。
            支持单张与批量图像处理，可逐步执行，查看中间图像、检测结果和执行日志。

            在算子库右键选择「预览算子」，可查看 HALCON 调用签名和中文参数说明。
            可按需配置 AI 助手、MCP 接入及 PLC、Modbus 等设备通讯。

            项目基于 AAAandSphinx 的开源代码，采用 MIT 许可证。
            应用附带 HALCON 运行库，执行视觉流程需要有效的 HALCON 许可。
            """;

        System.Windows.MessageBox.Show(
            System.Windows.Window.GetWindow(this),
            description,
            "关于 VisionFlow",
            System.Windows.MessageBoxButton.OK,
            System.Windows.MessageBoxImage.Information);
    }

}
