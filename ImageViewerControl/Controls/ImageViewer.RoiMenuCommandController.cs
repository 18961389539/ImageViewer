using System;
using System.Windows.Media;

namespace ImageViewer.Controls
{
    internal interface IImageViewerRoiMenuCommandHost
    {
        void Undo();

        void Redo();

        void DeleteSelected();

        void ClearAll();

        void EditSelectedProperties();

        void SetSelectedLabel();

        void SetSelectedColor(Color color);

        void CalibrateSelectedRoi();

        void EditSelectedCaliperSettings();

        void UpdateContextMenuState();
    }

    internal sealed class ImageViewerRoiMenuCommandController : ImageViewerMenuCommandControllerBase<IImageViewerRoiMenuCommandHost>
    {
        public ImageViewerRoiMenuCommandController(IImageViewerRoiMenuCommandHost host)
            : base(host, host.UpdateContextMenuState)
        {
        }

        public void Execute(ImageViewerRoiMenuCommand command)
        {
            switch (command)
            {
                case ImageViewerRoiMenuCommand.Undo:
                    Host.Undo();
                    break;
                case ImageViewerRoiMenuCommand.Redo:
                    Host.Redo();
                    break;
                case ImageViewerRoiMenuCommand.DeleteSelected:
                    Host.DeleteSelected();
                    break;
                case ImageViewerRoiMenuCommand.ClearAll:
                    Host.ClearAll();
                    break;
                case ImageViewerRoiMenuCommand.EditProperties:
                    Host.EditSelectedProperties();
                    break;
                case ImageViewerRoiMenuCommand.SetLabel:
                    Host.SetSelectedLabel();
                    break;
                case ImageViewerRoiMenuCommand.SetColorCyan:
                    Host.SetSelectedColor(Colors.Cyan);
                    break;
                case ImageViewerRoiMenuCommand.SetColorRed:
                    Host.SetSelectedColor(Colors.Red);
                    break;
                case ImageViewerRoiMenuCommand.SetColorGreen:
                    Host.SetSelectedColor(Colors.Green);
                    break;
                case ImageViewerRoiMenuCommand.SetColorYellow:
                    Host.SetSelectedColor(Colors.Yellow);
                    break;
                case ImageViewerRoiMenuCommand.SetColorMagenta:
                    Host.SetSelectedColor(Colors.Magenta);
                    break;
                case ImageViewerRoiMenuCommand.CalibratePixels:
                    Host.CalibrateSelectedRoi();
                    break;
                case ImageViewerRoiMenuCommand.EditCaliperSettings:
                    Host.EditSelectedCaliperSettings();
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(command), command, null);
            }

            RefreshMenuState();
        }
    }
}