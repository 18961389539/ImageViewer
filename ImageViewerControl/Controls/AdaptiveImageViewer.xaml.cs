using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Input;
using ImageViewer.Localization;
using ImageViewer.Models;
using ImageViewer.Services;

namespace ImageViewer.Controls
{
    public enum AdaptiveDisplayMode
    {
        Auto,
        TwoDimensional,
        ThreeDimensional,
        AxialSlice,
        Coronal,
        Sagittal
    }

    public partial class AdaptiveImageViewer : UserControl, IDisposable, IAsyncDisposable
    {
        private readonly ImageViewer _imageViewer;
        private readonly VolumeViewer _volumeViewer;
        private readonly Volume3DViewer _volume3DViewer;
        private ImageSource? _imageSource;
        private VolumeData? _volume;
        private AdaptiveDisplayMode _displayMode = AdaptiveDisplayMode.Auto;
        private bool _isDisposed;
        private CancellationTokenSource? _operationCancellation;


        public AdaptiveImageViewer()
        {
            InitializeComponent();
            Loaded += OnLoaded;
            _imageViewer = new ImageViewer();
            // 自适应宿主默认收起工具栏，把画布留给图像；点击左上角“工具栏”入口即可打开完整命令面板。
            _imageViewer.ShowToolbar = false;
            _volumeViewer = new VolumeViewer();
            _volume3DViewer = new Volume3DViewer();
            _volume3DViewer.SwitchToAxialSliceRequested += OnSwitchToAxialSliceRequested;
            _volumeViewer.CurrentSliceChanged += OnCurrentSliceChanged;
            ApplyTheme();
            UpdateDisplayedView();
            UpdateStatus();
            UpdateButtonStates();
        }

        public ImageSource? ImageSource
        {
            get => _imageSource;
            set
            {
                ObjectDisposedException.ThrowIf(_isDisposed, this);
                _imageSource = value;
                _imageViewer.ImageSource = value;
                UpdateDisplayedView();
                UpdateStatus();
                UpdateButtonStates();
            }
        }


        public VolumeData? Volume
        {
            get => _volume;
            set
            {
                ObjectDisposedException.ThrowIf(_isDisposed, this);
                _mprCancellation?.Cancel();
                _operationCancellation?.Cancel();
                _volume = value;
                _volumeViewer.Volume = value;
                _volume3DViewer.Volume = value;
                _coronalSliceIndex = value == null ? 0 : value.Height / 2;
                _sagittalSliceIndex = value == null ? 0 : value.Width / 2;
                _pendingSegmentation = null;
                _qualityReport = null;
                anomalyList.Items.Clear();
                segmentationText.Text = string.Empty;
                retryButton.Visibility = Visibility.Collapsed;
                UpdateDataQualityPanelVisibility();
                _volume3DViewer.SetCurrentSlice(_volumeViewer.CurrentSliceIndex);
                UpdateDisplayedView();
                UpdateStatus();
                UpdateButtonStates();
            }
        }

        public AdaptiveDisplayMode DisplayMode
        {
            get => _displayMode;
            set
            {
                ObjectDisposedException.ThrowIf(_isDisposed, this);
                _displayMode = value;
                UpdateDisplayedView();
                UpdateStatus();
                UpdateButtonStates();
            }
        }

        public ImageViewer ImageViewer => _imageViewer;

        public bool IsDirty => _imageViewer.IsDirty;

        public Task SaveSessionAsync() => _imageViewer.SaveSessionAsync();

        /// <summary>Marks host-provided initial content as the saved baseline.</summary>
        public void MarkDocumentClean() => _imageViewer.MarkDocumentClean();

        public VolumeViewer VolumeViewer => _volumeViewer;

        public Volume3DViewer Volume3DViewer => _volume3DViewer;

        public UserControl ActiveView => ResolveView();

        public void Dispose()
        {
            if (_isDisposed)
            {
                return;
            }

            _isDisposed = true;
            _imageViewer.Dispose();
            _volumeViewer.Dispose();
            _volume3DViewer.SwitchToAxialSliceRequested -= OnSwitchToAxialSliceRequested;
            _volumeViewer.CurrentSliceChanged -= OnCurrentSliceChanged;
            Loaded -= OnLoaded;
            _volume3DViewer.Dispose();
            _mprCancellation?.Cancel();
            _operationCancellation?.Cancel();
            contentHost.Content = null;
            _imageSource = null;
            _volume = null;
            GC.SuppressFinalize(this);
        }

        public ValueTask DisposeAsync()
        {
            Dispose();
            GC.SuppressFinalize(this);
            return ValueTask.CompletedTask;
        }

        private void UpdateDisplayedView()
        {
            if (_isDisposed)
            {
                return;
            }

            UserControl view = ResolveView();
            if (!ReferenceEquals(contentHost.Content, view))
            {
                contentHost.Content = view;
            }
        }

        private UserControl ResolveView()
        {
            return _displayMode switch
            {
                AdaptiveDisplayMode.TwoDimensional => _imageViewer,
                AdaptiveDisplayMode.ThreeDimensional => _volume3DViewer,
                AdaptiveDisplayMode.AxialSlice => _volumeViewer,
                AdaptiveDisplayMode.Coronal => _imageViewer,
                AdaptiveDisplayMode.Sagittal => _imageViewer,
                _ when _volume != null => _volume3DViewer,
                _ => _imageViewer
            };
        }

