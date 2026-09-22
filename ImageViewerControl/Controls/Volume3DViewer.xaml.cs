using System;
using System.Numerics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using HelixToolkit.Geometry;
using HelixToolkit.Maths;
using HelixToolkit.SharpDX;
using HelixToolkit.Wpf.SharpDX;
using ImageViewer.Models;
using ImageViewer.Services;

namespace ImageViewer.Controls
{
    public partial class Volume3DViewer : UserControl, IDisposable, IAsyncDisposable
    {
        private VolumeData? _volume;
        private MeshGeometryModel3D? _volumeBoundsModel;
        private MeshGeometryModel3D? _axialPlaneModel;
        private MeshGeometryModel3D? _coronalPlaneModel;
        private MeshGeometryModel3D? _sagittalPlaneModel;
        private MeshGeometryModel3D? _cropBoundsModel;
        private VolumeCropBounds? _cropBounds;
        private double _volumeOpacity = 0.22;
        private bool _showBoundingBox = true;
        private bool _showAxialPlane = true;
        private bool _showCoronalPlane;
        private bool _showSagittalPlane;
        private bool _isDisposed;

        public event EventHandler? SwitchToAxialSliceRequested;
        public event EventHandler<VolumeVoxelPickedEventArgs>? VoxelPicked;

        public Volume3DViewer()
        {
            InitializeComponent();
                viewport.Camera = new HelixToolkit.Wpf.SharpDX.PerspectiveCamera
            {
                Position = new Point3D(0, 0, 3),
                LookDirection = new Vector3D(0, 0, -3),
                UpDirection = new Vector3D(0, 1, 0),
                FieldOfView = 45
            };
            viewport.EffectsManager = new DefaultEffectsManager();
            viewport.Items.Add(new DirectionalLight3D { Direction = new Vector3D(-1, -1, -1) });
            volumeStatusText.Text = "No volume";
        }

        public VolumeData? Volume
        {
            get => _volume;
            set
            {
                ObjectDisposedException.ThrowIf(_isDisposed, this);
                _volume = value;
                RebuildScene();
            }
        }

        public int CurrentSliceIndex { get; private set; } = -1;
        public int CurrentCoronalSliceIndex { get; private set; } = -1;
        public int CurrentSagittalSliceIndex { get; private set; } = -1;

        public VolumeCropBounds? CropBounds
        {
            get => _cropBounds;
            set
            {
                ObjectDisposedException.ThrowIf(_isDisposed, this);
                if (_volume == null)
                {
                    _cropBounds = null;
                    RemoveCropBoundsModel();
                    return;
                }

                _cropBounds = VolumeInteractionService.NormalizeCrop(_volume, value);
                UpdateCropBoundsModel();
            }
        }

        public void SetCurrentSlice(int sliceIndex)
        {
            ObjectDisposedException.ThrowIf(_isDisposed, this);
            if (_volume == null)
            {
                CurrentSliceIndex = -1;
                return;
            }

            CurrentSliceIndex = Math.Clamp(sliceIndex, 0, _volume.Depth - 1);
            UpdatePlanes();
        }

        public void SetCoronalSlice(int sliceIndex)
        {
            ObjectDisposedException.ThrowIf(_isDisposed, this);
            if (_volume == null)
            {
                CurrentCoronalSliceIndex = -1;
                return;
            }

            CurrentCoronalSliceIndex = Math.Clamp(sliceIndex, 0, _volume.Height - 1);
            UpdatePlanes();
        }

        public void SetSagittalSlice(int sliceIndex)
        {
            ObjectDisposedException.ThrowIf(_isDisposed, this);
            if (_volume == null)
            {
                CurrentSagittalSliceIndex = -1;
                return;
            }

            CurrentSagittalSliceIndex = Math.Clamp(sliceIndex, 0, _volume.Width - 1);
            UpdatePlanes();
        }

        public Viewport3DX SceneViewport => viewport;

        public bool TryPickVoxelAtWorldPoint(Point3D point)
        {
            ObjectDisposedException.ThrowIf(_isDisposed, this);
            if (_volume == null || !VolumeInteractionService.TryReadVoxelAtWorldPoint(_volume, point, out VolumeVoxelLocation? voxel) || voxel == null)
            {
                return false;
            }

            VoxelPicked?.Invoke(this, new VolumeVoxelPickedEventArgs(voxel));
            return true;
        }

        public bool ShowCoordinateSystem
        {
            get => viewport.ShowCoordinateSystem;
            set => viewport.ShowCoordinateSystem = value;
        }

