using System;
using System.Collections;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using HalconVisionStudio.Shared;

namespace HalconVisionStudio.UI.Controls
{
    /// <summary>
    /// Workflow canvas control.
    /// </summary>
    public partial class WorkflowEditor : UserControl
    {
        public const double MinimumScale = 0.05;
        private const double MaxWorkflowScale = 2.5;
        private const double ZoomFactor = 1.1;

        private Point dragStartPoint;
        private Point nodeStartPoint;
        private Point panStartPoint;
        private Point panStartTranslation;
        private MouseButton panButton;
        private bool panMoved;
        private bool clearSelectionOnClick;

        private IHandleRunModel draggedNode;
        private IHandleRunModel pendingConnectionSource;
        private bool isPanning;
        private bool isSelecting;
        private Point selectionStartPoint;

        public WorkflowEditor()
        {
            InitializeComponent();
        }

        private static void OnNodesSourceChanged(DependencyObject sender, DependencyPropertyChangedEventArgs e)
        {
            var editor = (WorkflowEditor)sender;
            if (editor.ViewportTranslation != null)
            {
                editor.ViewportTranslation.X = 0;
                editor.ViewportTranslation.Y = 0;
            }
        }

        public static readonly DependencyProperty LayoutRevisionProperty = DependencyProperty.Register(
            nameof(LayoutRevision),
            typeof(int),
            typeof(WorkflowEditor),
            new PropertyMetadata(0, OnLayoutRevisionChanged));

        public int LayoutRevision
        {
            get => (int)GetValue(LayoutRevisionProperty);
            set => SetValue(LayoutRevisionProperty, value);
        }

        private static void OnLayoutRevisionChanged(DependencyObject sender, DependencyPropertyChangedEventArgs e)
        {
            ((WorkflowEditor)sender).CenterDiagram();
        }

        private void CenterView_Click(object sender, RoutedEventArgs e)
        {
            CenterDiagram();
        }

        private void CenterDiagram()
        {
            if (WorkflowViewport == null || ViewportTranslation == null ||
                WorkflowViewport.ActualWidth <= 0 || WorkflowViewport.ActualHeight <= 0 || Nodes == null)
            {
                return;
            }

            var nodes = Nodes.OfType<IWorkflowNodePosition>()
                .Where(node => double.IsFinite(node.X) && double.IsFinite(node.Y))
                .ToArray();
            if (nodes.Length == 0)
            {
                ViewportTranslation.X = 0;
                ViewportTranslation.Y = 0;
                return;
            }

            var left = nodes.Min(node => node.X);
            var top = nodes.Min(node => node.Y);
            var width = nodes.Max(node => node.X) - left + WorkflowConnection.NodeWidth;
            var height = nodes.Max(node => node.Y) - top + WorkflowConnection.NodeHeight;
            const double viewportPadding = 64;
            var fit = Math.Min(
                Math.Max(1, WorkflowViewport.ActualWidth - viewportPadding) / width,
                Math.Max(1, WorkflowViewport.ActualHeight - viewportPadding) / height);
            var scale = Math.Max(MinimumScale, Math.Min(WorkflowScale, fit));
            SetCurrentValue(WorkflowScaleProperty, scale);
            GetBindingExpression(WorkflowScaleProperty)?.UpdateSource();
            ViewportTranslation.X = WorkflowViewport.ActualWidth / 2 - (left + width / 2) * scale;
            ViewportTranslation.Y = WorkflowViewport.ActualHeight / 2 - (top + height / 2) * scale;
        }

        private static void OnWorkflowScaleChanged(DependencyObject sender, DependencyPropertyChangedEventArgs e)
        {
            var editor = (WorkflowEditor)sender;
            if (editor.WorkflowViewport == null || editor.ViewportTranslation == null)
            {
                return;
            }

            // 工具栏缩放围绕视口中心；滚轮会另行将锚点切换到鼠标位置。
            var ratio = (double)e.NewValue / (double)e.OldValue;
            var centerX = editor.WorkflowViewport.ActualWidth / 2;
            var centerY = editor.WorkflowViewport.ActualHeight / 2;
            editor.ViewportTranslation.X = centerX + (editor.ViewportTranslation.X - centerX) * ratio;
            editor.ViewportTranslation.Y = centerY + (editor.ViewportTranslation.Y - centerY) * ratio;
        }

