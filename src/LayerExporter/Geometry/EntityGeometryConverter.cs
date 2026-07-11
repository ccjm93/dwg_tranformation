using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using LayerExporter.Core.Geometry;
using NtsGeometry = NetTopologySuite.Geometries.Geometry;

namespace LayerExporter.Geometry;

public sealed record ConversionOptions(double Tolerance, bool ClosedPolylinesAsPolygons, bool IncludeZ);

public sealed record ConversionResult(NtsGeometry? Geometry, Dictionary<string, object?> Attributes, string? SkipReason);

/// <summary>AutoCAD 엔티티 → NTS 지오메트리 + DBF 속성 변환.</summary>
public static class EntityGeometryConverter
{
    public static ConversionResult Convert(Entity ent, ConversionOptions opt)
    {
        var attrs = BaseAttributes(ent);
        try
        {
            return ent switch
            {
                DBPoint p => Ok(PointOf(p.Position, opt), attrs, p.Position.Z),
                BlockReference br => ConvertBlockReference(br, opt, attrs),
                DBText t => ConvertText(t.Position, t.TextString, opt, attrs),
                MText mt => ConvertText(mt.Location, mt.Text, opt, attrs),
                Line l => ConvertLine(l, opt, attrs),
                Polyline pl => ConvertLwPolyline(pl, opt, attrs),
                Arc a => ConvertArc(a, opt, attrs),
                Circle c => ConvertCircle(c, opt, attrs),
                Curve c => ConvertGenericCurve(c, opt, attrs), // Ellipse/Spline/Polyline2d/3d 등
                _ => Skip(ent, attrs),
            };
        }
        catch (System.Exception ex)
        {
            return new ConversionResult(null, attrs, $"변환 중 오류: {ex.Message}");
        }
    }

    private static Dictionary<string, object?> BaseAttributes(Entity ent)
    {
        return new Dictionary<string, object?>
        {
            ["Layer"] = ent.Layer,
            ["EntType"] = ent.GetType().Name,
            ["Handle"] = ent.Handle.ToString(),
            ["Color"] = ent.Color.IsByLayer ? "ByLayer" : ent.Color.ColorIndex.ToString(),
            ["Linetype"] = ent.Linetype,
            ["Text"] = "",
            ["BlkName"] = "",
            ["Elev"] = 0.0,
        };
    }

    private static ConversionResult Ok(NtsGeometry geometry, Dictionary<string, object?> attrs, double elevation)
    {
        attrs["Elev"] = elevation;
        return new ConversionResult(geometry, attrs, null);
    }

    private static ConversionResult Skip(Entity ent, Dictionary<string, object?> attrs)
    {
        var typeName = ent.GetType().FullName ?? ent.GetType().Name;
        var reason = typeName.StartsWith("Autodesk.Civil", StringComparison.Ordinal)
            ? $"Civil 3D 전용 객체 ({ent.GetType().Name})"
            : $"지원되지 않는 객체 형식 ({ent.GetType().Name})";
        return new ConversionResult(null, attrs, reason);
    }

    private static NetTopologySuite.Geometries.Point PointOf(Point3d p, ConversionOptions opt)
    {
        return GeometryBuilder.BuildPoint(p.X, p.Y, opt.IncludeZ ? p.Z : null);
    }

    private static ConversionResult ConvertBlockReference(BlockReference br, ConversionOptions opt, Dictionary<string, object?> attrs)
    {
        attrs["BlkName"] = br.Name;
        return Ok(PointOf(br.Position, opt), attrs, br.Position.Z);
    }

    private static ConversionResult ConvertText(Point3d position, string text, ConversionOptions opt, Dictionary<string, object?> attrs)
    {
        attrs["Text"] = text;
        return Ok(PointOf(position, opt), attrs, position.Z);
    }

    private static ConversionResult ConvertLine(Line l, ConversionOptions opt, Dictionary<string, object?> attrs)
    {
        var pts = new List<Pt2> { new(l.StartPoint.X, l.StartPoint.Y), new(l.EndPoint.X, l.EndPoint.Y) };
        var zs = opt.IncludeZ ? new List<double> { l.StartPoint.Z, l.EndPoint.Z } : null;
        return Ok(GeometryBuilder.BuildLineString(pts, zs), attrs, l.StartPoint.Z);
    }

