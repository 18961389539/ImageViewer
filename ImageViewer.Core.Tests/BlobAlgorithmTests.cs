using System.Collections.Generic;
using ImageViewer.Core.Analysis;
using ImageViewer.Models;
using Xunit;

namespace ImageViewer.Core.Tests
{
    /// <summary>
    /// Direct unit tests for the managed blob analysis: threshold semantics (Otsu vs
    /// manual, light vs dark, Gray16 scaling), 8-connectivity, area filtering, ordering,
    /// and the computed region statistics.
    /// </summary>
    public class BlobAlgorithmTests
    {
        [Fact]
        public void Segment_ManualThreshold_BoundsAreInclusiveForLightAndDark()
        {
            // Values 10, 20, 30 with a threshold of 20.
            IntensityImage image = IntensityImageFactory.CreateGray8(3, 1, (x, _) => (byte)((x + 1) * 10));
            bool[] expectedLight = [false, true, true];
            bool[] expectedDark = [true, true, false];

            Assert.Equal(expectedLight, BlobAlgorithm.Segment(image, useOtsu: false, threshold: 20, detectDark: false));
            Assert.Equal(expectedDark, BlobAlgorithm.Segment(image, useOtsu: false, threshold: 20, detectDark: true));
        }

        [Fact]
        public void Segment_Otsu_LightTarget_IsExclusiveAtThreshold()
        {
            // Two levels: five pixels at 20 and five at 240. Otsu picks the boundary 20.
            IntensityImage image = IntensityImageFactory.CreateGray8(10, 1, (x, _) => x < 5 ? (byte)20 : (byte)240);

            bool[] otsu = BlobAlgorithm.Segment(image, useOtsu: true, threshold: 0, detectDark: false);
            bool[] manual = BlobAlgorithm.Segment(image, useOtsu: false, threshold: 20, detectDark: false);

            // Otsu selects g > T, so the level that equals T stays background.
            Assert.Equal(5, CountForeground(otsu));
            // A manual light threshold is inclusive (g >= T), so both levels qualify.
            Assert.Equal(10, CountForeground(manual));
        }

        [Fact]
        public void Segment_Gray16_ManualThresholdScalesTo16BitRange()
        {
            IntensityImage image = IntensityImageFactory.CreateGray16(
                3, 1, (x, _) => x switch { 0 => (ushort)25699, 1 => (ushort)25700, _ => (ushort)65535 });
            bool[] expected = [false, true, true];

            // 100 * 257 = 25700, and the light threshold includes the boundary value.
            Assert.Equal(expected, BlobAlgorithm.Segment(image, useOtsu: false, threshold: 100, detectDark: false));
        }

        [Fact]
        public void Connect_EightConnectivity_JoinsDiagonalAndSplitsDistantPixels()
        {
            IReadOnlyList<BlobFeature> joined = BlobAlgorithm.Connect(BuildMask(5, 5, (0, 0), (1, 1)), 5, 5, minArea: 1);
            BlobFeature region = Assert.Single(joined);
            Assert.Equal(2, region.Area);

            IReadOnlyList<BlobFeature> apart = BlobAlgorithm.Connect(BuildMask(5, 5, (0, 0), (2, 2)), 5, 5, minArea: 1);
            Assert.Equal(2, apart.Count);
        }

        [Fact]
        public void Connect_MinArea_DropsSmallComponents()
        {
            // A 2x2 block (area 4) plus a two-pixel pair (area 2).
            bool[] mask = BuildMask(6, 5, (0, 0), (1, 0), (0, 1), (1, 1), (4, 4), (5, 4));

            BlobFeature region = Assert.Single(BlobAlgorithm.Connect(mask, 6, 5, minArea: 3));
            Assert.Equal(4, region.Area);
        }

        [Fact]
        public void Connect_OrdersRegionsRowMajorByFirstPixel()
        {
            IReadOnlyList<BlobFeature> regions = BlobAlgorithm.Connect(BuildMask(6, 5, (2, 0), (0, 3)), 6, 5, minArea: 1);

            Assert.Equal(2, regions.Count);
            Assert.Equal(0, regions[0].BoundingBox.Y);
            Assert.Equal(3, regions[1].BoundingBox.Y);
        }

        [Fact]
        public void Connect_ComputesAreaCentroidAndBoundingBox()
        {
            bool[] mask = BuildMask(5, 4, (1, 1), (2, 1), (3, 1), (1, 2), (2, 2), (3, 2));

            BlobFeature region = Assert.Single(BlobAlgorithm.Connect(mask, 5, 4, minArea: 1));

            Assert.Equal(6, region.Area);
            Assert.Equal(2.0, region.Centroid.X, 6);
            Assert.Equal(1.5, region.Centroid.Y, 6);
            Assert.Equal(1.0, region.BoundingBox.X);
            Assert.Equal(1.0, region.BoundingBox.Y);
            Assert.Equal(3.0, region.BoundingBox.Width);
            Assert.Equal(2.0, region.BoundingBox.Height);
        }

        private static bool[] BuildMask(int width, int height, params (int X, int Y)[] points)
        {
            var mask = new bool[width * height];
            foreach ((int x, int y) in points)
            {
                mask[(y * width) + x] = true;
            }

            return mask;
        }

        private static int CountForeground(bool[] mask)
        {
            int count = 0;
            foreach (bool value in mask)
            {
                if (value)
                {
                    count++;
                }
            }

            return count;
        }
    }
}
