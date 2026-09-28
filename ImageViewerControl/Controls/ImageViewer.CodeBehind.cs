using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using ImageViewer.Localization;
using ImageViewer.Models;
using ImageViewer.Plugins;

namespace ImageViewer.Controls
{
    public partial class ImageViewer
    {
        private const double ToolbarCompactWidthThreshold = 760;
        private const double ToolbarCompactHeightThreshold = 420;

        private void InitializeEventHandlers()
        {
            rootGrid.MouseWheel += OnMouseWheel;
            rootGrid.MouseDown += OnMouseDown;
            rootGrid.MouseMove += OnMouseMove;
            rootGrid.MouseUp += OnMouseUp;
            rootGrid.MouseRightButtonDown += OnMouseRightButtonDown;
            rootGrid.LostMouseCapture += OnLostMouseCapture;
            rootGrid.DragOver += OnDragOver;
            rootGrid.DragLeave += OnDragLeave;
            rootGrid.Drop += OnDrop;
            KeyDown += OnKeyDown;
            rootGrid.SizeChanged += OnRootGridSizeChanged;
        }

        private void UnregisterEventHandlers()
        {
            rootGrid.MouseWheel -= OnMouseWheel;
            rootGrid.MouseDown -= OnMouseDown;
            rootGrid.MouseMove -= OnMouseMove;
            rootGrid.MouseUp -= OnMouseUp;
            rootGrid.MouseRightButtonDown -= OnMouseRightButtonDown;
            rootGrid.LostMouseCapture -= OnLostMouseCapture;
            rootGrid.DragOver -= OnDragOver;
            rootGrid.DragLeave -= OnDragLeave;
            rootGrid.Drop -= OnDrop;
            rootGrid.SizeChanged -= OnRootGridSizeChanged;
            KeyDown -= OnKeyDown;
        }

        private void OnRootGridSizeChanged(object sender, SizeChangedEventArgs e)
        {
            UpdateToolbarLayout();
            _imageViewStateController.HandleRootGridSizeChanged();
        }

        private void UpdateToolbarLayout()
        {
            double width = rootGrid.ActualWidth;
            double height = rootGrid.ActualHeight;
            bool isCompactViewport = width > 0 && (width < ToolbarCompactWidthThreshold || height > 0 && height < ToolbarCompactHeightThreshold);

            toolbarPanel.Visibility = ShowToolbar && !isCompactViewport ? Visibility.Visible : Visibility.Collapsed;
            toolbarCompactButton.Visibility = ShowToolbar && isCompactViewport ? Visibility.Visible : Visibility.Collapsed;
        }

        private void OnCompactToolbarClick(object sender, RoutedEventArgs e)
        {
            mainContextMenu.PlacementTarget = toolbarCompactButton;
            mainContextMenu.IsOpen = true;
        }

        private void OnToolbarHideClick(object sender, RoutedEventArgs e)
        {
            ShowToolbar = false;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            UpdateStatusBar();
            _controlComposition.SessionController.StartAutoSave();
            _externalImageSourceBindingController.Refresh();
            _imageSourceController.HandleLoaded();
        }

        private async void OnUnloaded(object sender, RoutedEventArgs e)
        {
            _statusHintTimer?.Stop();
            _zoomBadgeTimer?.Stop();
            _viewportOverlayRefreshScheduler.StopScheduling();
            _analysisRefreshScheduler.StopScheduling();
            _controlComposition.SessionController.StopAutoSave();
            _analysisState.DisposeAnalysisWork();
            _infoPanelStatisticsScheduler.Cancel();
            await RunShutdownOperationAsync(
                "Drain autosave during unload",
                () => _controlComposition.SessionController.DrainAutoSaveAsync());
        }

        private static IEnumerable<MenuItem> EnumerateMenuItems(ItemsControl itemsControl)
        {
            foreach (var item in itemsControl.Items)
            {
                if (item is not MenuItem menuItem)
                {
                    continue;
                }

                yield return menuItem;

                foreach (var child in EnumerateMenuItems(menuItem))
                {
                    yield return child;
                }
            }
        }