        public static readonly DependencyProperty NodesProperty =
            DependencyProperty.Register(
                nameof(Nodes),
                typeof(IEnumerable),
                typeof(WorkflowEditor),
                new FrameworkPropertyMetadata(OnNodesSourceChanged));

        public static readonly DependencyProperty ConnectionsProperty =
            DependencyProperty.Register(nameof(Connections), typeof(IEnumerable), typeof(WorkflowEditor));

        public static readonly DependencyProperty WorkflowScaleProperty =
            DependencyProperty.Register(
                nameof(WorkflowScale),
                typeof(double),
                typeof(WorkflowEditor),
                new FrameworkPropertyMetadata(1.0, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnWorkflowScaleChanged));

        public static readonly DependencyProperty AddNodeCommandProperty =
            DependencyProperty.Register(nameof(AddNodeCommand), typeof(ICommand), typeof(WorkflowEditor));

        public static readonly DependencyProperty MoveNodeCommandProperty =
            DependencyProperty.Register(nameof(MoveNodeCommand), typeof(ICommand), typeof(WorkflowEditor));

        public static readonly DependencyProperty ConnectNodesCommandProperty =
            DependencyProperty.Register(nameof(ConnectNodesCommand), typeof(ICommand), typeof(WorkflowEditor));

        public static readonly DependencyProperty SelectConnectionCommandProperty =
            DependencyProperty.Register(nameof(SelectConnectionCommand), typeof(ICommand), typeof(WorkflowEditor));

        public static readonly DependencyProperty DeleteSelectedConnectionCommandProperty =
            DependencyProperty.Register(nameof(DeleteSelectedConnectionCommand), typeof(ICommand), typeof(WorkflowEditor));

        public static readonly DependencyProperty DeleteConnectionCommandProperty =
            DependencyProperty.Register(nameof(DeleteConnectionCommand), typeof(ICommand), typeof(WorkflowEditor));

        public static readonly DependencyProperty SelectNodeCommandProperty =
            DependencyProperty.Register(nameof(SelectNodeCommand), typeof(ICommand), typeof(WorkflowEditor));

        public static readonly DependencyProperty NodeEditCommandProperty =
            DependencyProperty.Register(nameof(NodeEditCommand), typeof(ICommand), typeof(WorkflowEditor));

        public static readonly DependencyProperty NodeExecuteCommandProperty =
            DependencyProperty.Register(nameof(NodeExecuteCommand), typeof(ICommand), typeof(WorkflowEditor));

        public static readonly DependencyProperty NodeDeleteCommandProperty =
            DependencyProperty.Register(nameof(NodeDeleteCommand), typeof(ICommand), typeof(WorkflowEditor));

        public static readonly DependencyProperty NodeConditionCommandProperty =
            DependencyProperty.Register(nameof(NodeConditionCommand), typeof(ICommand), typeof(WorkflowEditor));

        public static readonly DependencyProperty SetInputBindingCommandProperty =
            DependencyProperty.Register(nameof(SetInputBindingCommand), typeof(ICommand), typeof(WorkflowEditor));

        public static readonly DependencyProperty UndoCommandProperty =
            DependencyProperty.Register(nameof(UndoCommand), typeof(ICommand), typeof(WorkflowEditor));

        public static readonly DependencyProperty RedoCommandProperty =
            DependencyProperty.Register(nameof(RedoCommand), typeof(ICommand), typeof(WorkflowEditor));

        public static readonly DependencyProperty StopExecutionCommandProperty =
            DependencyProperty.Register(nameof(StopExecutionCommand), typeof(ICommand), typeof(WorkflowEditor));

        public static readonly DependencyProperty AutoLayoutCommandProperty =
            DependencyProperty.Register(nameof(AutoLayoutCommand), typeof(ICommand), typeof(WorkflowEditor));

        public IEnumerable Nodes
        {
            get => (IEnumerable)GetValue(NodesProperty);
            set => SetValue(NodesProperty, value);
        }

        public IEnumerable Connections
        {
            get => (IEnumerable)GetValue(ConnectionsProperty);
            set => SetValue(ConnectionsProperty, value);
        }

