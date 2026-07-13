using LayerExporter.Core.Geometry;
using NetTopologySuite.Algorithm;
using Xunit;

namespace LayerExporter.Tests;

public class GeometryBuilderTests
{
    [Fact]
    public void ClockwiseRing_IsCorrectedToCcwShell()
    {
        // 시계방향 사각형 입력
        var cwSquare = new List<Pt2>
        {
            new(0, 0), new(0, 10), new(10, 10), new(10, 0),
        };

        var polygon = GeometryBuilder.TryBuildPolygon(cwSquare, out var reason);

        Assert.NotNull(polygon);
        Assert.Null(reason);
        Assert.True(Orientation.IsCCW(polygon!.ExteriorRing.CoordinateSequence));
        Assert.Equal(100.0, polygon.Area, 9);
    }

    [Fact]
    public void SelfIntersectingRing_ReturnsNull()
    {
        // 나비넥타이(자기교차) 형상
        var bowtie = new List<Pt2>
        {
            new(0, 0), new(10, 10), new(10, 0), new(0, 10),
        };

        var polygon = GeometryBuilder.TryBuildPolygon(bowtie, out var reason);

        Assert.Null(polygon);
        Assert.NotNull(reason);
    }

    [Fact]
    public void TooFewPoints_ReturnsNull()
    {
        var line = new List<Pt2> { new(0, 0), new(10, 0) };
        var polygon = GeometryBuilder.TryBuildPolygon(line, out var reason);
        Assert.Null(polygon);
        Assert.NotNull(reason);
    }

    [Fact]
    public void OpenRing_IsClosedAutomatically()
    {
        var openSquare = new List<Pt2>
        {
            new(0, 0), new(10, 0), new(10, 10), new(0, 10),
        };

        var polygon = GeometryBuilder.TryBuildPolygon(openSquare, out _);
        Assert.NotNull(polygon);
        Assert.Equal(100.0, polygon!.Area, 9);
    }

    [Fact]
    public void ClockwisePolygonWithZ_ClosesReorientsAndPreservesZValues()
    {
        var openSquare = new List<Pt2>
        {
            new(0, 0), new(0, 10), new(10, 10), new(10, 0),
        };
        var zs = new List<double> { 1, 2, 3, 4 };

        var polygon = GeometryBuilder.TryBuildPolygon(openSquare, out var reason, zs);

        Assert.NotNull(polygon);
        Assert.Null(reason);
        var coordinates = polygon!.ExteriorRing.Coordinates;
        Assert.Equal(5, coordinates.Length);
        Assert.True(Orientation.IsCCW(polygon.ExteriorRing.CoordinateSequence));
        Assert.Equal(coordinates[0].Z, coordinates[^1].Z);
        Assert.Equal(new[] { 1.0, 2.0, 3.0, 4.0 }, coordinates.Take(4).Select(c => c.Z).OrderBy(z => z));
    }
}
