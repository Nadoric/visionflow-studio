using System;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using HalconVisionStudio.Bootstrap;
using HalconVisionStudio.Core;
using HalconVisionStudio.UI.Dialogs;
using HalconVisionStudio.Shared;
using HalconVisionStudio.Views;
using Prism.Ioc;
using Prism.Modularity;
using Prism.Mvvm;

namespace HalconVisionStudio
{
    /// <summary>
    /// 应用程序入口类，继承自 Prism 的 <see cref="PrismApplication"/>。
    /// </summary>
    public partial class App : PrismApplication
    {
        private Views.SplashScreen splashScreen;

        // UI 异常弹框限流状态（同类异常 10 秒窗口）
        private string lastUiExceptionKey;
        private DateTime lastUiExceptionAt = DateTime.MinValue;
        private int suppressedUiExceptions;
        private bool isShowingUiException;

        /// <summary>
        /// 更新启动屏的进度条和状态文字，并短暂暂停以让动画可见。
        /// </summary>
        private void UpdateSplash(double progress, string status)
        {
            splashScreen?.UpdateProgress(progress, status);
            splashScreen?.PauseForNextStep();
        }

        /// <inheritdoc/>
        protected override void OnStartup(StartupEventArgs e)
        {
            // 插件注册或图像控件创建前，先配置当前进程的 HALCON 运行库。
            Services.HalconRuntimeBootstrap.EnsureInitialized();

            // 初始化主题管理器
            AppThemeManager.Initialize();
            
            splashScreen = new Views.SplashScreen();
            splashScreen.Show();
            base.OnStartup(e);
        }

        /// <inheritdoc/>
        protected override void ConfigureViewModelLocator()
        {
            base.ConfigureViewModelLocator();
            ViewModelLocationProvider.Register<MainWindow, MainViewModel>();
        }

        /// <inheritdoc/>
        protected override Window CreateShell()
        {
            Application.Current.DispatcherUnhandledException += OnDispatcherUnhandledException;

            AppDomain.CurrentDomain.UnhandledException += (s, e) =>
            {
                if (e.ExceptionObject is Exception ex)
                {
                    var fullError = GetFullExceptionMessage(ex);
                    MessageBox.Show(fullError, "未处理异常", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            };

            TaskScheduler.UnobservedTaskException += (s, e) =>
            {
                if (e.Exception is Exception ex)
                {
                    var fullError = GetFullExceptionMessage(ex);
                    MessageBox.Show(fullError, "未观察到的任务异常", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            };

            try
            {
                return Container.Resolve<MainWindow>();
            }
            catch (Exception ex)
            {
                var fullError = GetFullExceptionMessage(ex);
                MessageBox.Show(fullError, "初始化主窗口失败", MessageBoxButton.OK, MessageBoxImage.Error);
                throw;
            }
        }

        /// <inheritdoc/>
        protected override void ConfigureModuleCatalog(IModuleCatalog moduleCatalog)
        {
            UpdateSplash(0.1, "Loading modules...");
            base.ConfigureModuleCatalog(moduleCatalog);
            PluginModuleRegistry.RegisterModules(moduleCatalog, UpdateSplash);
        }

        /// <inheritdoc/>
        protected override void RegisterTypes(IContainerRegistry containerRegistry)
        {
            UpdateSplash(0.95, "Registering services...");

            containerRegistry.RegisterSingleton<IHandleControl, ControlCenter>();
            containerRegistry.RegisterSingleton<MainViewModel>();
            containerRegistry.RegisterSingleton<MainWindow>();

            containerRegistry.RegisterDialogWindow<MetroDialogWindow>();
            containerRegistry.RegisterDialog<NoParameterPreviewDialogView, NoParameterPreviewDialogViewModel>(Common.NoParameterPreviewDialogName);
            containerRegistry.RegisterDialog<ParameterDialogView, ParameterDialogViewModel>(Common.ParameterDialogName);
            containerRegistry.RegisterDialog<ShapeDrawDialogView, ShapeDrawDialogViewModel>(Common.ShapeDrawDialogName);
            containerRegistry.RegisterForNavigation<MainWindow, MainViewModel>();
        }

        /// <inheritdoc/>
        protected override void InitializeShell(Window shell)
        {
            base.InitializeShell(shell);
            splashScreen?.UpdateProgress(1.0, "Starting application...");
            Task.Run(async () =>
            {
                if (splashScreen != null)
                {
                    await splashScreen.DelayUntilMinimumVisibleAsync();
                }

                Dispatcher.Invoke(() =>
                {
                    splashScreen?.Close();
                    shell.Show();
                });
            });
        }

        private void OnDispatcherUnhandledException(
            object sender,
            System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
        {
            // MessageBox 会启动嵌套消息循环。先标记已处理并记录限流状态，
            // 防止渲染异常在弹窗未关闭时重入，连续创建大量错误对话框。
            e.Handled = true;
            var now = DateTime.UtcNow;
            var key = e.Exception.GetType().FullName + ":" + e.Exception.Message;
            if (isShowingUiException ||
                (key == lastUiExceptionKey && now - lastUiExceptionAt < TimeSpan.FromSeconds(10)))
            {
                suppressedUiExceptions++;
                return;
            }

            lastUiExceptionKey = key;
            lastUiExceptionAt = now;
            var suppressedCount = suppressedUiExceptions;
            suppressedUiExceptions = 0;
            isShowingUiException = true;

            try
            {
                Shared.Diagnostics.ExecutionLogService.App(
                    $"UI 未处理异常: {e.Exception.Message}",
                    Shared.Diagnostics.ExecLogLevel.Error,
                    e.Exception.ToString());
                Shared.Diagnostics.ExecutionLogService.Flush();

                var message = GetFullExceptionMessage(e.Exception);
                if (suppressedCount > 0)
                {
                    message += $"{Environment.NewLine}（已抑制 {suppressedCount} 个重复异常）";
                }

                MessageBox.Show(message, "应用程序异常", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                isShowingUiException = false;
            }
        }

        private static string GetFullExceptionMessage(Exception ex)
        {
            if (ex == null) return "未知异常";
            var sb = new StringBuilder();
            var current = ex;
            while (current != null)
            {
                sb.AppendLine($"[{current.GetType().Name}] {current.Message}");
                if (!string.IsNullOrWhiteSpace(current.StackTrace))
                {
                    sb.AppendLine(current.StackTrace);
                }
                current = current.InnerException;
                if (current != null)
                {
                    sb.AppendLine("--- 内部原因 (Inner Exception) ---");
                }
            }
            return sb.ToString();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            Shared.Diagnostics.ExecutionLogService.App("应用退出");
            Shared.Diagnostics.ExecutionLogService.Flush();
            // 断开全部通讯驱动（TCP/UDP/串口长连接）。
            Shared.Communication.CommunicationCenter.ShutdownAll();
            base.OnExit(e);
        }
    }
}