        public double WorkflowScale
        {
            get => (double)GetValue(WorkflowScaleProperty);
            set => SetValue(WorkflowScaleProperty, value);
        }

        public ICommand AddNodeCommand
        {
            get => (ICommand)GetValue(AddNodeCommandProperty);
            set => SetValue(AddNodeCommandProperty, value);
        }

        public ICommand MoveNodeCommand
        {
            get => (ICommand)GetValue(MoveNodeCommandProperty);
            set => SetValue(MoveNodeCommandProperty, value);
        }

        public ICommand ConnectNodesCommand
        {
            get => (ICommand)GetValue(ConnectNodesCommandProperty);
            set => SetValue(ConnectNodesCommandProperty, value);
        }

        public ICommand SelectConnectionCommand
        {
            get => (ICommand)GetValue(SelectConnectionCommandProperty);
            set => SetValue(SelectConnectionCommandProperty, value);
        }

        public ICommand DeleteSelectedConnectionCommand
        {
            get => (ICommand)GetValue(DeleteSelectedConnectionCommandProperty);
            set => SetValue(DeleteSelectedConnectionCommandProperty, value);
        }

        public ICommand DeleteConnectionCommand
        {
            get => (ICommand)GetValue(DeleteConnectionCommandProperty);
            set => SetValue(DeleteConnectionCommandProperty, value);
        }

        public ICommand SelectNodeCommand
        {
            get => (ICommand)GetValue(SelectNodeCommandProperty);
            set => SetValue(SelectNodeCommandProperty, value);
        }

        public ICommand NodeEditCommand
        {
            get => (ICommand)GetValue(NodeEditCommandProperty);
            set => SetValue(NodeEditCommandProperty, value);
        }

        public ICommand NodeExecuteCommand
        {
            get => (ICommand)GetValue(NodeExecuteCommandProperty);
            set => SetValue(NodeExecuteCommandProperty, value);
        }

        public ICommand NodeDeleteCommand
        {
            get => (ICommand)GetValue(NodeDeleteCommandProperty);
            set => SetValue(NodeDeleteCommandProperty, value);
        }

        public ICommand NodeConditionCommand
        {
            get => (ICommand)GetValue(NodeConditionCommandProperty);
            set => SetValue(NodeConditionCommandProperty, value);
        }

        public ICommand UndoCommand
        {
            get => (ICommand)GetValue(UndoCommandProperty);
            set => SetValue(UndoCommandProperty, value);
        }

        public ICommand RedoCommand
        {
            get => (ICommand)GetValue(RedoCommandProperty);
            set => SetValue(RedoCommandProperty, value);
        }

        public ICommand StopExecutionCommand
        {
            get => (ICommand)GetValue(StopExecutionCommandProperty);
            set => SetValue(StopExecutionCommandProperty, value);
        }

        public ICommand AutoLayoutCommand
        {
            get => (ICommand)GetValue(AutoLayoutCommandProperty);
            set => SetValue(AutoLayoutCommandProperty, value);
        }

        public ICommand SetInputBindingCommand
        {
            get => (ICommand)GetValue(SetInputBindingCommandProperty);
            set => SetValue(SetInputBindingCommandProperty, value);
        }

        /// <summary>
        /// 工具箱算子拖到画布上方时，根据拖拽数据格式判断是否允许投放。
        /// </summary>
        private void WorkflowCanvas_DragOver(object sender, DragEventArgs e)
        {
            e.Effects = GetDraggedHandle(e.Data) != null ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
        }

        /// <summary>
        /// 工具箱算子投放到画布时，转换为新增节点请求。
        /// 节点坐标以鼠标位置为中心做偏移，使节点出现在用户投放的位置附近。
        /// </summary>
        private void WorkflowCanvas_Drop(object sender, DragEventArgs e)
        {
            var model = GetDraggedHandle(e.Data);
            if (model == null)
            {
                return;
            }

            var point = e.GetPosition(WorkflowCanvas);
            var x = point.X - WorkflowConnection.NodeWidth / 2;
            var y = point.Y - WorkflowConnection.NodeHeight / 2;
            Execute(AddNodeCommand, new WorkflowNodeCreateRequest(model, x, y));
            e.Handled = true;
        }