    private static ConversionResult ConvertLwPolyline(Polyline pl, ConversionOptions opt, Dictionary<string, object?> attrs)
    {
        var vertices = new List<(Pt2, double)>(pl.NumberOfVertices);
        for (var i = 0; i < pl.NumberOfVertices; i++)
        {
            var p = pl.GetPoint2dAt(i);
            vertices.Add((new Pt2(p.X, p.Y), pl.GetBulgeAt(i)));
        }

        var pts = Tessellation.TessellatePolyline(vertices, pl.Closed, opt.Tolerance);
        return BuildLineOrPolygon(pts, pl.Closed, pl.Elevation, opt, attrs);
    }

    private static ConversionResult ConvertArc(Arc a, ConversionOptions opt, Dictionary<string, object?> attrs)
    {
        var sweep = a.EndAngle - a.StartAngle;
        if (sweep <= 0)
        {
            sweep += Math.PI * 2;
        }

        var pts = Tessellation.TessellateArc(
            new Pt2(a.Center.X, a.Center.Y), a.Radius, a.StartAngle, sweep, opt.Tolerance);
        // IncludeZ 시 다른 곡선과 동일하게 Z를 채워 한 파일 안에서 2D/3D가 섞이지 않게 한다
        var zs = opt.IncludeZ ? Enumerable.Repeat(a.Center.Z, pts.Count).ToList() : null;
        return Ok(GeometryBuilder.BuildLineString(pts, zs), attrs, a.Center.Z);
    }

    private static ConversionResult ConvertCircle(Circle c, ConversionOptions opt, Dictionary<string, object?> attrs)
    {
        var pts = Tessellation.TessellateCircle(new Pt2(c.Center.X, c.Center.Y), c.Radius, opt.Tolerance);
        return BuildLineOrPolygon(pts, true, c.Center.Z, opt, attrs);
    }

    /// <summary>Ellipse/Spline/Polyline2d/3d 등 일반 Curve를 거리 기반 샘플링으로 분할한다.</summary>
    private static ConversionResult ConvertGenericCurve(Curve c, ConversionOptions opt, Dictionary<string, object?> attrs)
    {
        var length = c.GetDistanceAtParameter(c.EndParam) - c.GetDistanceAtParameter(c.StartParam);
        if (length <= 0)
        {
            return new ConversionResult(null, attrs, "길이가 0인 곡선");
        }

        // 허용오차에 비례한 현 길이로 샘플 수 결정 (곡률을 모르므로 보수적으로)
        var chordStep = Math.Max(opt.Tolerance * 20.0, 1e-6);
        var n = Math.Clamp((int)Math.Ceiling(length / chordStep), 8, 2048);

        var pts = new List<Pt2>(n + 1);
        var zs = opt.IncludeZ ? new List<double>(n + 1) : null;
        var startDist = c.GetDistanceAtParameter(c.StartParam);
        for (var i = 0; i <= n; i++)
        {
            var p = c.GetPointAtDist(startDist + length * i / n);
            pts.Add(new Pt2(p.X, p.Y));
            zs?.Add(p.Z);
        }

        var firstZ = c.StartPoint.Z;
        return BuildLineOrPolygon(pts, c.Closed, firstZ, opt, attrs, zs);
    }

    private static ConversionResult BuildLineOrPolygon(
        List<Pt2> pts, bool closed, double elevation, ConversionOptions opt,
        Dictionary<string, object?> attrs, List<double>? zs = null)
    {
        if (pts.Count < 2)
        {
            return new ConversionResult(null, attrs, "점이 부족한 형상");
        }

        if (closed && opt.ClosedPolylinesAsPolygons)
        {
            var polygon = GeometryBuilder.TryBuildPolygon(pts, out _);
            if (polygon is not null)
            {
                return Ok(polygon, attrs, elevation);
            }
            // 유효하지 않은 폴리곤은 라인으로 강등
        }

        var lineZs = opt.IncludeZ ? (IReadOnlyList<double>?)(zs ?? Enumerable.Repeat(elevation, pts.Count).ToList()) : null;
        return Ok(GeometryBuilder.BuildLineString(pts, lineZs), attrs, elevation);
    }
}
