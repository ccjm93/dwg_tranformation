using Autodesk.AutoCAD.DatabaseServices;
using LayerExporter.Geometry;
using LayerExporter.Core.Shp;

namespace LayerExporter.Services;

public sealed record ShpExportSummary(
    IReadOnlyList<string> WrittenFiles,
    int PointCount,
    int LineCount,
    int PolygonCount,
    IReadOnlyList<(string Handle, string Reason)> Skipped,
    bool PrjWritten);

/// <summary>선택 엔티티를 NTS 지오메트리로 변환해 타입별 shapefile로 저장한다.</summary>
public static class ShpExportService
{
    public static ShpExportSummary Export(
        Database db,
        ObjectIdCollection ids,
        string basePathNoExt,
        ConversionOptions options,
        string? esriWkt,
        Action? progress = null)
    {
        var features = new List<ShpFeature>();
        var skipped = new List<(string, string)>();

        using (var tr = db.TransactionManager.StartTransaction())
        {
            foreach (ObjectId id in ids)
            {
                if (tr.GetObject(id, OpenMode.ForRead, false) is Entity ent)
                {
                    var result = EntityGeometryConverter.Convert(ent, options);
                    if (result.Geometry is not null)
                    {
                        features.Add(new ShpFeature(result.Geometry, result.Attributes));
                    }
                    else
                    {
                        skipped.Add((ent.Handle.ToString(), result.SkipReason ?? "알 수 없는 사유"));
                    }
                }

                progress?.Invoke();
            }

            tr.Commit();
        }

        var writeResult = ShpWriter.Write(basePathNoExt, features, esriWkt);

        return new ShpExportSummary(
            writeResult.WrittenFiles,
            writeResult.PointCount,
            writeResult.LineCount,
            writeResult.PolygonCount,
            skipped,
            esriWkt is not null && writeResult.WrittenFiles.Count > 0);
    }
}
