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
        private CancellationTokenSource? _mprCancellation;
        private long _mprRequestGeneration;
        private SegmentationResult? _pendingSegmentation;
        private VolumeQualityReport? _qualityReport;
        private int _coronalSliceIndex;
        private int _sagittalSliceIndex;
        private bool _isUpdatingMprSlider;

        public AdaptiveImageViewer()
        {
            InitializeComponent();
            Loaded += OnLoaded;
            _imageViewer = new ImageViewer();
            // 自适应宿主面向演示和快速浏览，默认显示常用工具、测量结果和 ROI 列表。
            _imageViewer.ShowToolbar = true;
            _volumeViewer = new VolumeViewer();
            _volume3DViewer = new Volume3DViewer();
            _volume3DViewer.SwitchToAxialSliceRequested += OnSwitchToAxialSliceRequested;
            _volumeViewer.CurrentSliceChanged += OnCurrentSliceChanged;
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

        private async void OnQualityClick(object sender, RoutedEventArgs e)
        {
            if (_volume == null)
            {
                statusText.Text = UiText.Get("StatusLoadVolumeFirstQuality");
                return;
            }

            CancellationTokenSource operation = BeginOperation(UiText.Get("StatusAnalyzingQuality"));
            dataQualityPanel.Visibility = Visibility.Visible;
            try
            {
                VolumeData volume = _volume!;
                VolumeQualityReport report = await Task.Run(() => VolumeQualityAnalyzer.Analyze(volume, _imageViewer.QualityProfile), operation.Token);
                if (!IsCurrentOperation(operation))
                {
                    return;
                }

                _qualityReport = report;
                anomalyList.Items.Clear();
                foreach (VolumeAnomaly anomaly in report.Anomalies)
                {
                    anomalyList.Items.Add(UiText.Format("StatusAnomalyItem", anomaly.SliceIndex + 1, anomaly.Message));
                }

                statusText.Text = report.HasAnomalies ? UiText.Format("StatusQualityFound", report.Anomalies.Count) : UiText.Get("StatusQualityPassed");
                retryButton.Visibility = Visibility.Collapsed;
            }
            catch (OperationCanceledException) when (operation.Token.IsCancellationRequested)
            {
                if (IsCurrentOperation(operation))
                {
                    statusText.Text = UiText.Get("StatusQualityCancelled");
                }
            }
            catch (Exception)
            {
                if (IsCurrentOperation(operation))
                {
                    statusText.Text = UiText.Get("StatusQualityFailed");
                    retryButton.Visibility = Visibility.Visible;
                }
            }
            finally
            {
                bool wasCurrent = ReferenceEquals(_operationCancellation, operation);
                EndOperation(operation);
                if (wasCurrent)
                {
                    UpdateDataQualityPanelVisibility();
                }
            }
        }

        private async void OnSegmentClick(object sender, RoutedEventArgs e)
        {
            if (_volume == null)
            {
                statusText.Text = UiText.Get("StatusLoadVolumeFirstSegmentation");
                return;
            }

            VolumeData volume = _volume!;
            int sliceIndex = Math.Max(0, _volumeViewer.CurrentSliceIndex);
            BitmapSource slice = volume.GetAxialSlice(sliceIndex);
            CancellationTokenSource operation = BeginOperation(UiText.Get("StatusAnalyzingSegmentation"));
            try
            {
                SegmentationResult segmentation = await Task.Run(() => SegmentationPipelineService
                    .Segment(slice, new Rect(0, 0, slice.PixelWidth, slice.PixelHeight), cancellationToken: operation.Token), operation.Token);
                if (!IsCurrentOperation(operation) || !ReferenceEquals(_volume, volume))
                {
                    return;
                }

                _pendingSegmentation = segmentation
                    with { SliceIndex = sliceIndex };
                segmentationText.Text = UiText.Format("StatusSegmentationCandidates", _pendingSegmentation.Blobs.Count);
                statusText.Text = UiText.Get("StatusSegmentationComplete");
                UpdateDataQualityPanelVisibility();
            }
            catch (OperationCanceledException) when (operation.Token.IsCancellationRequested)
            {
                if (IsCurrentOperation(operation))
                {
                    statusText.Text = UiText.Get("StatusSegmentationCancelled");
                }
            }
            catch (Exception)
            {
                if (IsCurrentOperation(operation))
                {
                    _pendingSegmentation = null;
                    statusText.Text = UiText.Get("StatusSegmentationFailed");
                }
            }
            finally
            {
                bool wasCurrent = ReferenceEquals(_operationCancellation, operation);
                EndOperation(operation);
                if (wasCurrent)
                {
                    UpdateButtonStates();
                }
            }
        }

        private async Task SetMprModeAsync(AdaptiveDisplayMode mode)
        {
            if (_volume == null)
            {
                statusText.Text = UiText.Get("StatusLoadVolumeFirstMpr");
                return;
            }

            try
            {
                VolumeSliceOrientation orientation = mode == AdaptiveDisplayMode.Coronal ? VolumeSliceOrientation.Coronal : VolumeSliceOrientation.Sagittal;
                if (await UpdateMprSliceAsync(mode, orientation, GetMprSliceIndex(mode)))
                {
                    DisplayMode = mode;
                }
            }
            catch (Exception)
            {
                statusText.Text = UiText.Format("StatusUnableCreateView", LocalizeMode(mode));
                retryButton.Visibility = Visibility.Visible;
                UpdateDataQualityPanelVisibility();
            }
        }

        private void OnAnomalySelected(object sender, SelectionChangedEventArgs e)
        {
            if (anomalyList.SelectedIndex < 0 || _qualityReport == null || _volume == null)
            {
                return;
            }

            VolumeAnomaly anomaly = _qualityReport.Anomalies[anomalyList.SelectedIndex];
            _volumeViewer.SelectSlice(anomaly.SliceIndex);
            DisplayMode = AdaptiveDisplayMode.AxialSlice;
            statusText.Text = UiText.Format("StatusAnomalyLocated", anomaly.SliceIndex + 1);
        }

        private void OnAcceptSegmentationClick(object sender, RoutedEventArgs e)
        {
            if (_pendingSegmentation is not SegmentationResult segmentation || _volume == null || segmentation.Blobs.Count == 0)
            {
                statusText.Text = UiText.Get("StatusSegmentationEmpty");
                return;
            }

            BitmapSource slice = _volume.GetAxialSlice(segmentation.SliceIndex);
            var roi = new BlobAnalysisRoi
            {
                Center = new PointD(slice.PixelWidth / 2d, slice.PixelHeight / 2d),
                Width = slice.PixelWidth,
                Height = slice.PixelHeight,
                Label = UiText.Get("SegmentationRoiLabel"),
                DetectedBlobs = segmentation.Blobs.ToList()
            };

            _imageViewer.ImageSource = slice;
            if (!_imageViewer.AddRoi(roi))
            {
                statusText.Text = UiText.Get("StatusSegmentationFailed");
                return;
            }

            _imageViewer.ShowRoiList = true;
            DisplayMode = AdaptiveDisplayMode.TwoDimensional;
            _pendingSegmentation = null;
            segmentationText.Text = string.Empty;
            UpdateDataQualityPanelVisibility();
            statusText.Text = UiText.Format("StatusSegmentationAccepted", roi.DetectedBlobs.Count);
            UpdateButtonStates();
        }

        private void OnRejectSegmentationClick(object sender, RoutedEventArgs e)
        {
            _pendingSegmentation = null;
            segmentationText.Text = UiText.Get("StatusCandidateRejected");
            UpdateDataQualityPanelVisibility();
            statusText.Text = UiText.Get("StatusNoRoiChanged");
            UpdateButtonStates();
        }

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

        private bool IsMprModeActive => _volume != null &&
            (_displayMode == AdaptiveDisplayMode.Coronal || _displayMode == AdaptiveDisplayMode.Sagittal);

        private static bool IsKeyboardInputControlFocused()
        {
            IInputElement? focusedElement = Keyboard.FocusedElement;
            return focusedElement is TextBoxBase
                or PasswordBox
                or ComboBox
                or Selector
                or Slider;
        }

        internal void StepMprSlice(int offset)
        {
            _ = StepMprSliceAsync(offset);
        }

        private async Task StepMprSliceAsync(int offset)
        {
            if (_volume == null || (_displayMode != AdaptiveDisplayMode.Coronal && _displayMode != AdaptiveDisplayMode.Sagittal))
            {
                return;
            }

            VolumeSliceOrientation orientation = _displayMode == AdaptiveDisplayMode.Coronal
                ? VolumeSliceOrientation.Coronal
                : VolumeSliceOrientation.Sagittal;
            int maximumSliceIndex = orientation == VolumeSliceOrientation.Coronal ? _volume.Height - 1 : _volume.Width - 1;
            int sliceIndex = Math.Clamp(GetMprSliceIndex(_displayMode) + offset, 0, maximumSliceIndex);
            try
            {
                await UpdateMprSliceAsync(_displayMode, orientation, sliceIndex);
            }
            catch (Exception)
            {
                statusText.Text = UiText.Format("StatusUnableCreateView", LocalizeMode(_displayMode));
                retryButton.Visibility = Visibility.Visible;
                UpdateDataQualityPanelVisibility();
            }
        }

        private int GetMprSliceIndex(AdaptiveDisplayMode mode) =>
            mode == AdaptiveDisplayMode.Coronal ? _coronalSliceIndex : _sagittalSliceIndex;

        private async Task<bool> UpdateMprSliceAsync(AdaptiveDisplayMode mode, VolumeSliceOrientation orientation, int sliceIndex)
        {
            VolumeData? volume = _volume;
            if (volume == null || _isDisposed)
            {
                return false;
            }

            // Publish the requested plane immediately. Pixel extraction remains on the
            // worker thread below, while the 3D crosshair and navigation state respond
            // to the user's command without waiting for a large MPR reconstruction.
            ApplyMprSelection(mode, sliceIndex, volume);
            (CancellationTokenSource cancellation, long generation) = BeginMprRequest();
            try
            {
                BitmapSource slice = await Task.Run(
                    () => VolumeSliceService.GetSlice(volume, orientation, sliceIndex, cancellation.Token),
                    cancellation.Token);

                if (!IsCurrentMprRequest(cancellation, generation, volume))
                {
                    return false;
                }

                _imageViewer.ImageSource = slice;
                UpdateMprStatus(mode, sliceIndex, volume);

                UpdateMprSliceControl(mode);
                return true;
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
            {
                return false;
            }
            finally
            {
                EndMprRequest(cancellation);
            }
        }

        private void ApplyMprSelection(AdaptiveDisplayMode mode, int sliceIndex, VolumeData volume)
        {
            if (mode == AdaptiveDisplayMode.Coronal)
            {
                _coronalSliceIndex = Math.Clamp(sliceIndex, 0, volume.Height - 1);
                _volume3DViewer.SetCoronalSlice(_coronalSliceIndex);
            }
            else
            {
                _sagittalSliceIndex = Math.Clamp(sliceIndex, 0, volume.Width - 1);
                _volume3DViewer.SetSagittalSlice(_sagittalSliceIndex);
            }

            // A direct DisplayMode assignment can arrive before the first async MPR
            // result. Keep the view populated while that result is being reconstructed.
            if (_imageViewer.ImageSource == null)
            {
                _imageViewer.ImageSource = volume.GetAxialSlice(_volumeViewer.CurrentSliceIndex);
            }

            UpdateMprSliceControl(mode);
        }

        private void UpdateMprStatus(AdaptiveDisplayMode mode, int sliceIndex, VolumeData volume)
        {
            statusText.Text = mode == AdaptiveDisplayMode.Coronal
                ? UiText.Format("StatusCoronalSlice", sliceIndex + 1, volume.Height)
                : UiText.Format("StatusSagittalSlice", sliceIndex + 1, volume.Width);
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

        private void OnRetryQualityClick(object sender, RoutedEventArgs e) => OnQualityClick(sender, e);

        private void OnCancelClick(object sender, RoutedEventArgs e)
        {
            _operationCancellation?.Cancel();
            _mprCancellation?.Cancel();
            statusText.Text = UiText.Get("StatusCancelRequested");
            operationProgress.Visibility = Visibility.Collapsed;
            UpdateButtonStates();
        }

        private void OnPreviousMprSliceClick(object sender, RoutedEventArgs e) => StepMprSlice(-1);

        private void OnNextMprSliceClick(object sender, RoutedEventArgs e) => StepMprSlice(1);

        private async void OnMprSliceValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isUpdatingMprSlider || _volume == null || (_displayMode != AdaptiveDisplayMode.Coronal && _displayMode != AdaptiveDisplayMode.Sagittal))
            {
                return;
            }

            int sliceIndex = (int)Math.Round(e.NewValue);
            VolumeSliceOrientation orientation = _displayMode == AdaptiveDisplayMode.Coronal
                ? VolumeSliceOrientation.Coronal
                : VolumeSliceOrientation.Sagittal;
            try
            {
                await UpdateMprSliceAsync(_displayMode, orientation, sliceIndex);
            }
            catch (Exception)
            {
                statusText.Text = UiText.Format("StatusUnableCreateView", LocalizeMode(_displayMode));
                retryButton.Visibility = Visibility.Visible;
                UpdateDataQualityPanelVisibility();
            }
        }

        private async void OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (_volume != null && (_displayMode == AdaptiveDisplayMode.Coronal || _displayMode == AdaptiveDisplayMode.Sagittal))
            {
                await StepMprSliceAsync(e.Delta > 0 ? 1 : -1);
                e.Handled = true;
            }
        }

        private (CancellationTokenSource Cancellation, long Generation) BeginMprRequest()
        {
            _mprCancellation?.Cancel();
            var cancellation = new CancellationTokenSource();
            _mprCancellation = cancellation;
            long generation = Interlocked.Increment(ref _mprRequestGeneration);
            return (cancellation, generation);
        }

        private bool IsCurrentMprRequest(CancellationTokenSource cancellation, long generation, VolumeData volume)
        {
            return ReferenceEquals(_mprCancellation, cancellation) &&
                   generation == Volatile.Read(ref _mprRequestGeneration) &&
                   ReferenceEquals(_volume, volume) &&
                   !cancellation.IsCancellationRequested &&
                   !_isDisposed;
        }

        private void EndMprRequest(CancellationTokenSource cancellation)
        {
            if (ReferenceEquals(_mprCancellation, cancellation))
            {
                _mprCancellation = null;
            }

            cancellation.Dispose();
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

        private void UpdateStatus()
        {
            string data = _volume == null
                ? (_imageSource == null ? UiText.Get("StatusNoDataLoaded") : UiText.Get("StatusSingleImage"))
                : UiText.Format("StatusVolumeSummary", _volume.Width, _volume.Height, _volume.Depth, _volume.SpacingX, _volume.SpacingY, _volume.SpacingZ);
            statusText.Text = data;
            stateBarText.Text = UiText.Format("StatusModeFormat", LocalizeMode(ResolveMode()), data);
            UpdateMprSliceControl(ResolveMode());
        }

        private void UpdateMprSliceControl(AdaptiveDisplayMode mode)
        {
            bool isMpr = _volume != null && (mode == AdaptiveDisplayMode.Coronal || mode == AdaptiveDisplayMode.Sagittal);
            mprSliceBar.Visibility = isMpr ? Visibility.Visible : Visibility.Collapsed;
            if (!isMpr || _volume == null)
            {
                return;
            }

            int sliceIndex = GetMprSliceIndex(mode);
            int maximum = mode == AdaptiveDisplayMode.Coronal ? _volume.Height - 1 : _volume.Width - 1;
            _isUpdatingMprSlider = true;
            try
            {
                mprSliceSlider.Maximum = Math.Max(0, maximum);
                mprSliceSlider.Value = Math.Clamp(sliceIndex, 0, Math.Max(0, maximum));
            }
            finally
            {
                _isUpdatingMprSlider = false;
            }

            mprSliceStatusText.Text = UiText.Format("AdaptiveMprSliceStatus", LocalizeMode(mode), sliceIndex + 1, maximum + 1);
            mprPreviousButton.IsEnabled = sliceIndex > 0;
            mprNextButton.IsEnabled = sliceIndex < maximum;
        }

        private static string LocalizeMode(AdaptiveDisplayMode mode)
        {
            return mode switch
            {
                AdaptiveDisplayMode.TwoDimensional => UiText.Get("Mode2D"),
                AdaptiveDisplayMode.ThreeDimensional => UiText.Get("Mode3D"),
                AdaptiveDisplayMode.AxialSlice => UiText.Get("ModeAxialSlice"),
                AdaptiveDisplayMode.Coronal => UiText.Get("ModeCoronal"),
                AdaptiveDisplayMode.Sagittal => UiText.Get("ModeSagittal"),
                _ => UiText.Get("ModeAuto")
            };
        }

        private void UpdateButtonStates()
        {
            bool hasVolume = _volume != null;
            bool operationActive = _operationCancellation != null;
            threeDimensionalButton.IsEnabled = hasVolume && !operationActive;
            axialButton.IsEnabled = hasVolume && !operationActive;
            coronalButton.IsEnabled = hasVolume && !operationActive;
            sagittalButton.IsEnabled = hasVolume && !operationActive;
            qualityButton.IsEnabled = hasVolume && !operationActive;
            segmentButton.IsEnabled = hasVolume && !operationActive;
            cancelButton.IsEnabled = operationActive;
            bool isMpr = hasVolume && (_displayMode == AdaptiveDisplayMode.Coronal || _displayMode == AdaptiveDisplayMode.Sagittal);
            mprSliceSlider.IsEnabled = isMpr && !operationActive;
            mprPreviousButton.IsEnabled = isMpr && !operationActive && mprSliceSlider.Value > mprSliceSlider.Minimum;
            mprNextButton.IsEnabled = isMpr && !operationActive && mprSliceSlider.Value < mprSliceSlider.Maximum;
            acceptSegmentationButton.IsEnabled = _pendingSegmentation != null;
            rejectSegmentationButton.IsEnabled = _pendingSegmentation != null;
            UpdateModeButtonVisuals();
            UpdateDataQualityPanelVisibility();
        }

        private void UpdateModeButtonVisuals()
        {
            SetModeButtonVisual(autoButton, _displayMode == AdaptiveDisplayMode.Auto);
            SetModeButtonVisual(twoDimensionalButton, _displayMode == AdaptiveDisplayMode.TwoDimensional);
            SetModeButtonVisual(threeDimensionalButton, _displayMode == AdaptiveDisplayMode.ThreeDimensional);
            SetModeButtonVisual(axialButton, _displayMode == AdaptiveDisplayMode.AxialSlice);
            SetModeButtonVisual(coronalButton, _displayMode == AdaptiveDisplayMode.Coronal);
            SetModeButtonVisual(sagittalButton, _displayMode == AdaptiveDisplayMode.Sagittal);
        }

        private static void SetModeButtonVisual(Button button, bool isSelected)
        {
            button.Background = (Brush)button.FindResource(isSelected ? "AdaptiveModeSelectedBrush" : "AdaptiveModeDefaultBrush");
            button.BorderBrush = (Brush)button.FindResource(isSelected ? "AdaptiveModeSelectedBorderBrush" : "AdaptiveModeDefaultBorderBrush");
            button.FontWeight = isSelected ? FontWeights.SemiBold : FontWeights.Normal;
        }

        private void UpdateDataQualityPanelVisibility()
        {
            bool hasQualityResult = _qualityReport is not null || retryButton.Visibility == Visibility.Visible;
            bool hasSegmentationResult = _pendingSegmentation is not null;
            dataQualityPanel.Visibility = hasQualityResult || hasSegmentationResult ? Visibility.Visible : Visibility.Collapsed;
            segmentationActionsPanel.Visibility = hasSegmentationResult ? Visibility.Visible : Visibility.Collapsed;
            qualityActionsPanel.Visibility = retryButton.Visibility == Visibility.Visible ? Visibility.Visible : Visibility.Collapsed;
        }

        private AdaptiveDisplayMode ResolveMode()
        {
            return _displayMode == AdaptiveDisplayMode.Auto
                ? (_volume == null ? AdaptiveDisplayMode.TwoDimensional : AdaptiveDisplayMode.ThreeDimensional)
                : _displayMode;
        }
    }
}
