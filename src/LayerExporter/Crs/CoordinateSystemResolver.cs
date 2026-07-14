using System.Reflection;
using LayerExporter.Core.Crs;
using LayerExporter.Infrastructure;

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
    /// <summary>
    /// MAPCSASSIGN 명령을 제공하는 Civil 3D 또는 AutoCAD Map 3D인지 확인한다.
    /// 명령 존재 여부를 직접 질의하는 것이 1차 판정이고, 어셈블리 로드 검사는
    /// 명령이 아직 등록되지 않았을 수 있는(디맨드 로드 이전) 경우의 폴백이다.
    /// AeccDbMgd(Civil 3D)는 FindCivilApplicationType()이 담당한다.
    /// </summary>
    public static bool IsMapCsAssignAvailable()
    {
        return IsCommandDefined("MAPCSASSIGN")
            || AssemblyResolver.IsAssemblyLoaded("AcMapMgd")
            || AssemblyResolver.IsAssemblyLoaded("Autodesk.Gis.Map.Platform")
            || FindCivilApplicationType() is not null;
    }

    private static bool IsCommandDefined(string globalCommandName)
    {
        try
        {
            return Autodesk.AutoCAD.Internal.Utils.IsCommandDefined(globalCommandName);
        }
        catch
        {
            // 순수 AutoCAD 외 환경 또는 API 미지원 버전 — 어셈블리 검사 폴백에 맡긴다
            return false;
        }
    }

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