        /// <summary>
        /// 开始拖动节点，同时选中该节点并记录拖动起点。
        /// 支持 Ctrl 键增量选中，非 Ctrl 单击时保留多选状态。
        /// </summary>
        private void Node_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            Focus();
            if (IsLeftAltPressed())
            {
                Execute(SelectConnectionCommand, null);
                StartSelection(e);
                e.Handled = true;
                return;
            }

            if ((sender as FrameworkElement)?.DataContext is IHandleRunModel node)
            {
                draggedNode = node;
                bool isCtrl = (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control;
                if (isCtrl)
                {
                    node.IsSelected = !node.IsSelected;
                    Execute(SelectConnectionCommand, null);
                    if (node.IsSelected)
                    {
                        Execute(SelectNodeCommand, node);
                    }
                }
                else
                {
                    if (!node.IsSelected)
                    {
                        ClearNodeSelection();
                        node.IsSelected = true;
                    }
                    Execute(SelectConnectionCommand, null);
                    Execute(SelectNodeCommand, node);
                }

                dragStartPoint = e.GetPosition(WorkflowCanvas);
                nodeStartPoint = new Point(node.X, node.Y);
                Mouse.Capture(sender as IInputElement);
                e.Handled = true;
            }
        }

        /// <summary>
        /// 节点拖动中持续发送移动请求。
        /// 实际坐标写入和连线刷新由 ViewModel 处理。
        /// </summary>
        private void Node_MouseMove(object sender, MouseEventArgs e)
        {
            if (draggedNode == null || e.LeftButton != MouseButtonState.Pressed)
            {
                return;
            }

            var current = e.GetPosition(WorkflowCanvas);
            var x = nodeStartPoint.X + current.X - dragStartPoint.X;
            var y = nodeStartPoint.Y + current.Y - dragStartPoint.Y;
            Execute(MoveNodeCommand, new WorkflowNodeMoveRequest(draggedNode, x, y));
        }

        /// <summary>
        /// 结束节点拖动并释放鼠标捕获。
        /// </summary>
        private void Node_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (draggedNode != null)
            {
                Mouse.Capture(null);
                draggedNode = null;
                e.Handled = true;
            }
        }

        /// <summary>
        /// 从节点底部输出端开始创建临时连线预览。
        /// </summary>
        private void OutputPort_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            Focus();
            pendingConnectionSource = (sender as FrameworkElement)?.DataContext as IHandleRunModel;
            if (pendingConnectionSource != null)
            {
                Mouse.Capture(WorkflowViewport, CaptureMode.SubTree);
                UpdateConnectionPreview(e.GetPosition(WorkflowCanvas));
            }

