using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
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
        private SegmentationResult? _pendingSegmentation;
        private VolumeQualityReport? _qualityReport;
        private int _coronalSliceIndex;
        private int _sagittalSliceIndex;

        public AdaptiveImageViewer()
        {
            InitializeComponent();
            Loaded += OnLoaded;
            _imageViewer = new ImageViewer();
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
                _volume = value;
                _volumeViewer.Volume = value;
                _volume3DViewer.Volume = value;
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
            _operationCancellation?.Cancel();
            _operationCancellation?.Dispose();
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
        private void OnCoronalClick(object sender, RoutedEventArgs e) => SetMprMode(AdaptiveDisplayMode.Coronal);
        private void OnSagittalClick(object sender, RoutedEventArgs e) => SetMprMode(AdaptiveDisplayMode.Sagittal);

        private async void OnQualityClick(object sender, RoutedEventArgs e)
        {
            if (_volume == null)
            {
                statusText.Text = UiText.Get("StatusLoadVolumeFirstQuality");
                return;
            }

            BeginOperation(UiText.Get("StatusAnalyzingQuality"));
            try
            {
                VolumeQualityReport report = await Task.Run(() => VolumeQualityAnalyzer.Analyze(_volume), _operationCancellation!.Token);
                _qualityReport = report;
                anomalyList.Items.Clear();
                foreach (VolumeAnomaly anomaly in report.Anomalies)
                {
                    anomalyList.Items.Add(UiText.Format("StatusAnomalyItem", anomaly.SliceIndex + 1, anomaly.Message));
                }

                statusText.Text = report.HasAnomalies ? UiText.Format("StatusQualityFound", report.Anomalies.Count) : UiText.Get("StatusQualityPassed");
                retryButton.Visibility = Visibility.Collapsed;
            }
            catch (OperationCanceledException)
            {
                statusText.Text = UiText.Get("StatusQualityCancelled");
            }
            catch (Exception exception)
            {
                statusText.Text = UiText.Format("StatusQualityFailed", exception.Message);
                retryButton.Visibility = Visibility.Visible;
            }
            finally
            {
                EndOperation();
            }
        }

        private void OnSegmentClick(object sender, RoutedEventArgs e)
        {
            if (_volume == null)
            {
                statusText.Text = UiText.Get("StatusLoadVolumeFirstSegmentation");
                return;
            }

            try
            {
                BitmapSource slice = _volume.GetAxialSlice(Math.Max(0, _volumeViewer.CurrentSliceIndex));
                _pendingSegmentation = SegmentationPipelineService.Segment(slice, new Rect(0, 0, slice.PixelWidth, slice.PixelHeight));
                segmentationText.Text = UiText.Format("StatusSegmentationCandidates", _pendingSegmentation.Blobs.Count);
                statusText.Text = UiText.Get("StatusSegmentationComplete");
            }
            catch (Exception exception)
            {
                _pendingSegmentation = null;
                statusText.Text = UiText.Format("StatusSegmentationFailed", exception.Message);
            }
            UpdateButtonStates();
        }

        private void SetMprMode(AdaptiveDisplayMode mode)
        {
            if (_volume == null)
            {
                statusText.Text = UiText.Get("StatusLoadVolumeFirstMpr");
                return;
            }

            try
            {
                VolumeSliceOrientation orientation = mode == AdaptiveDisplayMode.Coronal ? VolumeSliceOrientation.Coronal : VolumeSliceOrientation.Sagittal;
                UpdateMprSlice(mode, orientation, GetMprSliceIndex(mode));
                DisplayMode = mode;
            }
            catch (Exception exception)
            {
                statusText.Text = UiText.Format("StatusUnableCreateView", LocalizeMode(mode), exception.Message);
                retryButton.Visibility = Visibility.Visible;
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

        private void OnAcceptSegmentationClick(object sender, RoutedEventArgs e) =>
            statusText.Text = _pendingSegmentation == null ? UiText.Get("StatusNoSegmentationCandidate") : UiText.Get("StatusCandidateAccepted");

        private void OnRejectSegmentationClick(object sender, RoutedEventArgs e)
        {
            _pendingSegmentation = null;
            segmentationText.Text = UiText.Get("StatusCandidateRejected");
            statusText.Text = UiText.Get("StatusNoRoiChanged");
            UpdateButtonStates();
        }

        private void OnPreviewKeyDown(object sender, KeyEventArgs e)
        {
            switch (e.Key)
            {
                case Key.D1: DisplayMode = AdaptiveDisplayMode.AxialSlice; break;
                case Key.D2: SetMprMode(AdaptiveDisplayMode.Coronal); break;
                case Key.D3: SetMprMode(AdaptiveDisplayMode.Sagittal); break;
                case Key.D4: DisplayMode = AdaptiveDisplayMode.ThreeDimensional; break;
                case Key.Home: ResetActiveView(); break;
                case Key.F:
                    if (_volume != null)
                    {
                        _volume3DViewer.FitVolume();
                        statusText.Text = UiText.Get("StatusVolumeFitted");
                    }
                    break;
                case Key.Up: StepMprSlice(1); break;
                case Key.Down: StepMprSlice(-1); break;
                case Key.Escape: _operationCancellation?.Cancel(); break;
                default: return;
            }

            e.Handled = true;
        }

        internal void StepMprSlice(int offset)
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
            UpdateMprSlice(_displayMode, orientation, sliceIndex);
        }

        private int GetMprSliceIndex(AdaptiveDisplayMode mode) =>
            mode == AdaptiveDisplayMode.Coronal ? _coronalSliceIndex : _sagittalSliceIndex;

        private void UpdateMprSlice(AdaptiveDisplayMode mode, VolumeSliceOrientation orientation, int sliceIndex)
        {
            if (_volume == null)
            {
                return;
            }

            _imageViewer.ImageSource = VolumeSliceService.GetSlice(_volume, orientation, sliceIndex);
            if (mode == AdaptiveDisplayMode.Coronal)
            {
                _coronalSliceIndex = sliceIndex;
                _volume3DViewer.SetCoronalSlice(sliceIndex);
                statusText.Text = UiText.Format("StatusCoronalSlice", sliceIndex + 1, _volume.Height);
            }
            else
            {
                _sagittalSliceIndex = sliceIndex;
                _volume3DViewer.SetSagittalSlice(sliceIndex);
                statusText.Text = UiText.Format("StatusSagittalSlice", sliceIndex + 1, _volume.Width);
            }
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
            statusText.Text = UiText.Get("StatusCancelRequested");
            operationProgress.Visibility = Visibility.Collapsed;
            UpdateButtonStates();
        }

        private void BeginOperation(string message)
        {
            _operationCancellation?.Cancel();
            _operationCancellation?.Dispose();
            _operationCancellation = new CancellationTokenSource();
            statusText.Text = message;
            operationProgress.Visibility = Visibility.Visible;
            UpdateButtonStates();
        }

        private void EndOperation()
        {
            operationProgress.Visibility = Visibility.Collapsed;
            _operationCancellation?.Dispose();
            _operationCancellation = null;
            UpdateButtonStates();
        }

        private void UpdateStatus()
        {
            string data = _volume == null
                ? (_imageSource == null ? UiText.Get("StatusNoDataLoaded") : UiText.Get("StatusSingleImage"))
                : UiText.Format("StatusVolumeSummary", _volume.Width, _volume.Height, _volume.Depth, _volume.SpacingX, _volume.SpacingY, _volume.SpacingZ);
            statusText.Text = data;
            stateBarText.Text = UiText.Format("StatusModeFormat", LocalizeMode(ResolveMode()), data);
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
            acceptSegmentationButton.IsEnabled = _pendingSegmentation != null;
            rejectSegmentationButton.IsEnabled = _pendingSegmentation != null;
        }

        private AdaptiveDisplayMode ResolveMode()
        {
            return _displayMode == AdaptiveDisplayMode.Auto
                ? (_volume == null ? AdaptiveDisplayMode.TwoDimensional : AdaptiveDisplayMode.ThreeDimensional)
                : _displayMode;
        }
    }
}
