using System;
using System.Linq;
using ImageViewer.Controls;
using Xunit;

namespace ImageViewerControl.Tests
{
    public class ImageViewerHostDecouplingTests
    {
        [Fact]
        public void ImageViewer_NoLongerImplementsWorkflowCommandAndPersistenceHostInterfaces()
        {
            Type[] interfaces = typeof(ImageViewer.Controls.ImageViewer).GetInterfaces();

            Assert.DoesNotContain(typeof(IImageViewerFeatureMenuCommandHost), interfaces);
            Assert.DoesNotContain(typeof(IImageViewerViewCommandHost), interfaces);
            Assert.DoesNotContain(typeof(IImageViewerModeCommandHost), interfaces);
            Assert.DoesNotContain(typeof(IImageViewerAnalysisCommandHost), interfaces);
            Assert.DoesNotContain(typeof(IImageViewerRoiMenuCommandHost), interfaces);
            Assert.DoesNotContain(typeof(IImageViewerFileMenuCommandHost), interfaces);
            Assert.DoesNotContain(typeof(IImageViewerRoiPersistenceControllerHost), interfaces);
        }
    }
}