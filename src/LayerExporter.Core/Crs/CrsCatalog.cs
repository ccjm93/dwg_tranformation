using System.Reflection;
using System.Text.Json;

namespace LayerExporter.Core.Crs;

public sealed record CrsEntry(string Name, int Epsg, string[] AdskCodes, string EsriWkt);

/// <summary>
/// 한국 좌표계 중심의 정적 코드→ESRI WKT 카탈로그 (Tier 2 폴백).
/// Autodesk CS-Map 코드 또는 EPSG 번호("EPSG:5186", "5186")로 조회한다.
/// </summary>
public static class CrsCatalog
{
    private static readonly Lazy<List<CrsEntry>> Entries = new(LoadEntries);

    public static IReadOnlyList<CrsEntry> All => Entries.Value;

    public static string? ResolveEsriWkt(string code)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return null;
        }

        var trimmed = code.Trim();

        foreach (var entry in Entries.Value)
        {
            if (entry.AdskCodes.Any(c => string.Equals(c, trimmed, StringComparison.OrdinalIgnoreCase)))
            {
                return entry.EsriWkt;
            }
        }

        // "EPSG:5186" 또는 순수 숫자 형태 지원
        var numeric = trimmed.StartsWith("EPSG:", StringComparison.OrdinalIgnoreCase)
            ? trimmed.Substring(5)
            : trimmed;
        if (int.TryParse(numeric, out var epsg))
        {
            return Entries.Value.FirstOrDefault(e => e.Epsg == epsg)?.EsriWkt;
        }

        return null;
    }

    private static List<CrsEntry> LoadEntries()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var resourceName = assembly.GetManifestResourceNames()
            .First(n => n.EndsWith("korean_crs.json", StringComparison.OrdinalIgnoreCase));
        using var stream = assembly.GetManifestResourceStream(resourceName)!;
        using var doc = JsonDocument.Parse(stream);

        var list = new List<CrsEntry>();
        foreach (var e in doc.RootElement.GetProperty("entries").EnumerateArray())
        {
            list.Add(new CrsEntry(
                e.GetProperty("name").GetString()!,
                e.GetProperty("epsg").GetInt32(),
                e.GetProperty("adskCodes").EnumerateArray().Select(c => c.GetString()!).ToArray(),
                e.GetProperty("esriWkt").GetString()!));
        }

        return list;
    }
}
