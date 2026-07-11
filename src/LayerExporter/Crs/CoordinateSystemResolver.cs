using System.Reflection;
using LayerExporter.Core.Crs;

namespace LayerExporter.Crs;

public sealed record CrsResolution(string? EsriWkt, string? Code, string Source);

/// <summary>
/// Civil 3D 도면 좌표계 코드를 읽고 3단계 폴백으로 ESRI WKT를 해석한다.
/// Civil 3D API(AeccDbMgd)는 버전 간 바이너리 호환이 보장되지 않으므로
/// 컴파일 참조 없이 리플렉션으로만 접근한다. 덕분에 하나의 바이너리가
/// 같은 밴드의 모든 Civil 3D 버전과 (좌표계 기능 없이) 순수 AutoCAD에서도 동작한다.
/// Tier 1: AdSpatialReferenceMgd (설치본 API) / Tier 2: 정적 카탈로그 / Tier 3: 실패(.prj 생략)
/// </summary>
public static class CoordinateSystemResolver
{
    public static string? GetDrawingCoordinateSystemCode()
    {
        try
        {
            // CivilApplication.ActiveDocument.Settings.DrawingSettings.UnitZoneSettings.CoordinateSystemCode
            var civilAppType = FindCivilApplicationType();
            var civilDoc = civilAppType
                ?.GetProperty("ActiveDocument", BindingFlags.Public | BindingFlags.Static)
                ?.GetValue(null);
            var settings = GetPropertyValue(civilDoc, "Settings");
            var drawingSettings = GetPropertyValue(settings, "DrawingSettings");
            var unitZone = GetPropertyValue(drawingSettings, "UnitZoneSettings");
            var code = GetPropertyValue(unitZone, "CoordinateSystemCode") as string;
            return string.IsNullOrWhiteSpace(code) ? null : code;
        }
        catch
        {
            // Civil 3D가 아니거나(순수 AutoCAD) 설정 접근 실패
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

    private static Type? FindCivilApplicationType()
    {
        // Civil 3D 프로세스에는 AeccDbMgd가 이미 로드되어 있다 (없으면 Civil 3D가 아님)
        var assembly = AppDomain.CurrentDomain.GetAssemblies()
            .FirstOrDefault(a => string.Equals(a.GetName().Name, "AeccDbMgd", StringComparison.OrdinalIgnoreCase));
        var type = assembly?.GetType("Autodesk.Civil.ApplicationServices.CivilApplication");
        return type ?? Type.GetType("Autodesk.Civil.ApplicationServices.CivilApplication, AeccDbMgd", throwOnError: false);
    }

    private static object? GetPropertyValue(object? obj, string name)
    {
        return obj?.GetType().GetProperty(name)?.GetValue(obj);
    }
}
