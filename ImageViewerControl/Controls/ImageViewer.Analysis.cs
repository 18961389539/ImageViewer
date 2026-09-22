using System.ComponentModel;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ImageViewer.Localization;
using ImageViewer.Models;
using ImageViewer.Services;

namespace ImageViewer.Controls
{
    public partial class ImageViewer
    {
        private void OnRuntimeOptionsPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            switch (e.PropertyName)
            {
                case nameof(ImageViewerRuntimeOptions.EnableImagePyramid):
                    RebuildPyramidIfNeeded();
                    break;
                case nameof(ImageViewerRuntimeOptions.AutoSelectPyramidLevel):
                case nameof(ImageViewerRuntimeOptions.EnableTiledRendering):
                case nameof(ImageViewerRuntimeOptions.PrefetchAdjacentTiles):
                    UpdateRenderedImage();
                    break;
                case nameof(ImageViewerRuntimeOptions.EnableAsyncAnalysis):
                    _analysisController.HandleAsyncAnalysisChanged();
                    break;
                case nameof(ImageViewerRuntimeOptions.PauseRealtimeHistogram):
                    _analysisController.HandleRealtimeHistogramPauseChanged();
                    break;
                case nameof(ImageViewerRuntimeOptions.PauseRealtimeProfile):
                    _analysisController.HandleRealtimeProfilePauseChanged();
                    break;
                case nameof(ImageViewerRuntimeOptions.PreferShaderPseudoColor):
                case nameof(ImageViewerRuntimeOptions.AllowCpuPseudoColorFallback):
                    _analysisController.HandleRenderingOptionChanged();
                    break;
            }

            UpdateContextMenuState();
        }

        private void UpdateRenderedImage()
        {
            if (!IsLoaded)
            {
                return;
            }

            _analysisController.UpdateRenderedImage();
        }

        private BitmapSource? GetAnalysisBitmapSource() => _analysisController.GetAnalysisBitmapSource();

        private Task PrepareAnalysisResourcesAsync(ImageSource? source) => _analysisController.PrepareAnalysisResourcesAsync(source);

        private void RefreshAnalysisDisplays(bool force = false) => _ = BackgroundOperationObserver.ObserveAsync(_analysisController.RefreshAnalysisDisplays(force), "Refresh analysis displays");

        private void RebuildPyramidIfNeeded() => _analysisController.RebuildPyramidIfNeeded();

        private void ClearAnalysisCaches() => _analysisController.ClearAnalysisCaches();

        private string BuildRenderStatusSummary() => _analysisController.BuildRenderStatusSummary();

        private void ShowSmartDisplaySuggestion(ImageViewerDialogWorkflowService dialogWorkflowService)
        {
            ArgumentNullException.ThrowIfNull(dialogWorkflowService);

            BitmapSource? bitmap = GetAnalysisBitmapSource();
            if (bitmap == null)
            {
                return;
            }

            DisplaySettingsSuggestion suggestion = DisplaySettingsSuggestionService.Suggest(bitmap);
            string pseudoColorRecommendation = suggestion.UsePseudoColor
                ? UiText.Get("SmartDisplaySuggestionPseudoColorRecommended")
                : UiText.Get("SmartDisplaySuggestionPseudoColorNotNeeded");
            string summary = UiText.Format(
                "SmartDisplaySuggestionSummary",
                suggestion.Contrast,
                suggestion.Brightness,
                pseudoColorRecommendation,
                suggestion.Reason);
            dialogWorkflowService.ShowReadOnlyText(UiText.Get("DialogSmartDisplaySuggestionTitle"), summary);
        }

        private void UpdatePseudoColorMenuState() => _analysisController.UpdatePseudoColorMenuState();

        private static void OnEnableGpuRenderingChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is ImageViewer viewer)
            {
                viewer.UpdateRenderedImage();
            }
        }

        private static void OnPseudoColorPaletteChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is ImageViewer viewer)
            {
                viewer._analysisController.HandlePseudoColorPaletteChanged();
            }
        }

        private static void OnShowHistogramChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is ImageViewer viewer)
            {
                viewer._analysisController.HandleHistogramVisibilityChanged((bool)e.NewValue);
            }
        }

        private static void OnShowProfileChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is ImageViewer viewer)
            {
                viewer._analysisController.HandleProfileVisibilityChanged((bool)e.NewValue);
            }
        }
    }
}