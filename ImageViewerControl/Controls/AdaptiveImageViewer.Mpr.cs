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
    public partial class AdaptiveImageViewer : UserControl, IDisposable, IAsyncDisposable
    {
        private CancellationTokenSource? _mprCancellation;
        private long _mprRequestGeneration;
        private int _coronalSliceIndex;
        private int _sagittalSliceIndex;
        private bool _isUpdatingMprSlider;

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


        private bool IsMprModeActive => _volume != null &&
            (_displayMode == AdaptiveDisplayMode.Coronal || _displayMode == AdaptiveDisplayMode.Sagittal);

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
                // 与轴位体数据保持一致：普通滚轮切片，Shift 快速跳层，Ctrl+滚轮
                // 交给嵌套 ImageViewer 执行缩放。
                if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
                {
                    return;
                }

                int step = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? 5 : 1;
                await StepMprSliceAsync((e.Delta > 0 ? 1 : -1) * step);
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
    }
}
