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
    private static readonly string[] ManagedSidecarSuffixes =
    [
        ".shp", ".shx", ".dbf", ".cpg", ".prj",
        ".qix", ".sbn", ".sbx", ".fix", ".shp.xml",
    ];

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

        return WriteAtomically(basePathNoExt, points, lines, polygons, esriWkt);
    }

    private static ShpWriteResult WriteAtomically(string basePath, List<IFeature> points,
        List<IFeature> lines, List<IFeature> polygons, string? esriWkt)
    {
        var finalBase = Path.GetFullPath(basePath);
        var outputDirectory = Path.GetDirectoryName(finalBase)
            ?? throw new ArgumentException("The output path must include a directory.", nameof(basePath));
        Directory.CreateDirectory(outputDirectory);
        var stagingDirectory = Path.Combine(outputDirectory,
            $".{Path.GetFileName(finalBase)}.staging-{Guid.NewGuid():N}");
        Directory.CreateDirectory(stagingDirectory);

        try
        {
            var stagingBase = Path.Combine(stagingDirectory, Path.GetFileName(finalBase));
            WriteBucket($"{stagingBase}_point.shp", points, esriWkt);
            WriteBucket($"{stagingBase}_line.shp", lines, esriWkt);
            WriteBucket($"{stagingBase}_polygon.shp", polygons, esriWkt);
            CommitStagedFiles(finalBase, stagingBase);

            var written = new List<string>();
            if (points.Count > 0) written.Add($"{finalBase}_point.shp");
            if (lines.Count > 0) written.Add($"{finalBase}_line.shp");
            if (polygons.Count > 0) written.Add($"{finalBase}_polygon.shp");
            return new ShpWriteResult(written, points.Count, lines.Count, polygons.Count);
        }
        finally
        {
            DeleteDirectoryBestEffort(stagingDirectory);
        }
    }

    private static void WriteBucket(string shpPath, List<IFeature> bucket, string? esriWkt)
    {
        if (bucket.Count == 0)
        {
            return;
        }

        Shapefile.WriteAllFeatures(bucket, shpPath);
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

    private static void CommitStagedFiles(string finalBase, string stagingBase)
    {
        var outputDirectory = Path.GetDirectoryName(finalBase)!;
        var backupDirectory = Path.Combine(outputDirectory,
            $".{Path.GetFileName(finalBase)}.backup-{Guid.NewGuid():N}");
        Directory.CreateDirectory(backupDirectory);
        var installed = new List<string>();
        var backups = new List<(string Original, string Backup)>();
        var cleanupBackup = false;

        try
        {
            foreach (var finalPath in GetManagedPaths(finalBase).Where(File.Exists))
            {
                var backupPath = Path.Combine(backupDirectory, Path.GetFileName(finalPath));
                File.Move(finalPath, backupPath);
                backups.Add((finalPath, backupPath));
            }

            foreach (var stagedPath in GetManagedPaths(stagingBase).Where(File.Exists))
            {
                var finalPath = Path.Combine(outputDirectory, Path.GetFileName(stagedPath));
                File.Move(stagedPath, finalPath);
                installed.Add(finalPath);
            }

            cleanupBackup = true;
        }
        catch
        {
            cleanupBackup = RollBack(installed, backups);
            throw;
        }
        finally
        {
            if (cleanupBackup)
            {
                DeleteDirectoryBestEffort(backupDirectory);
            }
        }
    }

    private static bool RollBack(List<string> installed,
        List<(string Original, string Backup)> backups)
    {
        var restored = true;
        foreach (var path in installed)
        {
            try { File.Delete(path); }
            catch { restored = false; }
        }

        foreach (var (original, backup) in backups)
        {
            if (!File.Exists(backup)) continue;
            try { File.Move(backup, original); }
            catch { restored = false; }
        }

        return restored;
    }

    private static IEnumerable<string> GetManagedPaths(string basePath)
    {
        foreach (var geometry in new[] { "_point", "_line", "_polygon" })
        {
            foreach (var sidecar in ManagedSidecarSuffixes)
            {
                yield return basePath + geometry + sidecar;
            }
        }
    }

    private static void DeleteDirectoryBestEffort(string path)
    {
        try
        {
            if (Directory.Exists(path)) Directory.Delete(path, true);
        }
        catch
        {
            // A later run can clean temporary files held by another process.
        }
    }

    private static Feature ToNtsFeature(ShpFeature f)
    {
        var table = new AttributesTable();
        foreach (var pair in f.Attributes)
        {
            var v = pair.Value switch
            {
                null => "",
                string s when s.Length > MaxDbfTextLength => s.Substring(0, MaxDbfTextLength),
                _ => pair.Value,
            };
            table.Add(pair.Key, v);
        }

        return new Feature(f.Geometry, table);
    }
}