        private void OnSwitchToAxialSliceRequested(object? sender, EventArgs e)
        {
            DisplayMode = AdaptiveDisplayMode.AxialSlice;
        }

        private void OnCurrentSliceChanged(object? sender, EventArgs e)
        {
            _volume3DViewer.SetCurrentSlice(_volumeViewer.CurrentSliceIndex);
            if (_volume != null)
            {
                statusText.Text = UiText.Format("StatusAxialSlice", _volumeViewer.CurrentSliceIndex + 1, _volume.Depth);
            }
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            Focus();
        }

        private void OnAutoClick(object sender, RoutedEventArgs e) => DisplayMode = AdaptiveDisplayMode.Auto;
        private void OnTwoDimensionalClick(object sender, RoutedEventArgs e) => DisplayMode = AdaptiveDisplayMode.TwoDimensional;
        private void OnThreeDimensionalClick(object sender, RoutedEventArgs e) => DisplayMode = AdaptiveDisplayMode.ThreeDimensional;
        private void OnAxialClick(object sender, RoutedEventArgs e) => DisplayMode = AdaptiveDisplayMode.AxialSlice;
        private async void OnCoronalClick(object sender, RoutedEventArgs e) => await SetMprModeAsync(AdaptiveDisplayMode.Coronal);
        private async void OnSagittalClick(object sender, RoutedEventArgs e) => await SetMprModeAsync(AdaptiveDisplayMode.Sagittal);


        private async void OnKeyDown(object sender, KeyEventArgs e)
        {
            // 让嵌套 ImageViewer 先处理 ROI 的方向键和 Esc；文本框、下拉框、列表和滑块
            // 也必须保留自己的键盘行为，避免外层查看器抢走输入。
            if (e.Handled || IsKeyboardInputControlFocused())
            {
                return;
            }

            switch (e.Key)
            {
                case Key.D1 when _volume != null:
                    DisplayMode = AdaptiveDisplayMode.AxialSlice;
                    break;
                case Key.D2 when _volume != null:
                    await SetMprModeAsync(AdaptiveDisplayMode.Coronal);
                    break;
                case Key.D3 when _volume != null:
                    await SetMprModeAsync(AdaptiveDisplayMode.Sagittal);
                    break;
                case Key.D4 when _volume != null:
                    DisplayMode = AdaptiveDisplayMode.ThreeDimensional;
                    break;
                case Key.Home: ResetActiveView(); break;
                case Key.F when _volume != null && ResolveMode() == AdaptiveDisplayMode.ThreeDimensional:
                    _volume3DViewer.FitVolume();
                    statusText.Text = UiText.Get("StatusVolumeFitted");
                    break;
                case Key.Up when IsMprModeActive:
                    await StepMprSliceAsync(1);
                    break;
                case Key.Down when IsMprModeActive:
                    await StepMprSliceAsync(-1);
                    break;
                case Key.Escape:
                    bool cancelled = false;
                    if (_operationCancellation is not null)
                    {
                        _operationCancellation.Cancel();
                        cancelled = true;
                    }

                    if (_mprCancellation is not null)
                    {
                        _mprCancellation.Cancel();
                        cancelled = true;
                    }

                    if (!cancelled)
                    {
                        return;
                    }

                    break;
                default: return;
            }

            e.Handled = true;
        }

        private static bool IsKeyboardInputControlFocused()
        {
            IInputElement? focusedElement = Keyboard.FocusedElement;
            return focusedElement is TextBoxBase
                or PasswordBox
                or ComboBox
                or Selector
                or Slider;
        }

        private void ResetActiveView()
        {
            if (ResolveMode() == AdaptiveDisplayMode.ThreeDimensional)
            {
                _volume3DViewer.ResetCamera();
                statusText.Text = UiText.Get("StatusCameraReset");
            }
            else if (_volume == null)
            {
                statusText.Text = UiText.Get("Status2DViewReset");
            }
            else
            {
                _volumeViewer.SelectSlice(0);
                statusText.Text = UiText.Get("Status2DViewResetFirstSlice");
            }
        }

        private void OnCancelClick(object sender, RoutedEventArgs e)
        {
            _operationCancellation?.Cancel();
            _mprCancellation?.Cancel();
            statusText.Text = UiText.Get("StatusCancelRequested");
            operationProgress.Visibility = Visibility.Collapsed;
            UpdateButtonStates();
        }

        private CancellationTokenSource BeginOperation(string message)
        {
            _operationCancellation?.Cancel();
            var operation = new CancellationTokenSource();
            _operationCancellation = operation;
            statusText.Text = message;
            operationProgress.Visibility = Visibility.Visible;
            UpdateButtonStates();
            return operation;
        }

        private bool IsCurrentOperation(CancellationTokenSource operation)
        {
            return ReferenceEquals(_operationCancellation, operation) && !operation.IsCancellationRequested && !_isDisposed;
        }

        private void EndOperation(CancellationTokenSource operation)
        {
            if (!ReferenceEquals(_operationCancellation, operation))
            {
                operation.Dispose();
                return;
            }

            operation.Dispose();
            _operationCancellation = null;
            if (!_isDisposed)
            {
                operationProgress.Visibility = Visibility.Collapsed;
                UpdateButtonStates();
            }
        }

    }
}
