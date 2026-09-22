using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using ImageViewer.Models;
using ImageViewer.Plugins;
using ImageViewer.Services;
using Xunit;

namespace ImageViewerControl.Tests
{
    public class RoiPersistenceServiceTests
    {
        private static readonly RoiPersistenceServiceRoundTripExpectation[] SerializationExpectations =
        [
            new(
                new BlobAnalysisRoi
                {
                    Center = new Point(10, 12),
                    Width = 14,
                    Height = 16,
                    Angle = 18,
                    UseOtsu = true,
                    ManualThreshold = 90,
                    DetectDark = true,
                    MinArea = 3
                },
                roi =>
                {
                    var roundTripped = Assert.IsType<BlobAnalysisRoi>(roi);
                    Assert.Equal(new Point(10, 12), roundTripped.Center);
                    Assert.Equal(14, roundTripped.Width);
                    Assert.Equal(16, roundTripped.Height);
                    Assert.Equal(18, roundTripped.Angle);
                    Assert.True(roundTripped.UseOtsu);
                    Assert.Equal(90, roundTripped.ManualThreshold);
                    Assert.True(roundTripped.DetectDark);
                    Assert.Equal(3, roundTripped.MinArea);
                }),
            new(
                new CircularCaliperMeasureRoi
                {
                    Center = new Point(25, 30),
                    Radius = 11,
                    CaliperCount = 21,
                    CaliperSearchRange = 9,
                    CaliperSamplingHalfWidth = 2,
                    MinimumValidCalipers = 7,
                    CaliperMinimumGradient = 5.5,
                    CaliperOutlierThreshold = 1.75,
                    CaliperEdgePolarity = CaliperEdgePolarity.LightToDark
                },
                roi =>
                {
                    var roundTripped = Assert.IsType<CircularCaliperMeasureRoi>(roi);
                    Assert.Equal(new Point(25, 30), roundTripped.Center);
                    Assert.Equal(11, roundTripped.Radius);
                    Assert.Equal(21, roundTripped.CaliperCount);
                    Assert.Equal(9, roundTripped.CaliperSearchRange);
                    Assert.Equal(2, roundTripped.CaliperSamplingHalfWidth);
                    Assert.Equal(7, roundTripped.MinimumValidCalipers);
                    Assert.Equal(5.5, roundTripped.CaliperMinimumGradient);
                    Assert.Equal(1.75, roundTripped.CaliperOutlierThreshold);
                    Assert.Equal(CaliperEdgePolarity.LightToDark, roundTripped.CaliperEdgePolarity);
                }),
            new(
                new ConcentricityMeasureRoi
                {
                    Center1 = new Point(4, 5),
                    Radius1 = 6,
                    Center2 = new Point(7, 8),
                    Radius2 = 9
                },
                roi =>
                {
                    var roundTripped = Assert.IsType<ConcentricityMeasureRoi>(roi);
                    Assert.Equal(new Point(4, 5), roundTripped.Center1);
                    Assert.Equal(6, roundTripped.Radius1);
                    Assert.Equal(new Point(7, 8), roundTripped.Center2);
                    Assert.Equal(9, roundTripped.Radius2);
                })
        ];

        public static IEnumerable<object[]> SerializationCases =>
            SerializationExpectations.Select(static expectation => new object[] { expectation });

        public static IEnumerable<object[]> LegacyFixtureCases =>
            new[]
            {
                new object[]
                {
                    new LegacyPersistenceFixtureExpectation(
                        "legacy-flat-blob-analysis.json",
                        0.5,
                        "mm",
                        roi =>
                        {
                            var roundTripped = Assert.IsType<BlobAnalysisRoi>(roi);
                            Assert.Equal("legacy-blob", roundTripped.Label);
                            Assert.Equal(new Point(10, 12), roundTripped.Center);
                            Assert.Equal(14, roundTripped.Width);
                            Assert.Equal(16, roundTripped.Height);
                            Assert.Equal(18, roundTripped.Angle);
                            Assert.True(roundTripped.UseOtsu);
                            Assert.Equal(90, roundTripped.ManualThreshold);
                            Assert.True(roundTripped.DetectDark);
                            Assert.Equal(3, roundTripped.MinArea);
                        })
                },
                new object[]
                {
                    new LegacyPersistenceFixtureExpectation(
                        "legacy-flat-circular-caliper.json",
                        1.25,
                        "px",
                        roi =>
                        {
                            var roundTripped = Assert.IsType<CircularCaliperMeasureRoi>(roi);
                            Assert.Equal("legacy-circular-caliper", roundTripped.Label);
                            Assert.Equal(new Point(25, 30), roundTripped.Center);
                            Assert.Equal(11, roundTripped.Radius);
                            Assert.Equal(21, roundTripped.CaliperCount);
                            Assert.Equal(9, roundTripped.CaliperSearchRange);
                            Assert.Equal(2, roundTripped.CaliperSamplingHalfWidth);
                            Assert.Equal(7, roundTripped.MinimumValidCalipers);
                            Assert.Equal(5.5, roundTripped.CaliperMinimumGradient);
                            Assert.Equal(1.75, roundTripped.CaliperOutlierThreshold);
                            Assert.Equal(CaliperEdgePolarity.LightToDark, roundTripped.CaliperEdgePolarity);
                        })
                },
                new object[]
                {
                    new LegacyPersistenceFixtureExpectation(
                        "legacy-flat-polygon.json",
                        2.0,
                        "um",
                        roi =>
                        {
                            var roundTripped = Assert.IsType<PolygonRoi>(roi);
                            Assert.Equal("legacy-polygon", roundTripped.Label);
                            Assert.True(roundTripped.IsClosed);
                            Assert.Equal(3, roundTripped.Points.Count);
                            Assert.Equal(new Point(5, 5), roundTripped.Points[2]);
                        })
                }
            };

