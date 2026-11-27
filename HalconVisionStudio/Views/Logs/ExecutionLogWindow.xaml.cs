using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using HalconVisionStudio.Shared.Diagnostics;
using Microsoft.Win32;

namespace HalconVisionStudio.Views.Logs
{
    /// <summary>
    /// 查看日志服务的内存快照；定时刷新避免高频日志逐条调度阻塞界面。
    /// </summary>
    public partial class ExecutionLogWindow
    {
        private readonly DispatcherTimer refreshTimer;
        private ExecutionLogEntry[] lastSnapshot = Array.Empty<ExecutionLogEntry>();
        private ExecutionLogEntry[] visibleEntries = Array.Empty<ExecutionLogEntry>();

        public ExecutionLogWindow()
        {
            InitializeComponent();

            CategoryBox.Items.Add("全部类别");
            foreach (var category in Enum.GetValues<ExecLogCategory>())
            {
                CategoryBox.Items.Add(new ExecutionLogEntry { Category = category }.CategoryText);
            }

            LevelBox.Items.Add("全部级别");
            foreach (var level in Enum.GetValues<ExecLogLevel>())
            {
                LevelBox.Items.Add(new ExecutionLogEntry { Level = level }.LevelText);
            }

            CategoryBox.SelectedIndex = 0;
            LevelBox.SelectedIndex = 0;

            refreshTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(500)
            };
            refreshTimer.Tick += OnRefreshTick;
            refreshTimer.Start();
            RefreshEntries(true);
        }

        protected override void OnClosed(EventArgs e)
        {
            refreshTimer.Stop();
            refreshTimer.Tick -= OnRefreshTick;
            base.OnClosed(e);
        }

        private void OnRefreshTick(object sender, EventArgs e)
        {
            RefreshEntries(false);
        }

        private void OnFilterChanged(object sender, RoutedEventArgs e)
        {
            // InitializeComponent 期间控件尚未全部创建。
            if (LogGrid == null || SearchBox == null || StatusText == null)
            {
                return;
            }

            RefreshEntries(true);
        }

        private void RefreshEntries(bool force)
        {
            var snapshot = ExecutionLogService.Snapshot();
            if (!force && snapshot.SequenceEqual(lastSnapshot))
            {
                return;
            }

            lastSnapshot = snapshot;
            var selected = LogGrid.SelectedItem as ExecutionLogEntry;
            var keyword = SearchBox.Text.Trim();
            var query = snapshot.AsEnumerable();

            if (CategoryBox.SelectedIndex > 0)
            {
                var category = Enum.GetValues<ExecLogCategory>()[CategoryBox.SelectedIndex - 1];
                query = query.Where(entry => entry.Category == category);
            }

            if (LevelBox.SelectedIndex > 0)
            {
                var level = Enum.GetValues<ExecLogLevel>()[LevelBox.SelectedIndex - 1];
                query = query.Where(entry => entry.Level == level);
            }

            if (keyword.Length > 0)
            {
                query = query.Where(entry =>
                    (entry.Scope ?? string.Empty).Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
                    (entry.Message ?? string.Empty).Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
                    (entry.Detail ?? string.Empty).Contains(keyword, StringComparison.OrdinalIgnoreCase));
            }

            visibleEntries = query.Reverse().ToArray();
            LogGrid.ItemsSource = visibleEntries;
            if (selected != null && visibleEntries.Contains(selected))
            {
                LogGrid.SelectedItem = selected;
            }

            StatusText.Text = $"显示 {visibleEntries.Length} / {snapshot.Length} 条最近日志";
        }

        private void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (DetailBox == null)
            {
                return;
            }

            var entry = LogGrid.SelectedItem as ExecutionLogEntry;
            DetailBox.Text = entry == null
                ? string.Empty
                : $"{entry.Timestamp:yyyy-MM-dd} {entry.OneLine}{Environment.NewLine}{entry.Detail}";
        }

        private void OnExport(object sender, RoutedEventArgs e)
        {
            var dialog = new SaveFileDialog
            {
                Filter = "日志文本 (*.log)|*.log|文本文件 (*.txt)|*.txt",
                FileName = $"execution_{DateTime.Now:yyyyMMdd_HHmmss}.log"
            };
            if (dialog.ShowDialog(this) != true)
            {
                return;
            }

            try
            {
                var lines = visibleEntries.Reverse().Select(entry =>
                    $"{entry.Timestamp:yyyy-MM-dd} {entry.OneLine}{Environment.NewLine}{entry.Detail}");
                File.WriteAllLines(dialog.FileName, lines, new UTF8Encoding(false));
                StatusText.Text = $"已导出 {visibleEntries.Length} 条日志";
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "日志导出失败", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void OnOpenDirectory(object sender, RoutedEventArgs e)
        {
            try
            {
                ExecutionLogService.Flush();
                Directory.CreateDirectory(ExecutionLogService.LogDirectory);
                Process.Start(new ProcessStartInfo(ExecutionLogService.LogDirectory)
                {
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "打开日志目录失败", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
