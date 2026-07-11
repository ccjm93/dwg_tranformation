using NetTopologySuite.Geometries;

namespace LayerExporter.Core.Geometry;

/// <summary>2D 점 목록 → NTS 지오메트리 생성 (링 방향 보정, 유효성 검사 포함).</summary>
public static class GeometryBuilder
{
    public static readonly GeometryFactory Factory = new(new PrecisionModel(), 0);

    public static LineString BuildLineString(IReadOnlyList<Pt2> points, IReadOnlyList<double>? zs = null)
    {
        var coords = ToCoordinates(points, zs);
        return Factory.CreateLineString(coords);
    }

    /// <summary>
    /// 닫힌 점 목록으로 폴리곤을 생성한다. 링이 닫혀있지 않으면 닫고,
    /// 외곽 링은 CCW(OGC 규약)로 보정한다. 유효하지 않으면 null을 반환한다.
    /// </summary>
    public static Polygon? TryBuildPolygon(IReadOnlyList<Pt2> points, out string? failReason)
    {
        failReason = null;
        var pts = new List<Pt2>(points);
        if (pts.Count > 1 && (pts[0].X != pts[^1].X || pts[0].Y != pts[^1].Y))
        {
            pts.Add(pts[0]);
        }

        if (pts.Count < 4)
        {
            failReason = "폴리곤을 구성하기에 점이 부족함";
            return null;
        }

        try
        {
            var ring = Factory.CreateLinearRing(ToCoordinates(pts, null));
            if (!NetTopologySuite.Algorithm.Orientation.IsCCW(ring.CoordinateSequence))
            {
                ring = (LinearRing)ring.Reverse();
            }

            var polygon = Factory.CreatePolygon(ring);
            if (!polygon.IsValid)
            {
                failReason = "자기교차 등으로 유효하지 않은 폴리곤";
                return null;
            }

            return polygon;
        }
        catch (System.Exception ex)
        {
            failReason = ex.Message;
            return null;
        }
    }

    public static Point BuildPoint(double x, double y, double? z = null)
    {
        return z.HasValue
            ? Factory.CreatePoint(new CoordinateZ(x, y, z.Value))
            : Factory.CreatePoint(new Coordinate(x, y));
    }

    private static Coordinate[] ToCoordinates(IReadOnlyList<Pt2> points, IReadOnlyList<double>? zs)
    {
        var coords = new Coordinate[points.Count];
        for (var i = 0; i < points.Count; i++)
        {
            coords[i] = zs is not null && i < zs.Count
                ? new CoordinateZ(points[i].X, points[i].Y, zs[i])
                : new Coordinate(points[i].X, points[i].Y);
        }

        return coords;
    }
}
