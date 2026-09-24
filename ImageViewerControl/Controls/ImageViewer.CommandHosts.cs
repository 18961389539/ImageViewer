using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Threading;
using ImageViewer.Abstractions;
using ImageViewer.Models;
using ImageViewer.Plugins;
using ImageViewer.Services;

namespace ImageViewer.Controls
{
    internal sealed class ImageViewerViewCommandDependencies
    {
        public required Func<bool> GetShowPixelGrid { get; init; }
        public required Action<bool> SetShowPixelGrid { get; init; }
        public required Func<bool> GetShowCrosshair { get; init; }
        public required Action<bool> SetShowCrosshair { get; init; }
        public required Func<bool> GetShowCaliperScores { get; init; }
        public required Action<bool> SetShowCaliperScores { get; init; }
        public required Func<bool> GetShowInfoPanel { get; init; }
        public required Action<bool> SetShowInfoPanel { get; init; }
        public required Func<bool> GetShowHistogram { get; init; }
        public required Action<bool> SetShowHistogram { get; init; }
        public required Func<bool> GetShowProfile { get; init; }
        public required Action<bool> SetShowProfile { get; init; }
        public required Func<bool> GetShowScaleBar { get; init; }
        public required Action<bool> SetShowScaleBar { get; init; }
        public required Func<bool> GetShowRoiList { get; init; }
        public required Action<bool> SetShowRoiList { get; init; }
        public required Func<bool> GetShowToolbar { get; init; }
        public required Action<bool> SetShowToolbar { get; init; }
        public required Func<bool> GetShowSnapGrid { get; init; }
        public required Action<bool> SetShowSnapGrid { get; init; }
        public required Func<bool> GetEnableSnapToGrid { get; init; }
        public required Action<bool> SetEnableSnapToGrid { get; init; }
        public required Action FitToView { get; init; }
        public required Action ResetView { get; init; }
        public required Action ShowFullImage { get; init; }
        public required Action SetActualSize { get; init; }
        public required Action ZoomIn { get; init; }
        public required Action ZoomOut { get; init; }
        public required Action ZoomToSelection { get; init; }
        public required Action RotateLeft { get; init; }
        public required Action RotateRight { get; init; }
        public required Action FlipHorizontal { get; init; }
        public required Action FlipVertical { get; init; }
    }

    internal sealed class ImageViewerModeCommandDependencies
    {
        public required Action StartRectangleMode { get; init; }
        public required Action StartEllipseMode { get; init; }
        public required Action StartCircleMode { get; init; }
        public required Action StartPolygonMode { get; init; }
        public required Action StartPolylineMode { get; init; }
        public required Action StartFreehandMode { get; init; }
        public required Action StartPointAnnotationMode { get; init; }
        public required Action StartTextAnnotationMode { get; init; }
        public required Action StartLineMeasureMode { get; init; }
        public required Action StartAngleMeasureMode { get; init; }
    }

    internal sealed class ImageViewerAnalysisCommandDependencies
    {
        /// <summary>
        /// 分析相关开关的唯一状态源。
        /// Chinese: 开关集中存放在控件的 ImageViewerRuntimeOptions 上，不再逐项透传 Get/Set 委托，
        /// 避免同一批开关出现"选项对象 + 控件门面 + 委托"三份定义。
        /// English: Single source of truth for the analysis switches; per-option get/set delegate pairs were removed.
        /// </summary>
        public required ImageViewerRuntimeOptions RuntimeOptions { get; init; }

        /// <summary>
        /// 启用 GPU 渲染开关。
        /// Chinese: 该开关是控件依赖属性（可绑定，且变更需触发重绘），因此仍以委托方式桥接。
        /// English: Kept as a delegated bridge because it stays a bindable dependency property with a change side effect.
        /// </summary>
        public required Func<bool> GetEnableGpuRendering { get; init; }

        public required Action<bool> SetEnableGpuRendering { get; init; }

        public required Action UpdateRenderedImage { get; init; }
        public required Action RefreshAnalysis { get; init; }
        public required Action ClearAnalysisCache { get; init; }
        public required Action ResetPyramidToBaseLevel { get; init; }
        public required Action RebuildPyramidIfNeeded { get; init; }
        public required Action<PseudoColorPalette> SetPseudoColorPalette { get; init; }
        public required Action ShowSmartDisplaySuggestion { get; init; }
        public required Action ShowRenderStatus { get; init; }
    }

    internal interface IImageViewerViewCommandHost
    {
        bool ShowPixelGrid { get; set; }
        bool ShowCrosshair { get; set; }
        bool ShowCaliperScores { get; set; }
        bool ShowInfoPanel { get; set; }
        bool ShowHistogram { get; set; }
        bool ShowProfile { get; set; }
        bool ShowScaleBar { get; set; }
        bool ShowRoiList { get; set; }
        bool ShowToolbar { get; set; }
        bool ShowSnapGrid { get; set; }
        bool EnableSnapToGrid { get; set; }

        void FitToView();
        void ResetView();
        void ShowFullImage();
        void SetActualSize();
        void ZoomIn();
        void ZoomOut();
        void ZoomToSelection();
        void RotateLeft();
        void RotateRight();
        void FlipHorizontal();
        void FlipVertical();
    }

    internal interface IImageViewerModeCommandHost
    {
        void StartRectangleMode();
        void StartEllipseMode();
        void StartCircleMode();
        void StartPolygonMode();
        void StartPolylineMode();
        void StartFreehandMode();
        void StartPointAnnotationMode();
        void StartTextAnnotationMode();
        void StartLineMeasureMode();
        void StartAngleMeasureMode();
    }

    internal interface IImageViewerAnalysisCommandHost
    {
        bool EnableAsyncAnalysis { get; set; }
        bool PauseRealtimeHistogram { get; set; }
        bool PauseRealtimeProfile { get; set; }
        bool EnableImagePyramid { get; set; }
        bool AutoSelectPyramidLevel { get; set; }
        bool EnableTiledRendering { get; set; }
        bool PrefetchAdjacentTiles { get; set; }
        bool EnableGpuRendering { get; set; }
        bool PreferShaderPseudoColor { get; set; }
        bool AllowCpuPseudoColorFallback { get; set; }

        void UpdateRenderedImage();
        void RefreshAnalysis();
        void ClearAnalysisCache();
        void ResetPyramidToBaseLevel();
        void RebuildPyramidIfNeeded();
        void SetPseudoColorPalette(PseudoColorPalette palette);
        void ShowSmartDisplaySuggestion();
        void ShowRenderStatus();
    }

}