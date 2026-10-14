using System;
using ImageViewer.Core.Measurements;
using ImageViewer.Core.Persistence;
using ImageViewer.Models;
using ImageViewer.Plugins;
using ImageViewer.Services;
using Xunit;

namespace ImageViewerControl.Tests;

public class AdvancedMeasurementRecipeAlignmentTests
{
    [Fact]
    public void AlignmentTransformsImageCoordinatePositionNominalsAndKeepsWorkpieceNominalsFixed()
    {
        AdvancedMeasurementDefinition imageNominal = new()
        {
            Name = "image-position",
            Kind = AdvancedMeasurementKind.Position,
            NominalX = 120,
            NominalY = 130
        };
        AdvancedMeasurementDefinition workpieceNominal = new()
        {
            Name = "workpiece-position",
            Kind = AdvancedMeasurementKind.Position,
            NominalX = 7,
            NominalY = 8,
            NominalInWorkpieceCoordinates = true
        };
        MeasurementRecipeDocument recipe = MeasurementRecipeService.Create(
            "position nominals",
            null,
            [new CircleRoi { Center = new PointD(120, 130), Radius = 4, AdvancedMeasurements = [imageNominal, workpieceNominal] }],
            1,
            "px",
            RoiPluginRegistry.CreateBuiltIn(),
            datumPoints:
            [
                new RecipeDatumPoint { Name = "reference-1", X = 100, Y = 100 },
                new RecipeDatumPoint { Name = "reference-2", X = 200, Y = 100 }
            ]);

        RoiDocument aligned = MeasurementRecipeService.BuildAlignedRoiDocument(
            recipe, new PointD(300, 400), new PointD(300, 500));
        AdvancedMeasurementDefinition[] transformed = Assert.Single(aligned.Items).AdvancedMeasurements;

        Assert.Equal(new PointD(270, 420), new PointD(transformed[0].NominalX, transformed[0].NominalY));
        Assert.False(transformed[0].NominalInWorkpieceCoordinates);
        Assert.Equal(new PointD(7, 8), new PointD(transformed[1].NominalX, transformed[1].NominalY));
        Assert.True(transformed[1].NominalInWorkpieceCoordinates);
        RoiPersistencePoint originalCenter = recipe.Items[0].Template!.Center!;
        Assert.Equal(new PointD(120, 130), new PointD(originalCenter.X, originalCenter.Y));
    }
}