        public bool ShowBoundingBox
        {
            get => _showBoundingBox;
            set
            {
                _showBoundingBox = value;
                if (_volumeBoundsModel != null)
                {
                    _volumeBoundsModel.Visibility = value ? Visibility.Visible : Visibility.Hidden;
                }
            }
        }

        public double VolumeOpacity
        {
            get => _volumeOpacity;
            set
            {
                _volumeOpacity = Math.Clamp(value, 0.05, 0.9);
                if (_volumeBoundsModel?.Material is PhongMaterial material)
                {
                    material.DiffuseColor = new Color4(0.18f, 0.55f, 0.82f, (float)_volumeOpacity);
                }
            }
        }

        public bool ShowAxialPlane
        {
            get => _showAxialPlane;
            set
            {
                _showAxialPlane = value;
                UpdatePlaneVisibility();
            }
        }

        public bool ShowCoronalPlane
        {
            get => _showCoronalPlane;
            set
            {
                _showCoronalPlane = value;
                UpdatePlaneVisibility();
            }
        }

        public bool ShowSagittalPlane
        {
            get => _showSagittalPlane;
            set
            {
                _showSagittalPlane = value;
                UpdatePlaneVisibility();
            }
        }

        public void ResetCamera()
        {
            ObjectDisposedException.ThrowIf(_isDisposed, this);
            SetCamera(new Point3D(0, 0, 3), new Vector3D(0, 0, -3), new Vector3D(0, 1, 0));
        }

        public void FitVolume() => SetVolumeCamera(new Vector3D(1.6, 1.4, 1.8));

        public void ShowFrontView() => SetVolumeCamera(new Vector3D(0, 0, 1));

        public void ShowBackView() => SetVolumeCamera(new Vector3D(0, 0, -1));

        public void ShowTopView() => SetVolumeCamera(new Vector3D(0, 1, 0));

        public void ShowBottomView() => SetVolumeCamera(new Vector3D(0, -1, 0));

        public void ShowSideView() => SetVolumeCamera(new Vector3D(1, 0, 0));

        public void ShowOppositeSideView() => SetVolumeCamera(new Vector3D(-1, 0, 0));

        public void ShowIsometricView() => SetVolumeCamera(new Vector3D(1, 1, 1));

        public void Dispose()
        {
            if (_isDisposed)
            {
                return;
            }

            _isDisposed = true;
            viewport.Items.Clear();
            viewport.EffectsManager?.Dispose();
            viewport.EffectsManager = null;
            _volumeBoundsModel = null;
            _axialPlaneModel = null;
            _coronalPlaneModel = null;
            _sagittalPlaneModel = null;
            _volume = null;
            GC.SuppressFinalize(this);
        }

        public ValueTask DisposeAsync()
        {
            Dispose();
            GC.SuppressFinalize(this);
            return ValueTask.CompletedTask;
        }

        private void RebuildScene()
        {
            viewport.Items.Clear();
            _volumeBoundsModel = null;
            _axialPlaneModel = null;
            _coronalPlaneModel = null;
            _sagittalPlaneModel = null;
            _cropBoundsModel = null;
            CropBounds = null;
            if (_volume == null)
            {
                volumeStatusText.Text = "No volume";
                return;
            }

            double width = _volume.Width * _volume.SpacingX;
            double height = _volume.Height * _volume.SpacingY;
            double depth = _volume.Depth * _volume.SpacingZ;
            MeshBuilder meshBuilder = new();
            meshBuilder.AddBox(
                new Vector3(0, 0, 0),
                (float)width,
                (float)height,
                (float)depth);

            _volumeBoundsModel = new MeshGeometryModel3D
            {
                Geometry = meshBuilder.ToMeshGeometry3D(),
                Material = new PhongMaterial
                {
                    DiffuseColor = new Color4(0.18f, 0.55f, 0.82f, (float)_volumeOpacity),
                    AmbientColor = new Color4(0.08f, 0.18f, 0.28f, 1),
                    ReflectiveColor = new Color4(0, 0, 0, 1),
                    SpecularColor = new Color4(0.2f, 0.2f, 0.2f, 1),
                    SpecularShininess = 16
                },
                IsTransparent = true
            };
            _volumeBoundsModel.Visibility = _showBoundingBox ? Visibility.Visible : Visibility.Hidden;
            viewport.Items.Add(_volumeBoundsModel);
            CurrentSliceIndex = 0;
            CurrentCoronalSliceIndex = _volume.Height / 2;
            CurrentSagittalSliceIndex = _volume.Width / 2;
            UpdatePlanes();
            UpdateCropBoundsModel();
            viewport.Camera = new HelixToolkit.Wpf.SharpDX.PerspectiveCamera
            {
                Position = new Point3D(width * 1.6, height * 1.4, depth * 1.8),
                LookDirection = new Vector3D(-width * 1.6, -height * 1.4, -depth * 1.8),
                UpDirection = new Vector3D(0, 1, 0),
                FieldOfView = 45
            };
            volumeStatusText.Text = $"3D volume | {_volume.Width} x {_volume.Height} x {_volume.Depth} | spacing {_volume.SpacingX:0.###} x {_volume.SpacingY:0.###} x {_volume.SpacingZ:0.###} mm | axes X/Y/Z";
        }

