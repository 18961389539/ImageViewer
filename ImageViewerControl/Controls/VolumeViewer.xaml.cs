using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ImageViewer.Localization;
using ImageViewer.Models;
using ImageViewer.Services;

namespace ImageViewer.Controls
{
    public partial class VolumeViewer : UserControl, IDisposable, IAsyncDisposable
    {
        private VolumeData? _volume;
        private bool _isDisposed;

        public VolumeViewer()
        {
            InitializeComponent();
            sliceViewer = new ImageViewer();
            sliceViewerHost.Content = sliceViewer;
            UpdateSliceState();
        }

        private readonly ImageViewer sliceViewer;

        public VolumeData? Volume
        {
            get => _volume;
            set
            {
                ObjectDisposedException.ThrowIf(_isDisposed, this);
                _volume = value;
                sliceSlider.Maximum = Math.Max(0, (value?.Depth ?? 1) - 1);
                sliceSlider.Value = 0;
                UpdateSliceState();
            }
        }

        public int CurrentSliceIndex => _volume == null ? -1 : (int)sliceSlider.Value;

        public event EventHandler? CurrentSliceChanged;

        public ImageViewer SliceViewer => sliceViewer;

        public void SelectSlice(int sliceIndex)
        {
            ObjectDisposedException.ThrowIf(_isDisposed, this);
            if (_volume == null)
            {
                throw new InvalidOperationException("A volume must be assigned before selecting a slice.");
            }

            if ((uint)sliceIndex >= (uint)_volume.Depth)
            {
                throw new ArgumentOutOfRangeException(nameof(sliceIndex));
            }

            sliceSlider.Value = sliceIndex;
        }

        public void Dispose()
        {
            if (_isDisposed)
            {
                return;
            }

            _isDisposed = true;
            sliceViewer.Dispose();
            _volume = null;
            GC.SuppressFinalize(this);
        }

        public ValueTask DisposeAsync()
        {
            Dispose();
            GC.SuppressFinalize(this);
            return ValueTask.CompletedTask;
        }

        private void OnSliceValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (!_isDisposed)
            {
                UpdateSliceState();
                CurrentSliceChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        private void OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (_volume == null || e.Delta == 0)
            {
                return;
            }

            int step = Keyboard.Modifiers.HasFlag(ModifierKeys.Control) ? 5 : 1;
            int direction = e.Delta > 0 ? 1 : -1;
            int target = Math.Clamp(CurrentSliceIndex + direction * step, 0, _volume.Depth - 1);
            if (target != CurrentSliceIndex)
            {
                SelectSlice(target);
            }

            e.Handled = true;
        }

        private void UpdateSliceState()
        {
            if (_volume == null)
            {
                sliceViewer.ImageSource = null;
                sliceStatusText.Text = UiText.Get("StatusNoVolume");
                return;
            }

            int sliceIndex = CurrentSliceIndex;
            sliceViewer.SetImage(VolumeSliceService.GetSlice(_volume, VolumeSliceOrientation.Axial, sliceIndex));
            sliceStatusText.Text = UiText.Format("StatusAxialSlice", sliceIndex + 1, _volume.Depth);
        }
    }
}
