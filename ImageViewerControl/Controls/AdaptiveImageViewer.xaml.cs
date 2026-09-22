using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Input;
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
                statusText.Text = $"Axial slice {_volumeViewer.CurrentSliceIndex + 1}/{_volume.Depth}";
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
                statusText.Text = "Load a volume before quality analysis.";
                return;
            }

            BeginOperation("Analyzing volume quality...");
            try
            {
                VolumeQualityReport report = await Task.Run(() => VolumeQualityAnalyzer.Analyze(_volume), _operationCancellation!.Token);
                _qualityReport = report;
                anomalyList.Items.Clear();
                foreach (VolumeAnomaly anomaly in report.Anomalies)
                {
                    anomalyList.Items.Add($"Slice {anomaly.SliceIndex + 1}: {anomaly.Message}");
                }

                statusText.Text = report.HasAnomalies ? $"Quality analysis found {report.Anomalies.Count} issue(s)." : "Quality analysis passed.";
                retryButton.Visibility = Visibility.Collapsed;
            }
            catch (OperationCanceledException)
            {
                statusText.Text = "Quality analysis cancelled.";
            }
            catch (Exception exception)
            {
                statusText.Text = $"Quality analysis failed: {exception.Message}";
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
                statusText.Text = "Load a volume before segmentation.";
                return;
            }

            try
            {
                BitmapSource slice = _volume.GetAxialSlice(Math.Max(0, _volumeViewer.CurrentSliceIndex));
                _pendingSegmentation = SegmentationPipelineService.Segment(slice, new Rect(0, 0, slice.PixelWidth, slice.PixelHeight));
                segmentationText.Text = $"Segmentation: {_pendingSegmentation.Blobs.Count} candidate region(s). Review before accepting.";
                statusText.Text = "Segmentation complete; no ROI was changed.";
            }
            catch (Exception exception)
            {
                _pendingSegmentation = null;
                statusText.Text = $"Segmentation failed: {exception.Message}";
            }
            UpdateButtonStates();
        }

        private void SetMprMode(AdaptiveDisplayMode mode)
        {
            if (_volume == null)
            {
                statusText.Text = "Load a volume before selecting an MPR direction.";
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
                statusText.Text = $"Unable to create {mode} view: {exception.Message}";
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
            statusText.Text = $"Located anomaly on slice {anomaly.SliceIndex + 1}.";
        }

        private void OnAcceptSegmentationClick(object sender, RoutedEventArgs e) =>
            statusText.Text = _pendingSegmentation == null ? "No segmentation candidate to accept." : "Candidate accepted for review; ROI data remains unchanged.";

        private void OnRejectSegmentationClick(object sender, RoutedEventArgs e)
        {
            _pendingSegmentation = null;
            segmentationText.Text = "Segmentation candidate rejected.";
            statusText.Text = "No ROI was changed.";
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
                        statusText.Text = "3D volume fitted to view.";
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
                statusText.Text = $"Coronal slice {sliceIndex + 1}/{_volume.Height}";
            }
            else
            {
                _sagittalSliceIndex = sliceIndex;
                _volume3DViewer.SetSagittalSlice(sliceIndex);
                statusText.Text = $"Sagittal slice {sliceIndex + 1}/{_volume.Width}";
            }
        }

        private void ResetActiveView()
        {
            if (ResolveMode() == AdaptiveDisplayMode.ThreeDimensional)
            {
                _volume3DViewer.ResetCamera();
                statusText.Text = "3D camera reset.";
            }
            else if (_volume == null)
            {
                statusText.Text = "2D view reset.";
            }
            else
            {
                _volumeViewer.SelectSlice(0);
                statusText.Text = "2D view reset to the first slice.";
            }
        }

        private void OnRetryQualityClick(object sender, RoutedEventArgs e) => OnQualityClick(sender, e);

        private void OnCancelClick(object sender, RoutedEventArgs e)
        {
            _operationCancellation?.Cancel();
            statusText.Text = "Operation cancellation requested.";
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
            string data = _volume == null ? (_imageSource == null ? "No data loaded" : "Single image") : $"Volume {_volume.Width} x {_volume.Height} x {_volume.Depth}, spacing {_volume.SpacingX:0.###} x {_volume.SpacingY:0.###} x {_volume.SpacingZ:0.###} mm";
            statusText.Text = data;
            stateBarText.Text = $"Mode: {ResolveMode()}  |  {data}";
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
