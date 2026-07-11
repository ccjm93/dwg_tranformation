using Autodesk.Civil.ApplicationServices;
using LayerExporter.Core.Crs;

namespace LayerExporter.Crs;

public sealed record CrsResolution(string? EsriWkt, string? Code, string Source);

/// <summary>
/// Civil 3D 도면 좌표계 코드를 읽고 3단계 폴백으로 ESRI WKT를 해석한다.
/// Tier 1: AdSpatialReferenceMgd (설치본 API) / Tier 2: 정적 카탈로그 / Tier 3: 실패(.prj 생략)
/// </summary>
public static class CoordinateSystemResolver
{
    public static string? GetDrawingCoordinateSystemCode()
    {
        try
        {
            var civilDoc = CivilApplication.ActiveDocument;
            if (civilDoc is null)
            {
                return null;
            }

            var code = civilDoc.Settings.DrawingSettings.UnitZoneSettings.CoordinateSystemCode;
            return string.IsNullOrWhiteSpace(code) ? null : code;
        }
        catch
        {
            // Civil 3D 설정 접근 실패 (순수 AutoCAD 도면 등)
            return null;
        }
    }

    public static CrsResolution Resolve()
    {
        var code = GetDrawingCoordinateSystemCode();
        if (code is null)
        {
            return new CrsResolution(null, null, "도면에 좌표계가 설정되어 있지 않음");
        }

        var tier1 = SpatialReferenceWktProvider.TryGetEsriWkt(code);
        if (tier1 is not null)
        {
            return new CrsResolution(tier1, code, "Autodesk Spatial Reference API");
        }

        var tier2 = CrsCatalog.ResolveEsriWkt(code);
        if (tier2 is not null)
        {
            return new CrsResolution(tier2, code, "내장 한국 좌표계 카탈로그");
        }

        return new CrsResolution(null, code, $"좌표계 코드 '{code}'를 WKT로 변환하지 못함");
    }
}