        private void RefreshRoiDrawingMenuItems()
        {
            drawRoiMenuItem.Items.Clear();
            measureMenuItem.Items.Clear();
            roiOperationsMenuItem.Items.Remove(gradientDetectMenuItem);

            foreach (var tool in AvailableDrawingTools)
            {
                ImageViewerDynamicMenuItem menuDescriptor = ImageViewerDynamicMenuItem.FromRoiTool(tool);
                MenuItem menuItem = CreateDynamicMenuItem(menuDescriptor);

                menuItem.Click += OnRoiDrawingToolClick;
                if (menuDescriptor.Group == ImageViewerDynamicMenuGroup.Measurement)
                {
                    measureMenuItem.Items.Add(menuItem);
                }
                else
                {
                    drawRoiMenuItem.Items.Add(menuItem);
                }
            }

            measureMenuItem.Items.Add(gradientDetectMenuItem);

            drawRoiMenuItem.Visibility = drawRoiMenuItem.Items.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            measureMenuItem.Visibility = measureMenuItem.Items.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            ApplyMenuItemContentAlignment(drawRoiMenuItem);
            ApplyMenuItemContentAlignment(measureMenuItem);
            RefreshToolbarQuickTools();
        }

        /// <summary>
        /// 将最常用的少量 ROI 工具放到可见工具栏，完整工具集仍保留在右键菜单中。
        /// </summary>
        private void RefreshToolbarQuickTools()
        {
            quickToolsPanel.Children.Clear();

            IEnumerable<RoiToolDescriptor> quickTools = AvailableDrawingTools
                .Where(tool => !tool.IsMeasurement)
                .Take(5)
                .Concat(AvailableDrawingTools.Where(tool => tool.IsMeasurement).Take(3));

            foreach (RoiToolDescriptor tool in quickTools)
            {
                var button = new ToggleButton
                {
                    ToolTip = tool.Header,
                    Tag = new ImageViewerRoiToolMenuTag(tool.Header, tool.Activate),
                    Style = (Style)FindResource("ViewerToolbarToggleButtonStyle"),
                    IsChecked = IsToolInteractionActive && string.Equals(tool.Header, ActiveToolName, StringComparison.Ordinal)
                };

                if (tool.CreateIcon is Func<FrameworkElement> createIcon)
                {
                    var content = new StackPanel { Orientation = Orientation.Horizontal };
                    FrameworkElement icon = createIcon();
                    icon.Margin = new Thickness(0, 0, 5, 0);
                    content.Children.Add(icon);
                    content.Children.Add(new TextBlock
                    {
                        Text = tool.Header,
                        VerticalAlignment = VerticalAlignment.Center
                    });
                    button.Content = content;
                }
                else
                {
                    button.Content = tool.Header;
                }

                button.Click += OnToolbarRoiToolClick;
                quickToolsPanel.Children.Add(button);
            }
        }

        private static MenuItem CreateDynamicMenuItem(ImageViewerDynamicMenuItem item)
        {
            var menuItem = new MenuItem
            {
                Header = item.Header,
                ToolTip = item.ToolTip,
                IsEnabled = item.IsEnabled,
                Tag = item.Tag
            };

            if (item.CreateIcon != null)
            {
                menuItem.Icon = item.CreateIcon();
            }

            return menuItem;
        }

        private static void ApplyMenuItemContentAlignment(ItemsControl itemsControl)
        {
            foreach (var menuItem in EnumerateMenuItems(itemsControl))
            {
                menuItem.HorizontalContentAlignment = HorizontalAlignment.Left;
                menuItem.VerticalContentAlignment = VerticalAlignment.Center;
            }
        }

        private void OnRoiDrawingToolClick(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem { Tag: ImageViewerRoiToolMenuTag tool } menuItem)
            {
                tool.Activate(this);
                SetActiveToolName(tool.ToolName);
                ShowStatusHint(UiText.Format("StatusToolActivated", tool.ToolName));
            }
        }

