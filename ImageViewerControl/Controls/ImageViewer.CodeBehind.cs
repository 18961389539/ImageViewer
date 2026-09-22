using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using ImageViewer.Localization;

namespace ImageViewer.Controls
{
    public partial class ImageViewer
    {
        private void InitializeEventHandlers()
        {
            rootGrid.MouseWheel += OnMouseWheel;
            rootGrid.MouseDown += OnMouseDown;
            rootGrid.MouseMove += OnMouseMove;
            rootGrid.MouseUp += OnMouseUp;
            rootGrid.MouseRightButtonDown += OnMouseRightButtonDown;
            rootGrid.LostMouseCapture += OnLostMouseCapture;
            rootGrid.DragOver += OnDragOver;
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
            rootGrid.Drop -= OnDrop;
            rootGrid.SizeChanged -= OnRootGridSizeChanged;
            KeyDown -= OnKeyDown;
        }

        private void OnRootGridSizeChanged(object sender, SizeChangedEventArgs e)
        {
            _imageViewStateController.HandleRootGridSizeChanged();
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
                ShowStatusHint(UiText.Format("StatusToolActivated", menuItem.Header?.ToString() ?? string.Empty));
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
            if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.O)
            {
                e.Handled = true;
                await RunUiOperationAsync(
                    "打开图像快捷键",
                    () => _fileMenuCommandController.ExecuteAsync(ImageViewerFileMenuCommand.OpenImage));
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

        private void OnMouseRightButtonDown(object sender, MouseButtonEventArgs e) => _interactionController.HandleMouseRightButtonDown(e);

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
                        matchCount++;
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

            bool ownMatch = words.Length == 0
                || MatchesAllWords(menuItem.Header?.ToString(), words)
                || MatchesAllWords(menuItem.InputGestureText, words)
                || MatchesAllWords(menuItem.ToolTip?.ToString(), words);
            bool visible = ownMatch || childMatch;
            menuItem.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
            return visible;
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
            }

            statusBarBorder.Visibility = Visibility.Visible;
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

        private void OnDragOver(object sender, DragEventArgs e) => DroppedContentController.HandleDragOver(e);

        private async void OnDrop(object sender, DragEventArgs e)
        {
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