        [Theory]
        [MemberData(nameof(SerializationCases))]
        public void SerializeDeserialize_RoundTripsRepresentativeRois_AndKeepsFlatJsonShape(RoiPersistenceServiceRoundTripExpectation expectation)
        {
            RoiPluginRegistry registry = RoiPluginRegistry.CreateBuiltIn();

            string json = RoiPersistenceService.Serialize([expectation.SourceRoi], 2.5, "mm", registry);

            Assert.DoesNotContain("\"Common\"", json, StringComparison.Ordinal);
            Assert.DoesNotContain("\"Geometry\"", json, StringComparison.Ordinal);
            Assert.DoesNotContain("\"Measurement\"", json, StringComparison.Ordinal);
            Assert.DoesNotContain("\"Options\"", json, StringComparison.Ordinal);

            var (rois, pixelSize, physicalUnit) = RoiPersistenceService.Deserialize(json, registry);
            RoiBase roi = Assert.Single(rois);

            Assert.Equal(2.5, pixelSize);
            Assert.Equal("mm", physicalUnit);
            expectation.AssertRoundTripped(roi);
        }

        [Theory]
        [MemberData(nameof(LegacyFixtureCases))]
        public void Deserialize_LegacyFlatJsonFixtures_RemainCompatible(LegacyPersistenceFixtureExpectation expectation)
        {
            RoiPluginRegistry registry = RoiPluginRegistry.CreateBuiltIn();
            string fixtureJson = File.ReadAllText(GetFixturePath(expectation.FileName));

            var (rois, pixelSize, physicalUnit) = RoiPersistenceService.Deserialize(fixtureJson, registry);
            RoiBase roi = Assert.Single(rois);

            Assert.Equal(expectation.ExpectedPixelSize, pixelSize);
            Assert.Equal(expectation.ExpectedPhysicalUnit, physicalUnit);
            expectation.AssertRoundTripped(roi);

            string reserialized = RoiPersistenceService.Serialize([roi], pixelSize, physicalUnit, registry);
            Assert.DoesNotContain("\"Common\"", reserialized, StringComparison.Ordinal);
            Assert.DoesNotContain("\"Geometry\"", reserialized, StringComparison.Ordinal);
            Assert.DoesNotContain("\"Measurement\"", reserialized, StringComparison.Ordinal);
            Assert.DoesNotContain("\"Options\"", reserialized, StringComparison.Ordinal);
        }

        [Fact]
        public void Serialize_WithoutPluginRegistry_Throws()
        {
            ArgumentNullException ex = Assert.Throws<ArgumentNullException>(() =>
                RoiPersistenceService.Serialize([new CircleRoi()], 1.0, "px", pluginRegistry: null));

            Assert.Equal("pluginRegistry", ex.ParamName);
        }

        [Fact]
        public void Deserialize_UnknownType_IgnoresItemAndKeepsKnownItems()
        {
            RoiPluginRegistry registry = RoiPluginRegistry.CreateBuiltIn();
            string knownJson = RoiPersistenceService.Serialize([new CircleRoi()], 2.0, "mm", registry);
            using var document = System.Text.Json.JsonDocument.Parse(knownJson);
            using var stream = new MemoryStream();
            using (var writer = new System.Text.Json.Utf8JsonWriter(stream))
            {
                writer.WriteStartObject();
                writer.WriteNumber("Version", 1);
                writer.WriteNumber("PixelSize", 2.0);
                writer.WriteString("PhysicalUnit", "mm");
                writer.WriteStartArray("Items");
                writer.WriteRawValue(document.RootElement.GetProperty("Items")[0].GetRawText());
                writer.WriteStartObject();
                writer.WriteString("Type", "unknown-roi-type");
                writer.WriteEndObject();
                writer.WriteEndArray();
                writer.WriteEndObject();
            }

            var (rois, pixelSize, physicalUnit) = RoiPersistenceService.Deserialize(
                System.Text.Encoding.UTF8.GetString(stream.ToArray()),
                registry);

            Assert.Single(rois);
            Assert.IsType<CircleRoi>(rois[0]);
            Assert.Equal(2.0, pixelSize);
            Assert.Equal("mm", physicalUnit);
        }

        private static string GetFixturePath(string fileName)
        {
            return Path.Combine(AppContext.BaseDirectory, "Fixtures", "RoiPersistence", fileName);
        }

        public sealed record RoiPersistenceServiceRoundTripExpectation(
            RoiBase SourceRoi,
            Action<RoiBase> AssertRoundTripped);

        public sealed record LegacyPersistenceFixtureExpectation(
            string FileName,
            double ExpectedPixelSize,
            string ExpectedPhysicalUnit,
            Action<RoiBase> AssertRoundTripped);
    }
}