        private void OnToolbarRoiToolClick(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement { Tag: ImageViewerRoiToolMenuTag tool })
            {
                tool.Activate(this);
                SetActiveToolName(tool.ToolName);
                ShowStatusHint(UiText.Format("StatusToolActivated", tool.ToolName));
            }
        }

        private static bool TryGetTaggedCommand<TCommand>(object sender, out TCommand command)
            where TCommand : struct, Enum
        {
            if (sender is FrameworkElement { Tag: IImageViewerMenuCommandTag<TCommand> tag })
            {
                command = tag.Command;
                return true;
            }

            command = default;
            return false;
        }

        private async void OnKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape && IsToolInteractionActive)
            {
                e.Handled = true;
                ExitCurrentMode();
                ShowStatusHint(UiText.Get("StatusDrawCancelled"));
                return;
            }

            if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.O)
            {
                e.Handled = true;
                await RunUiOperationAsync(
                    "打开图像快捷键",
                    () => _fileMenuCommandController.ExecuteAsync(ImageViewerFileMenuCommand.OpenImage));
                return;
            }

            if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.S)
            {
                e.Handled = true;
                await RunUiOperationAsync(
                    "保存会话快捷键",
                    () => _fileMenuCommandController.ExecuteAsync(ImageViewerFileMenuCommand.SaveSession));
                return;
            }

            try
            {
                _interactionController.HandleKeyDown(e);
            }
            catch (Exception ex)
            {
                ReportUiOperationFailure("键盘快捷键处理", ex);
            }
        }

        private void OnActiveToolCancelClick(object sender, RoutedEventArgs e)
        {
            if (!IsToolInteractionActive)
            {
                return;
            }

            ExitCurrentMode();
            ShowStatusHint(UiText.Get("StatusDrawCancelled"));
        }

        private void OnQuickCircularCaliperSettingsClick(object sender, RoutedEventArgs e)
        {
            if (ViewerState.SelectedRoi is not CircularCaliperMeasureRoi circularCaliper)
            {
                return;
            }

            _dialogWorkflowService.ShowCaliperSettings(circularCaliper);
            _roiSelectionStateController.RefreshPropertyPanel();
        }

        private void OnMouseRightButtonDown(object sender, MouseButtonEventArgs e) => _interactionController.HandleMouseRightButtonDown(e);

        /// <summary>
        /// 菜单搜索区的模板部件缓存。搜索框位于 <see cref="ContextMenu"/> 的 ControlTemplate 顶部，
        /// 因此需通过模板查找获取（不再是 XAML 生成的字段），以保证它横贯菜单宽度而不被项容器左对齐压扁。
        /// </summary>
        private TextBox? _menuSearchBox;
        private TextBlock? _menuSearchMatchCountText;
        private TextBlock? _menuSearchNoResultsText;

        private void EnsureMenuSearchTemplateParts()
        {
            if (_menuSearchBox is not null)
            {
                return;
            }

            mainContextMenu.ApplyTemplate();

            _menuSearchBox = mainContextMenu.Template?.FindName("PART_MenuSearchBox", mainContextMenu) as TextBox
                ?? throw new InvalidOperationException("菜单搜索区模板部件 'PART_MenuSearchBox' 缺失。");
            _menuSearchMatchCountText = mainContextMenu.Template?.FindName("PART_MenuSearchMatchCount", mainContextMenu) as TextBlock
                ?? throw new InvalidOperationException("菜单搜索区模板部件 'PART_MenuSearchMatchCount' 缺失。");
            _menuSearchNoResultsText = mainContextMenu.Template?.FindName("PART_MenuSearchNoResults", mainContextMenu) as TextBlock
                ?? throw new InvalidOperationException("菜单搜索区模板部件 'PART_MenuSearchNoResults' 缺失。");
        }

        internal TextBox menuSearchBox
        {
            get
            {
                EnsureMenuSearchTemplateParts();
                return _menuSearchBox!;
            }
        }

        internal TextBlock menuSearchMatchCountText
        {
            get
            {
                EnsureMenuSearchTemplateParts();
                return _menuSearchMatchCountText!;
            }
        }

        internal TextBlock menuSearchNoResultsText
        {
            get
            {
                EnsureMenuSearchTemplateParts();
                return _menuSearchNoResultsText!;
            }
        }

        private void OnContextMenuOpened(object sender, RoutedEventArgs e)
        {
            menuSearchBox.Text = string.Empty;
            _contextMenuController.HandleOpened();
            menuSearchBox.Dispatcher.BeginInvoke(
                () => menuSearchBox.Focus(),
                DispatcherPriority.Input);
        }

        private void OnMenuSearchTextChanged(object sender, TextChangedEventArgs e)
        {
            string rawQuery = menuSearchBox.Text.Trim();
            string[] words = rawQuery.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
            bool hasMatch = false;
            int matchCount = 0;
            foreach (object item in mainContextMenu.Items)
            {
                if (item is MenuItem menuItem)
                {
                    bool itemMatch = UpdateMenuSearchVisibility(menuItem, words);
                    hasMatch |= itemMatch;
                    if (itemMatch)
                    {
                        // 统计命中的叶子命令数，而非顶级分组数，使"找到 N 个匹配命令"贴合用户感知
                        matchCount += CountMatchingLeafMenuItems(menuItem, words);
                    }
                }
                else if (item is Separator separator)
                {
                    separator.Visibility = words.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
                }
            }

            bool isSearching = words.Length > 0;
            menuSearchMatchCountText.Text = UiText.Format("MenuSearchMatchCount", matchCount);
            menuSearchMatchCountText.Visibility = isSearching && hasMatch ? Visibility.Visible : Visibility.Collapsed;
            menuSearchNoResultsText.Visibility = isSearching && !hasMatch ? Visibility.Visible : Visibility.Collapsed;
        }

        private void OnMenuSearchPreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                ClearMenuSearchOrClose();
                e.Handled = true;
                return;
            }

            if (e.Key == Key.Down && TryFocusFirstVisibleMenuItem())
            {
                e.Handled = true;
            }
        }

        private void ClearMenuSearchOrClose()
        {
            if (!string.IsNullOrEmpty(menuSearchBox.Text))
            {
                menuSearchBox.Clear();
                return;
            }

            mainContextMenu.IsOpen = false;
        }

        private bool TryFocusFirstVisibleMenuItem()
        {
            foreach (object item in mainContextMenu.Items)
            {
                if (item is MenuItem { Visibility: Visibility.Visible, IsEnabled: true } menuItem)
                {
                    menuItem.Focus();
                    return true;
                }
            }

            return false;
        }

        private static bool UpdateMenuSearchVisibility(MenuItem menuItem, string[] words)
        {
            bool childMatch = false;
            foreach (object child in menuItem.Items)
            {
                if (child is MenuItem childMenuItem)
                {
                    childMatch |= UpdateMenuSearchVisibility(childMenuItem, words);
                }
            }

            bool ownMatch = MatchesOwnSearchTerms(menuItem, words);
            bool visible = ownMatch || childMatch;
            menuItem.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
            return visible;
        }

        /// <summary>递归统计命中的叶子命令数（无子菜单的可执行项），用于搜索结果计数。</summary>
        private static int CountMatchingLeafMenuItems(MenuItem menuItem, string[] words)
        {
            int count = 0;
            foreach (object child in menuItem.Items)
            {
                if (child is MenuItem childMenuItem)
                {
                    count += CountMatchingLeafMenuItems(childMenuItem, words);
                }
            }

            if (menuItem.Items.Count == 0 && MatchesOwnSearchTerms(menuItem, words))
            {
                count++;
            }

            return count;
        }

        private static bool MatchesOwnSearchTerms(MenuItem menuItem, string[] words)
        {
            return words.Length == 0
                || MatchesAllWords(menuItem.Header?.ToString(), words)
                || MatchesAllWords(menuItem.InputGestureText, words)
                || MatchesAllWords(menuItem.ToolTip?.ToString(), words);
        }

        /// <summary>多个空格分隔的关键词都必须命中（AND 语义）。</summary>
        private static bool MatchesAllWords(string? text, string[] words)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            string normalized = NormalizeSearchText(text);
            foreach (string word in words)
            {
                if (!normalized.Contains(NormalizeSearchText(word), StringComparison.Ordinal))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>归一化：转小写并移除常见分隔符（空格、加减号、括号、斜杠、点等），方便按快捷键或关键词连写搜索。</summary>
        private static string NormalizeSearchText(string text)
        {
            Span<char> buffer = text.Length <= 256 ? stackalloc char[text.Length] : new char[text.Length];
            int count = 0;
            foreach (char c in text)
            {
                if (char.IsWhiteSpace(c) || c is '+' or '-' or '.' or '/' or '\\' or '(' or ')' or '（' or '）')
                {
                    continue;
                }

                buffer[count++] = char.ToLowerInvariant(c);
            }

            return count == text.Length ? text.ToLowerInvariant() : new string(buffer[..count]);
        }

        private void OnZoomInClick(object sender, RoutedEventArgs e) => ZoomAtViewportCenter(1.25);

        private void OnZoomOutClick(object sender, RoutedEventArgs e) => ZoomAtViewportCenter(0.8);

        private void ZoomAtViewportCenter(double factor)
        {
            ImageViewerViewportState state = _controlComposition.ViewportController.CurrentState;
            if (state.Scale <= 0 || rootGrid.ActualWidth <= 0 || rootGrid.ActualHeight <= 0)
            {
                return;
            }

            Point viewportCenter = new(rootGrid.ActualWidth / 2, rootGrid.ActualHeight / 2);
            Point imagePoint = new(
                (viewportCenter.X - state.TranslateX) / state.Scale,
                (viewportCenter.Y - state.TranslateY) / state.Scale);
            _controlComposition.ViewportController.ZoomAt(imagePoint, factor);
        }

        private void UpdateContextMenuState() => _contextMenuController.UpdateState();

        /// <summary>
        /// 更新常驻状态栏：显示当前图像像素尺寸与文件路径；无图像时显示"未加载数据"。
        /// Chinese: 每次图像源变化时刷新状态栏上的图像信息，无图像时给出占位提示。
        /// English: Refreshes the persistent status bar with the current image size and file path.
        /// </summary>
        private void UpdateStatusBar()
        {
            if (statusBarTextBlock is null || statusBarBorder is null)
            {
                return;
            }

            string text;
            if (ImageSource is null || !ImageViewerImageSourceUtilities.TryGetSourceImageSize(ImageSource, out Size imageSize))
            {
                text = UiText.Get("StatusNoDataLoaded");
                statusBarTextBlock.ToolTip = null;
            }
            else
            {
                string? filePath = _controlComposition?.ViewportController.TryGetCurrentImagePath();
                text = string.IsNullOrEmpty(filePath)
                    ? UiText.Format("StatusBarImageSizeOnly", (int)imageSize.Width, (int)imageSize.Height)
                    : UiText.Format("StatusBarImageSizeAndPath", (int)imageSize.Width, (int)imageSize.Height, filePath);
                statusBarTextBlock.ToolTip = text;
            }

            if (!string.Equals(statusBarTextBlock.Text, text, StringComparison.Ordinal))
            {
                statusBarTextBlock.Text = text;
                // 状态栏已声明 LiveSetting=Polite，文本变化时主动通知屏幕阅读器
                RaiseLiveRegionChanged(statusBarTextBlock);
            }

            statusBarBorder.Visibility = Visibility.Visible;
        }

        /// <summary>
        /// 更新常驻标定状态。只要像素尺寸、单位或镜头畸变参数被设置，就在状态栏中保留醒目标识。
        /// Chinese: 标定状态不再依赖一次性的成功提示或选中 ROI 才能看到。
        /// English: Keep calibration visible in the status bar instead of relying on a transient hint or a selected ROI.
        /// </summary>
        private void UpdateCalibrationIndicator()
        {
            if (calibrationBadge is null || calibrationBadgeTextBlock is null)
            {
                return;
            }

            string unit = string.IsNullOrWhiteSpace(PhysicalUnit)
                ? UiText.Get("InfoUnitPixels")
                : PhysicalUnit.Trim();
            bool isCalibrated = double.IsFinite(PixelSize)
                && PixelSize > 0
                && (Calibration is not null
                    || Math.Abs(PixelSize - 1.0) > 1e-9
                    || !string.Equals(unit, UiText.Get("InfoUnitPixels"), StringComparison.OrdinalIgnoreCase));

            if (!isCalibrated)
            {
                calibrationBadge.Visibility = Visibility.Collapsed;
                calibrationBadgeTextBlock.Text = string.Empty;
                calibrationBadgeTextBlock.ToolTip = null;
                return;
            }

            string text = UiText.FormatInvariant("CalibrationBadgeText", PixelSize, unit);
            if (Calibration is not null)
            {
                text += " " + UiText.Get("CalibrationBadgeDistortionSuffix");
            }

            bool textChanged = !string.Equals(calibrationBadgeTextBlock.Text, text, StringComparison.Ordinal);
            calibrationBadgeTextBlock.Text = text;
            calibrationBadgeTextBlock.ToolTip = text;
            calibrationBadge.Visibility = Visibility.Visible;
            if (textChanged)
            {
                RaiseLiveRegionChanged(calibrationBadgeTextBlock);
            }
        }

        private async void OnViewCommandMenuClick(object sender, RoutedEventArgs e)
        {
            await RunUiOperationAsync("视图菜单命令", () =>
            {
                if (TryGetTaggedCommand(sender, out ImageViewerViewCommand command))
                {
                    _viewCommandController.Execute(command);
                    UpdateContextMenuState();
                }

                return Task.CompletedTask;
            });
        }

        private async void OnAnalysisCommandMenuClick(object sender, RoutedEventArgs e)
        {
            await RunUiOperationAsync("分析菜单命令", () =>
            {
                if (TryGetTaggedCommand(sender, out ImageViewerAnalysisCommand command))
                {
                    _analysisCommandController.Execute(command);
                    UpdateContextMenuState();
                }

                return Task.CompletedTask;
            });
        }

        private async void OnRoiMenuCommandClick(object sender, RoutedEventArgs e)
        {
            await RunUiOperationAsync("ROI 菜单命令", () =>
            {
                if (TryGetTaggedCommand(sender, out ImageViewerRoiMenuCommand command))
                {
                    _roiMenuCommandController.Execute(command);
                    UpdateContextMenuState();
                }

                return Task.CompletedTask;
            });
        }

        private async void OnFileMenuCommandClick(object sender, RoutedEventArgs e)
        {
            await RunUiOperationAsync("文件菜单命令", () => HandleFileMenuCommandClickAsync(sender));
        }

        private async void OnToolbarFileCommandClick(object sender, RoutedEventArgs e)
        {
            await RunUiOperationAsync("工具栏文件命令", () => HandleFileMenuCommandClickAsync(sender));
        }

        private void OnToolbarViewCommandClick(object sender, RoutedEventArgs e)
        {
            if (TryGetTaggedCommand(sender, out ImageViewerViewCommand command))
            {
                _viewCommandController.Execute(command);
                UpdateContextMenuState();
            }
        }

        private void OnToolbarPanelToggleChanged(object sender, RoutedEventArgs e)
        {
            UpdateContextMenuState();
        }

        private async Task HandleFileMenuCommandClickAsync(object sender)
        {
            if (sender is MenuItem { Tag: ImageViewerRecentProjectMenuTag recentProject, IsEnabled: true })
            {
                await _fileMenuCommandController.OpenRecentProjectAsync(recentProject.ProjectPath);
                return;
            }

            if (TryGetTaggedCommand(sender, out ImageViewerFileMenuCommand command))
            {
                await _fileMenuCommandController.ExecuteAsync(command);
            }
        }

        private void OnDragOver(object sender, DragEventArgs e)
        {
            DroppedContentController.HandleDragOver(e);
            // 拖入可打开文件时显示遮罩提示"松开以打开"，否则隐藏
            dropHintOverlay.Visibility = e.Effects == DragDropEffects.Copy ? Visibility.Visible : Visibility.Collapsed;
        }

        private void OnDragLeave(object sender, DragEventArgs e) => dropHintOverlay.Visibility = Visibility.Collapsed;

        private async void OnDrop(object sender, DragEventArgs e)
        {
            dropHintOverlay.Visibility = Visibility.Collapsed;
            await RunUiOperationAsync("拖放打开图像", () => _droppedContentController.HandleDropAsync(e));
        }

        private async void OnRetryImageLoadClick(object sender, RoutedEventArgs e)
        {
            await RunUiOperationAsync("重试加载图像", RetryLastImageLoadAsync);
        }

        private void OnDismissDiagnosticErrorClick(object sender, RoutedEventArgs e)
        {
            DismissDiagnosticError();
        }

        private async void OnRecoverAutoSaveClick(object sender, RoutedEventArgs e)
        {
            await RunUiOperationAsync(
                "恢复自动保存",
                _controlComposition.SessionController.RecoverLatestAutoSaveAsync);
        }

        private void OnDismissRecoveryClick(object sender, RoutedEventArgs e)
        {
            _controlComposition.SessionController.DismissRecoveryPrompt();
        }

        public Task ShowOpenImageDialogAsync() => _imageSourceController.OpenImageAsync();

        [EditorBrowsable(EditorBrowsableState.Never)]
        [Obsolete("Use ShowOpenImageDialogAsync() instead.", false)]
        public Task OpenImageAsync() => ShowOpenImageDialogAsync();

        private void OnMouseWheel(object sender, MouseWheelEventArgs e) => _interactionController.HandleMouseWheel(e);

        private void OnMouseDown(object sender, MouseButtonEventArgs e)
        {
            _interactionController.HandleMouseDown(e);
        }

        private void OnMouseMove(object sender, MouseEventArgs e) => _interactionController.HandleMouseMove(e);

        private void OnMouseUp(object sender, MouseButtonEventArgs e) => _interactionController.HandleMouseUp(e);
    }
}
