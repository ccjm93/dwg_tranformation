using System.IO;
using System.Reflection;

namespace LayerExporter.Crs;

/// <summary>
/// Tier 1: Autodesk 설치본의 AdSpatialReferenceMgd.dll을 리플렉션으로 호출해
/// 좌표계 코드 → ESRI WKT 변환을 시도한다.
/// 비문서화 API이므로 이 클래스 안에 완전히 격리하고, 어떤 실패도 null로 흡수한다.
/// </summary>
public static class SpatialReferenceWktProvider
{
    private static readonly string[] ConvertMethodNames = ["AdskIdToWkt", "IdToWkt", "EpsgIdToWkt"];

    public static string? TryGetEsriWkt(string code)
    {
        try
        {
            var assembly = LoadSpatialReferenceAssembly();
            if (assembly is null)
            {
                return null;
            }

            // WktFlavor.Esri 값 확보 (없으면 flavor 없는 오버로드만 시도)
            var flavorType = assembly.GetTypes()
                .FirstOrDefault(t => t.IsEnum && t.Name == "WktFlavor");
            object? esriFlavor = null;
            if (flavorType is not null && Enum.GetNames(flavorType).Contains("Esri"))
            {
                esriFlavor = Enum.Parse(flavorType, "Esri");
            }

            foreach (var type in assembly.GetTypes())
            {
                foreach (var name in ConvertMethodNames)
                {
                    var wkt = TryInvoke(type, name, code, esriFlavor);
                    if (!string.IsNullOrWhiteSpace(wkt))
                    {
                        return wkt;
                    }
                }
            }

            return null;
        }
        catch
        {
            return null;
        }
    }

    private static string? TryInvoke(Type type, string methodName, string code, object? esriFlavor)
    {
        try
        {
            foreach (var m in type.GetMethods(BindingFlags.Public | BindingFlags.Static))
            {
                if (m.Name != methodName || m.ReturnType != typeof(string))
                {
                    continue;
                }

                var ps = m.GetParameters();
                object? result = ps.Length switch
                {
                    1 when ps[0].ParameterType == typeof(string) => m.Invoke(null, [code]),
                    2 when ps[0].ParameterType == typeof(string) && esriFlavor is not null
                           && ps[1].ParameterType == esriFlavor.GetType() => m.Invoke(null, [code, esriFlavor]),
                    _ => null,
                };
                if (result is string s && !string.IsNullOrWhiteSpace(s))
                {
                    return s;
                }
            }

            return null;
        }
        catch
        {
            return null;
        }
    }

    private static Assembly? LoadSpatialReferenceAssembly()
    {
        try
        {
            // 이미 AutoCAD 프로세스에 로드되어 있으면 그것을 사용
            var loaded = AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(a => a.GetName().Name?.Equals("AdSpatialReferenceMgd", StringComparison.OrdinalIgnoreCase) == true);
            if (loaded is not null)
            {
                return loaded;
            }

            // acdbmgd 위치 기준으로 Map\AdSpatialReferenceMgd.dll 탐색
            var acadRoot = Path.GetDirectoryName(typeof(Autodesk.AutoCAD.DatabaseServices.Database).Assembly.Location);
            if (acadRoot is null)
            {
                return null;
            }

            var path = Path.Combine(acadRoot, "Map", "AdSpatialReferenceMgd.dll");
            return File.Exists(path) ? Assembly.LoadFrom(path) : null;
        }
        catch
        {
            return null;
        }
    }
}
