using System.Text;
using NetTopologySuite.Features;
using NetTopologySuite.Geometries;
using NetTopologySuite.IO.Esri;
using NtsGeometry = NetTopologySuite.Geometries.Geometry;

namespace LayerExporter.Core.Shp;

public sealed record ShpFeature(NtsGeometry Geometry, IReadOnlyDictionary<string, object?> Attributes);

public sealed record ShpWriteResult(IReadOnlyList<string> WrittenFiles, int PointCount, int LineCount, int PolygonCount);

/// <summary>
/// NTS 지오메트리를 지오메트리 타입별로 분리된 shapefile로 저장한다.
/// Shapefile 규격상 한 파일에는 한 가지 지오메트리 타입만 담을 수 있다.
/// 속성은 UTF-8로 기록되고 .cpg 파일이 함께 생성된다.
/// </summary>
public static class ShpWriter
{
    private const int MaxDbfTextLength = 254;

    /// <param name="basePathNoExt">확장자 없는 기본 경로. {base}_point.shp / _line.shp / _polygon.shp 로 분리 저장.</param>
    /// <param name="esriWkt">null이 아니면 각 .shp 옆에 .prj 파일을 생성한다.</param>
    public static ShpWriteResult Write(string basePathNoExt, IEnumerable<ShpFeature> features, string? esriWkt)
    {
        var points = new List<IFeature>();
        var lines = new List<IFeature>();
        var polygons = new List<IFeature>();

        foreach (var f in features)
        {
            var feature = ToNtsFeature(f);
            switch (f.Geometry)
            {
                case Point or MultiPoint:
                    points.Add(feature);
                    break;
                case LineString or MultiLineString:
                    lines.Add(feature);
                    break;
                case Polygon or MultiPolygon:
                    polygons.Add(feature);
                    break;
            }
        }

        var written = new List<string>();
        WriteBucket($"{basePathNoExt}_point.shp", points, esriWkt, written);
        WriteBucket($"{basePathNoExt}_line.shp", lines, esriWkt, written);
        WriteBucket($"{basePathNoExt}_polygon.shp", polygons, esriWkt, written);

        return new ShpWriteResult(written, points.Count, lines.Count, polygons.Count);
    }

    private static void WriteBucket(string shpPath, List<IFeature> bucket, string? esriWkt, List<string> written)
    {
        if (bucket.Count == 0)
        {
            return;
        }

        Shapefile.WriteAllFeatures(bucket, shpPath);
        written.Add(shpPath);

        var basePath = Path.ChangeExtension(shpPath, null);
        // NetTopologySuite.IO.Esri는 기본 UTF-8로 쓰지만 .cpg가 없으면 생성해 명시한다
        var cpgPath = basePath + ".cpg";
        if (!File.Exists(cpgPath))
        {
            File.WriteAllText(cpgPath, "UTF-8", Encoding.ASCII);
        }

        if (!string.IsNullOrWhiteSpace(esriWkt))
        {
            File.WriteAllText(basePath + ".prj", esriWkt, Encoding.ASCII);
        }
    }

    private static Feature ToNtsFeature(ShpFeature f)
    {
        var table = new AttributesTable();
        foreach (var (key, value) in f.Attributes)
        {
            var v = value switch
            {
                null => "",
                string s when s.Length > MaxDbfTextLength => s[..MaxDbfTextLength],
                _ => value,
            };
            table.Add(key, v);
        }

        return new Feature(f.Geometry, table);
    }
}