            e.Handled = true;
        }

        private void InputPort_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            // 输入端只用于接收连线，按下时阻止事件冒泡到节点拖动逻辑。
        }

        /// <summary>
        /// 鼠标在输入端释放时，如果存在临时连线来源，则创建正式连线。
        /// </summary>
        private void InputPort_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (pendingConnectionSource != null && (sender as FrameworkElement)?.DataContext is IHandleRunModel target)
            {
                Execute(ConnectNodesCommand, new WorkflowConnectionRequest(pendingConnectionSource, target));
            }

            CancelConnectionPreview();
            e.Handled = true;
        }

        /// <summary>
        /// 单击连线时选中连线，并清除所有节点的选中状态，后续可通过 Delete 或右键菜单删除。
        /// </summary>
        private void Connection_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            Focus();
            if ((sender as FrameworkElement)?.DataContext is WorkflowConnection connection)
            {
                ClearNodeSelection();
                Execute(SelectNodeCommand, null);
                Execute(SelectConnectionCommand, connection);
                e.Handled = true;
            }
        }

        /// <summary>
        /// 打开节点右键菜单时动态生成“输入来源”子菜单。
        /// 候选项基于当前所有节点输出实时构建，节点执行后新增的运行时输出也能出现在菜单中。
        /// </summary>
        private void NodeContextMenu_Opened(object sender, RoutedEventArgs e)
        {
            if (sender is not ContextMenu menu || menu.PlacementTarget is not FrameworkElement target || target.DataContext is not IHandleRunModel node)
            {
                return;
            }

            Execute(SelectNodeCommand, node);
            var bindingMenu = menu.Items
                .OfType<MenuItem>()
                .FirstOrDefault(x => Equals(x.Name, "InputBindingsMenuItem"));
            if (bindingMenu == null)
            {
                return;
            }

            bindingMenu.Items.Clear();
            if (node.InputBindings == null || node.InputBindings.Count == 0)
            {
                bindingMenu.IsEnabled = false;
                bindingMenu.Items.Add(new MenuItem { Header = "当前节点没有可绑定输入", IsEnabled = false });
                return;
            }

            bindingMenu.IsEnabled = true;
            var nodes = Nodes?.Cast<IHandleRunModel>() ?? Enumerable.Empty<IHandleRunModel>();
            foreach (var binding in node.InputBindings)
            {
                var slotMenu = new MenuItem { Header = binding.TargetInputName };
                var options = WorkflowBindingHelper.BuildOutputOptions(nodes, node, binding).ToList();
                foreach (var option in options)
                {
                    // Bind the selected output directly to the input binding.
                    var request = new WorkflowInputBindingRequest(binding, option);
                    var optionItem = new MenuItem
                    {
                        Header = option.DisplayName,
                        Command = SetInputBindingCommand,
                        CommandParameter = request,
                        IsCheckable = true,
                        IsChecked = ReferenceEquals(option.SourceNode, binding.SourceNode) && option.OutputKey == binding.SourceOutputKey
                    };
                    slotMenu.Items.Add(optionItem);
                }

                bindingMenu.Items.Add(slotMenu);
            }
        }

        /// <summary>
        /// 画布空白处按下鼠标左键：
        /// 左键拖动空白处平移。Shift 或 Alt 加左键拖动可框选节点。
        /// </summary>
        private void WorkflowCanvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            Focus();
            if (pendingConnectionSource != null)
            {
                CancelConnectionPreview();
                e.Handled = true;
                return;
            }

            if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0 || IsLeftAltPressed())
            {
                Execute(SelectConnectionCommand, null);
                StartSelection(e);
            }
            else
            {
                StartCanvasPan(e, clearSelectionOnClick: true);
            }

            e.Handled = true;
        }

        private void WorkflowViewport_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Middle ||
                (e.ChangedButton == MouseButton.Left && Keyboard.IsKeyDown(Key.Space)))
            {
                CancelConnectionPreview();
                if (isSelecting)
                {
                    CancelSelection();
                }

                Focus();
                StartCanvasPan(e, clearSelectionOnClick: false);
                e.Handled = true;
            }
        }

        private void WorkflowViewport_MouseUp(object sender, MouseButtonEventArgs e)
        {
            if (isPanning && e.ChangedButton == panButton)
            {
                StopCanvasPan(finished: true);
                e.Handled = true;
            }
        }

        private void WorkflowViewport_LostMouseCapture(object sender, MouseEventArgs e)
        {
            if (isPanning && Mouse.Captured != WorkflowViewport)
            {
                StopCanvasPan();
            }
        }

        /// <summary>
        /// 根据当前交互状态更新框选范围、画布平移或临时连线路径。
        /// </summary>
        private void WorkflowCanvas_MouseMove(object sender, MouseEventArgs e)
        {
            if (isSelecting)
            {
                UpdateSelection(e);
                e.Handled = true;
                return;
            }

            if (isPanning)
            {
                UpdateCanvasPan(e);
                e.Handled = true;
                return;
            }

            if (pendingConnectionSource == null)
            {
                return;
            }

            if (e.LeftButton != MouseButtonState.Pressed)
            {
                CancelConnectionPreview();
                return;
            }

            UpdateConnectionPreview(e.GetPosition(WorkflowCanvas));
            e.Handled = true;
        }

        /// <summary>
        /// 画布释放鼠标时完成框选、结束平移或取消未落在输入端的临时连线。
        /// </summary>
        private void WorkflowCanvas_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (isSelecting)
            {
                CompleteSelection(e);
                e.Handled = true;
                return;
            }

            if (isPanning && panButton == MouseButton.Left)
            {
                StopCanvasPan(finished: true);
                e.Handled = true;
                return;
            }

            if (pendingConnectionSource == null)
            {
                return;
            }

            CancelConnectionPreview();
            e.Handled = true;
        }

        /// <summary>
        /// 鼠标滚轮缩放画布，并保持鼠标指向的画布位置尽量不漂移。
        /// </summary>
        private void WorkflowViewport_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            var oldScale = Math.Max(MinimumScale, WorkflowScale);
            var newScale = e.Delta > 0
                ? Math.Min(MaxWorkflowScale, oldScale * ZoomFactor)
                : Math.Max(MinimumScale, oldScale / ZoomFactor);

            if (Math.Abs(newScale - oldScale) < 0.001)
            {
                e.Handled = true;
                return;
            }

            var viewportPoint = e.GetPosition(WorkflowViewport);
            var canvasPoint = e.GetPosition(WorkflowCanvas);
            SetCurrentValue(WorkflowScaleProperty, newScale);
            GetBindingExpression(WorkflowScaleProperty)?.UpdateSource();

            ViewportTranslation.X = viewportPoint.X - canvasPoint.X * newScale;
            ViewportTranslation.Y = viewportPoint.Y - canvasPoint.Y * newScale;
            e.Handled = true;
        }

        private void WorkflowEditor_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                if (isPanning)
                {
                    StopCanvasPan();
                    e.Handled = true;
                    return;
                }

                if (pendingConnectionSource != null)
                {
                    CancelConnectionPreview();
                }

                if (isSelecting)
                {
                    CancelSelection();
                }

                // 没有连线/框选可取消时，Esc 用于停止正在执行的流程。
                if (pendingConnectionSource == null && !isSelecting)
                {
                    Execute(StopExecutionCommand, null);
                }

                e.Handled = true;
                return;
            }

            // Ctrl+Z 撤销 / Ctrl+Y 或 Ctrl+Shift+Z 重做。
            if (e.Key == Key.Z && Keyboard.Modifiers == ModifierKeys.Control)
            {
                Execute(UndoCommand, null);
                e.Handled = true;
                return;
            }

            if ((e.Key == Key.Y && Keyboard.Modifiers == ModifierKeys.Control) ||
                (e.Key == Key.Z && Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift)))
            {
                Execute(RedoCommand, null);
                e.Handled = true;
                return;
            }

            if (e.Key == Key.Delete)
            {
                DeleteSelectedItems();
                e.Handled = true;
                return;
            }

            if (e.Key == Key.Enter)
            {
                var node = GetSelectedNodes().FirstOrDefault();
                if (node != null)
                {
                    Execute(NodeExecuteCommand, node);
                }

                e.Handled = true;
            }
        }

        private void StartSelection(MouseButtonEventArgs e)
        {
            isSelecting = true;
            selectionStartPoint = e.GetPosition(WorkflowCanvas);
            Canvas.SetLeft(SelectionRectangle, selectionStartPoint.X);
            Canvas.SetTop(SelectionRectangle, selectionStartPoint.Y);
            SelectionRectangle.Width = 0;
            SelectionRectangle.Height = 0;
            SelectionRectangle.Visibility = Visibility.Visible;
            WorkflowViewport.Cursor = Cursors.Cross;
            Mouse.Capture(WorkflowViewport);
        }

        private void UpdateSelection(MouseEventArgs e)
        {
            if (e.LeftButton != MouseButtonState.Pressed)
            {
                CompleteSelection(e);
                return;
            }

            var bounds = GetSelectionBounds(e.GetPosition(WorkflowCanvas));
            SetSelectionRectangle(bounds);

            // 框选过程中动态实时高亮所有与选框相交的节点
            if (Nodes != null)
            {
                foreach (var node in Nodes.Cast<IHandleRunModel>())
                {
                    var nodeRect = new Rect(node.X, node.Y, WorkflowConnection.NodeWidth, WorkflowConnection.NodeHeight);
                    node.IsSelected = bounds.IntersectsWith(nodeRect);
                }
            }
        }

        private void CompleteSelection(MouseEventArgs e)
        {
            var bounds = GetSelectionBounds(e.GetPosition(WorkflowCanvas));
            SelectNodesInBounds(bounds);
            CancelSelection();
        }

        private void CancelSelection()
        {
            isSelecting = false;
            SelectionRectangle.Visibility = Visibility.Collapsed;
            WorkflowViewport.Cursor = null;

            if (Mouse.Captured == WorkflowViewport)
            {
                Mouse.Capture(null);
            }
        }

        private Rect GetSelectionBounds(Point current)
        {
            var x = Math.Min(selectionStartPoint.X, current.X);
            var y = Math.Min(selectionStartPoint.Y, current.Y);
            var width = Math.Abs(current.X - selectionStartPoint.X);
            var height = Math.Abs(current.Y - selectionStartPoint.Y);
            return new Rect(x, y, width, height);
        }

        private void SetSelectionRectangle(Rect bounds)
        {
            Canvas.SetLeft(SelectionRectangle, bounds.X);
            Canvas.SetTop(SelectionRectangle, bounds.Y);
            SelectionRectangle.Width = bounds.Width;
            SelectionRectangle.Height = bounds.Height;
        }

        private void SelectNodesInBounds(Rect bounds)
        {
            var allNodes = (Nodes?.Cast<IHandleRunModel>() ?? Enumerable.Empty<IHandleRunModel>()).ToList();
            var selected = allNodes
                .Where(node => bounds.IntersectsWith(new Rect(node.X, node.Y, WorkflowConnection.NodeWidth, WorkflowConnection.NodeHeight)))
                .ToList();

            foreach (var node in allNodes)
            {
                node.IsSelected = selected.Contains(node);
            }

            Execute(SelectConnectionCommand, null);
            if (selected.Count > 0)
            {
                Execute(SelectNodeCommand, selected.First());
                // 再次确保已框选的所有节点保持高亮选中状态
                foreach (var node in selected)
                {
                    node.IsSelected = true;
                }
            }
            else
            {
                Execute(SelectNodeCommand, null);
            }
        }

        private void ClearNodeSelection()
        {
            if (Nodes != null)
            {
                foreach (var node in Nodes.Cast<IHandleRunModel>())
                {
                    node.IsSelected = false;
                }
            }
        }

        private void DeleteSelectedItems()
        {
            // 1. 优先检查是否有选中的连线，若有则仅删除选中的连线，绝不触碰任何节点！
            var selectedConn = Connections?.Cast<WorkflowConnection>().FirstOrDefault(c => c.IsSelected);

            if (selectedConn != null)
            {
                Execute(DeleteSelectedConnectionCommand, null);
                return;
            }

            // 2. 否则处理选中的节点批量删除
            var selectedNodes = GetSelectedNodes().ToList();
            if (selectedNodes.Count == 0)
            {
                return;
            }

            var message = selectedNodes.Count == 1
                ? $"确定删除节点“{selectedNodes[0].DisplayName}”吗？"
                : $"确定删除选中的 {selectedNodes.Count} 个节点吗？";
            var result = MessageBox.Show(
                message,
                "删除节点",
                MessageBoxButton.OKCancel,
                MessageBoxImage.Warning);
            if (result != MessageBoxResult.OK)
            {
                return;
            }

            foreach (var node in selectedNodes)
            {
                Execute(NodeDeleteCommand, node);
            }
        }

        private IEnumerable<IHandleRunModel> GetSelectedNodes()
        {
            return Nodes?.Cast<IHandleRunModel>().Where(node => node.IsSelected) ?? Enumerable.Empty<IHandleRunModel>();
        }

        private static bool IsLeftAltPressed()
        {
            return Keyboard.IsKeyDown(Key.LeftAlt);
        }

        /// <summary>
        /// 根据输出端和当前鼠标坐标刷新临时连线预览。
        /// </summary>
        private void UpdateConnectionPreview(Point current)
        {
            if (pendingConnectionSource == null)
            {
                return;
            }

            var startX = pendingConnectionSource.X + WorkflowConnection.NodeCenterX;
            var startY = pendingConnectionSource.Y + WorkflowConnection.NodeHeight;
            PreviewConnectionPath.Data = Geometry.Parse(BuildPreviewPath(startX, startY, current.X, current.Y));
            PreviewConnectionPath.Visibility = Visibility.Visible;
        }

        /// <summary>
        /// 清理临时连线状态并释放画布鼠标捕获。
        /// </summary>
        private void CancelConnectionPreview()
        {
            pendingConnectionSource = null;
            PreviewConnectionPath.Data = null;
            PreviewConnectionPath.Visibility = Visibility.Collapsed;

            if (Mouse.Captured == WorkflowViewport)
            {
                Mouse.Capture(null);
            }
        }

        /// <summary>
        /// 开始画布平移，记录当前鼠标位置和滚动偏移。
        /// </summary>
        private void StartCanvasPan(MouseButtonEventArgs e, bool clearSelectionOnClick)
        {
            isPanning = true;
            panButton = e.ChangedButton;
            panMoved = false;
            this.clearSelectionOnClick = clearSelectionOnClick;
            panStartPoint = e.GetPosition(WorkflowViewport);
            panStartTranslation = new Point(ViewportTranslation.X, ViewportTranslation.Y);
            WorkflowViewport.Cursor = Cursors.ScrollAll;
            Mouse.Capture(WorkflowViewport);
        }

        private void UpdateCanvasPan(MouseEventArgs e)
        {
            var buttonState = panButton == MouseButton.Middle ? e.MiddleButton : e.LeftButton;
            if (buttonState != MouseButtonState.Pressed)
            {
                StopCanvasPan();
                return;
            }

            var delta = e.GetPosition(WorkflowViewport) - panStartPoint;
            if (!panMoved && Math.Abs(delta.X) < SystemParameters.MinimumHorizontalDragDistance &&
                Math.Abs(delta.Y) < SystemParameters.MinimumVerticalDragDistance)
            {
                return;
            }

            panMoved = true;
            ViewportTranslation.X = panStartTranslation.X + delta.X;
            ViewportTranslation.Y = panStartTranslation.Y + delta.Y;
        }

        private void StopCanvasPan(bool finished = false)
        {
            isPanning = false;
            WorkflowViewport.Cursor = null;
            if (Mouse.Captured == WorkflowViewport)
            {
                Mouse.Capture(null);
            }

            if (finished && clearSelectionOnClick && !panMoved)
            {
                ClearNodeSelection();
                Execute(SelectNodeCommand, null);
                Execute(SelectConnectionCommand, null);
            }
        }

        /// <summary>
        /// 构建临时连线的折线路径。
        /// 与正式连线保持类似绕行规则，避免预览线直接穿过节点主体。
        /// </summary>
        private static string BuildPreviewPath(double startX, double startY, double endX, double endY)
        {
            if (endY > startY + WorkflowConnection.RouteGap)
            {
                var midY = (startY + endY) / 2;
                return BuildPath(startX, startY, startX, midY, endX, midY, endX, endY);
            }

            var laneY = Math.Max(startY, endY) + WorkflowConnection.RouteGap;
            return BuildPath(startX, startY, startX, laneY, endX, laneY, endX, endY);
        }

        /// <summary>
        /// 将坐标点序列转换为 WPF Path 使用的折线数据。
        /// </summary>
        private static string BuildPath(params double[] coordinates)
        {
            if (coordinates.Length < 4 || coordinates.Length % 2 != 0)
            {
                return string.Empty;
            }

            var path = $"M {coordinates[0]:0.###},{coordinates[1]:0.###}";
            for (var i = 2; i < coordinates.Length; i += 2)
            {
                path += $" L {coordinates[i]:0.###},{coordinates[i + 1]:0.###}";
            }

            return path;
        }

        /// <summary>
        /// 从 WPF 拖拽数据中提取算子注册模型。
        /// 同时支持自定义格式和接口类型格式，兼容不同控件发起的拖拽。
        /// </summary>
        private static IHandleRegisterModel GetDraggedHandle(IDataObject data)
        {
            if (data == null)
            {
                return null;
            }

            if (data.GetDataPresent(WorkflowDragDrop.ToolboxItemFormat) &&
                data.GetData(WorkflowDragDrop.ToolboxItemFormat) is IHandleRegisterModel formattedModel)
            {
                return formattedModel;
            }

            if (data.GetDataPresent(typeof(IHandleRegisterModel)) &&
                data.GetData(typeof(IHandleRegisterModel)) is IHandleRegisterModel interfaceModel)
            {
                return interfaceModel;
            }

            foreach (var format in data.GetFormats())
            {
                if (data.GetData(format) is IHandleRegisterModel model)
                {
                    return model;
                }
            }

            return null;
        }

        /// <summary>
        /// 安全执行绑定命令，统一处理 CanExecute 检查。
        /// </summary>
        private static void Execute(ICommand command, object parameter)
        {
            if (command?.CanExecute(parameter) == true)
            {
                command.Execute(parameter);
            }
        }
    }
}