        private void OnResetCameraClick(object sender, RoutedEventArgs e) => ResetCamera();
        private void OnFitVolumeClick(object sender, RoutedEventArgs e) => FitVolume();
        private void OnFrontViewClick(object sender, RoutedEventArgs e) => ShowFrontView();
        private void OnBackViewClick(object sender, RoutedEventArgs e) => ShowBackView();
        private void OnTopViewClick(object sender, RoutedEventArgs e) => ShowTopView();
        private void OnBottomViewClick(object sender, RoutedEventArgs e) => ShowBottomView();
        private void OnSideViewClick(object sender, RoutedEventArgs e) => ShowSideView();
        private void OnOppositeSideViewClick(object sender, RoutedEventArgs e) => ShowOppositeSideView();
        private void OnIsometricViewClick(object sender, RoutedEventArgs e) => ShowIsometricView();

        private void OnCoordinateSystemClick(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem menuItem)
            {
                ShowCoordinateSystem = menuItem.IsChecked;
            }
        }

        private void OnBoundingBoxClick(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem menuItem)
            {
                ShowBoundingBox = menuItem.IsChecked;
            }
        }

        private void OnAxialPlaneClick(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem menuItem)
            {
                ShowAxialPlane = menuItem.IsChecked;
            }
        }

        private void OnCoronalPlaneClick(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem menuItem)
            {
                ShowCoronalPlane = menuItem.IsChecked;
            }
        }

        private void OnSagittalPlaneClick(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem menuItem)
            {
                ShowSagittalPlane = menuItem.IsChecked;
            }
        }

        private void OnIncreaseTransparencyClick(object sender, RoutedEventArgs e) => VolumeOpacity -= 0.05;

        private void OnDecreaseTransparencyClick(object sender, RoutedEventArgs e) => VolumeOpacity += 0.05;

        private void OnSwitchToAxialSliceClick(object sender, RoutedEventArgs e) => SwitchToAxialSliceRequested?.Invoke(this, EventArgs.Empty);

        private void SetVolumeCamera(Vector3D direction)
        {
            ObjectDisposedException.ThrowIf(_isDisposed, this);
            if (_volume == null)
            {
                ResetCamera();
                return;
            }

            double width = _volume.Width * _volume.SpacingX;
            double height = _volume.Height * _volume.SpacingY;
            double depth = _volume.Depth * _volume.SpacingZ;
            double distance = Math.Max(width, Math.Max(height, depth)) * 2.4;
            Point3D position = new(direction.X * distance, direction.Y * distance, direction.Z * distance);
            Vector3D up = Math.Abs(direction.Y) > 0.9 ? new Vector3D(0, 0, -1) : new Vector3D(0, 1, 0);
            SetCamera(position, new Vector3D(-position.X, -position.Y, -position.Z), up);
        }

        private void SetCamera(Point3D position, Vector3D lookDirection, Vector3D upDirection)
        {
            viewport.Camera = new HelixToolkit.Wpf.SharpDX.PerspectiveCamera
            {
                Position = position,
                LookDirection = lookDirection,
                UpDirection = upDirection,
                FieldOfView = 45
            };
        }

        private void UpdatePlanes()
        {
            if (_volume == null || CurrentSliceIndex < 0)
            {
                return;
            }

            double width = _volume.Width * _volume.SpacingX;
            double height = _volume.Height * _volume.SpacingY;
            double depth = _volume.Depth * _volume.SpacingZ;
            double axialZ = -depth / 2 + (CurrentSliceIndex + 0.5) * _volume.SpacingZ;
            double coronalY = -height / 2 + (CurrentCoronalSliceIndex + 0.5) * _volume.SpacingY;
            double sagittalX = -width / 2 + (CurrentSagittalSliceIndex + 0.5) * _volume.SpacingX;

            _axialPlaneModel = ReplacePlane(_axialPlaneModel, new Vector3(0, 0, (float)axialZ), (float)width, (float)height, 0.01f, new Color4(0.95f, 0.72f, 0.2f, 0.35f));
            _coronalPlaneModel = ReplacePlane(_coronalPlaneModel, new Vector3(0, (float)coronalY, 0), (float)width, 0.01f, (float)depth, new Color4(0.2f, 0.85f, 0.55f, 0.3f));
            _sagittalPlaneModel = ReplacePlane(_sagittalPlaneModel, new Vector3((float)sagittalX, 0, 0), 0.01f, (float)height, (float)depth, new Color4(0.95f, 0.35f, 0.35f, 0.3f));
            UpdatePlaneVisibility();
        }

        private void UpdateCropBoundsModel()
        {
            RemoveCropBoundsModel();
            if (_volume == null || CropBounds == null)
            {
                return;
            }

            VolumeCropBounds bounds = CropBounds;
            VolumeCropBounds full = VolumeCropBounds.Full(_volume);
            if (bounds == full)
            {
                return;
            }

            double minX = bounds.MinimumX * _volume.SpacingX;
            double maxX = (bounds.MaximumX + 1) * _volume.SpacingX;
            double minY = bounds.MinimumY * _volume.SpacingY;
            double maxY = (bounds.MaximumY + 1) * _volume.SpacingY;
            double minZ = bounds.MinimumZ * _volume.SpacingZ;
            double maxZ = (bounds.MaximumZ + 1) * _volume.SpacingZ;
            MeshBuilder meshBuilder = new();
            meshBuilder.AddBox(
                new Vector3(
                    (float)((minX + maxX) / 2 - _volume.Width * _volume.SpacingX / 2),
                    (float)((minY + maxY) / 2 - _volume.Height * _volume.SpacingY / 2),
                    (float)((minZ + maxZ) / 2 - _volume.Depth * _volume.SpacingZ / 2)),
                (float)(maxX - minX),
                (float)(maxY - minY),
                (float)(maxZ - minZ));
            _cropBoundsModel = new MeshGeometryModel3D
            {
                Geometry = meshBuilder.ToMeshGeometry3D(),
                Material = new PhongMaterial
                {
                    DiffuseColor = new Color4(0.95f, 0.8f, 0.2f, 0.12f),
                    AmbientColor = new Color4(0.3f, 0.25f, 0.05f, 1),
                    ReflectiveColor = new Color4(0, 0, 0, 1),
                    SpecularColor = new Color4(0.2f, 0.2f, 0.2f, 1),
                    SpecularShininess = 4
                },
                IsTransparent = true
            };
            viewport.Items.Add(_cropBoundsModel);
        }

        private void RemoveCropBoundsModel()
        {
            if (_cropBoundsModel == null)
            {
                return;
            }

            viewport.Items.Remove(_cropBoundsModel);
            _cropBoundsModel = null;
        }

        private MeshGeometryModel3D ReplacePlane(MeshGeometryModel3D? previous, Vector3 center, float planeWidth, float planeHeight, float planeDepth, Color4 color)
        {
            if (previous != null)
            {
                viewport.Items.Remove(previous);
            }

            MeshBuilder meshBuilder = new();
            meshBuilder.AddBox(center, planeWidth, planeHeight, planeDepth);
            MeshGeometryModel3D model = new()
            {
                Geometry = meshBuilder.ToMeshGeometry3D(),
                Material = new PhongMaterial
                {
                    DiffuseColor = color,
                    AmbientColor = new Color4(color.Red * 0.4f, color.Green * 0.4f, color.Blue * 0.4f, 1),
                    ReflectiveColor = new Color4(0, 0, 0, 1),
                    SpecularColor = new Color4(0.3f, 0.3f, 0.3f, 1),
                    SpecularShininess = 8
                },
                IsTransparent = true
            };
            viewport.Items.Add(model);
            return model;
        }

        private void UpdatePlaneVisibility()
        {
            if (_axialPlaneModel != null)
            {
                _axialPlaneModel.Visibility = _showAxialPlane ? Visibility.Visible : Visibility.Hidden;
            }
            if (_coronalPlaneModel != null)
            {
                _coronalPlaneModel.Visibility = _showCoronalPlane ? Visibility.Visible : Visibility.Hidden;
            }
            if (_sagittalPlaneModel != null)
            {
                _sagittalPlaneModel.Visibility = _showSagittalPlane ? Visibility.Visible : Visibility.Hidden;
            }
        }
    }
}
