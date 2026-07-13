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

public sealed record ShpExtractionResult(
    IReadOnlyList<ShpFeature> Features,
    IReadOnlyList<(string Handle, string Reason)> Skipped);

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
        var extraction = Extract(db, ids, options, progress);
        return Write(basePathNoExt, extraction, esriWkt);
    }

    /// <summary>
    /// AutoCAD 엔티티를 독립적인 피처 데이터로 변환한다.
    /// 호출자는 실행 중 원본 문서 잠금을 유지해야 한다.
    /// </summary>
    public static ShpExtractionResult Extract(
        Database db,
        ObjectIdCollection ids,
        ConversionOptions options,
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

        return new ShpExtractionResult(features, skipped);
    }

    /// <summary>AutoCAD와 무관한 추출 결과를 shapefile로 기록한다.</summary>
    public static ShpExportSummary Write(
        string basePathNoExt,
        ShpExtractionResult extraction,
        string? esriWkt)
    {
        if (extraction.Features.Count == 0)
        {
            throw new InvalidOperationException(
                $"내보낼 수 있는 지원 형상이 없습니다. 제외된 객체: {extraction.Skipped.Count}개.");
        }

        var writeResult = ShpWriter.Write(basePathNoExt, extraction.Features, esriWkt);

        return new ShpExportSummary(
            writeResult.WrittenFiles,
            writeResult.PointCount,
            writeResult.LineCount,
            writeResult.PolygonCount,
            extraction.Skipped,
            esriWkt is not null && writeResult.WrittenFiles.Count > 0);
    }
}
