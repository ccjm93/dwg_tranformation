using LayerExporter.Core.Geometry;
using Xunit;

namespace LayerExporter.Tests;

public class TessellationTests
{
    [Fact]
    public void BulgeSemicircle_PointsLieOnCircle()
    {
        // bulge=1 → 180도 호. (0,0)→(2,0)이면 중심 (1,0), 반지름 1
        var pts = Tessellation.TessellateBulgeSegment(new Pt2(0, 0), new Pt2(2, 0), 1.0, 0.001);

        Assert.True(pts.Count >= 3);
        Assert.Equal(0, pts[0].X, 9);
        Assert.Equal(0, pts[0].Y, 9);
        Assert.Equal(2, pts[^1].X, 6);
        Assert.Equal(0, pts[^1].Y, 6);

        foreach (var p in pts)
        {
            var r = Math.Sqrt((p.X - 1) * (p.X - 1) + p.Y * p.Y);
            Assert.Equal(1.0, r, 6);
        }

        // 양수 bulge = CCW → +X 방향 현에서는 호가 아래로 볼록
        var mid = pts[pts.Count / 2];
        Assert.True(mid.Y < 0);
    }

    [Fact]
    public void ZeroBulge_ReturnsStraightSegment()
    {
        var pts = Tessellation.TessellateBulgeSegment(new Pt2(0, 0), new Pt2(3, 4), 0.0, 0.01);
        Assert.Equal(2, pts.Count);
        Assert.Equal(new Pt2(0, 0), pts[0]);
        Assert.Equal(new Pt2(3, 4), pts[1]);
    }

    [Fact]
    public void NegativeBulge_ArcBulgesUpForPositiveXChord()
    {
        var pts = Tessellation.TessellateBulgeSegment(new Pt2(0, 0), new Pt2(2, 0), -0.5, 0.001);
        var mid = pts[pts.Count / 2];
        Assert.True(mid.Y > 0);
    }

    [Fact]
    public void ArcTessellation_RespectsSagittaTolerance()
    {
        const double radius = 10.0;
        const double tolerance = 0.01;
        var pts = Tessellation.TessellateArc(new Pt2(0, 0), radius, 0, Math.PI, tolerance);

        // 각 세그먼트 현의 중점에서 호까지의 거리(sagitta)가 허용오차 이하인지 확인
        for (var i = 0; i < pts.Count - 1; i++)
        {
            var midX = (pts[i].X + pts[i + 1].X) / 2;
            var midY = (pts[i].Y + pts[i + 1].Y) / 2;
            var distToCenter = Math.Sqrt(midX * midX + midY * midY);
            var sagitta = radius - distToCenter;
            Assert.True(sagitta <= tolerance * 1.01, $"세그먼트 {i}의 sagitta {sagitta}가 허용오차 초과");
        }
    }

    [Fact]
    public void ClosedPolyline_FirstAndLastPointsMatch()
    {
        var square = new List<(Pt2, double)>
        {
            (new Pt2(0, 0), 0),
            (new Pt2(10, 0), 0),
            (new Pt2(10, 10), 0),
            (new Pt2(0, 10), 0),
        };

        var pts = Tessellation.TessellatePolyline(square, closed: true, tolerance: 0.01);
        Assert.Equal(5, pts.Count);
        Assert.Equal(pts[0], pts[^1]);
    }

    [Fact]
    public void CircleTessellation_IsClosed()
    {
        var pts = Tessellation.TessellateCircle(new Pt2(5, 5), 3, 0.01);
        Assert.Equal(pts[0].X, pts[^1].X, 6);
        Assert.Equal(pts[0].Y, pts[^1].Y, 6);
    }
}
