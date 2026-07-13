using LayerExporter.Core.Geometry;
using LayerExporter.Core.Shp;
using NetTopologySuite.Geometries;
using NetTopologySuite.IO.Esri;
using Xunit;

namespace LayerExporter.Tests;

public class ShpWriterTests : IDisposable
{
    private readonly string _dir;

    public ShpWriterTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "shpwriter_tests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, true);
        }
        catch
        {
            // 임시 폴더 정리 실패는 무시
        }
    }

    private static Dictionary<string, object?> Attrs(string layer, string text = "")
    {
        return new Dictionary<string, object?>
        {
            ["Layer"] = layer,
            ["EntType"] = "Test",
            ["Handle"] = "ABC123",
            ["Text"] = text,
            ["Elev"] = 0.0,
        };
    }

    [Fact]
    public void Write_SeparatesGeometryTypesIntoFiles()
    {
        var features = new List<ShpFeature>
        {
            new(GeometryBuilder.BuildPoint(1, 2), Attrs("포인트레이어")),
            new(GeometryBuilder.BuildLineString([new Pt2(0, 0), new Pt2(10, 10)]), Attrs("라인레이어")),
            new(GeometryBuilder.TryBuildPolygon([new Pt2(0, 0), new Pt2(10, 0), new Pt2(10, 10), new Pt2(0, 10)], out _)!, Attrs("면레이어")),
        };

        var basePath = Path.Combine(_dir, "test");
        var result = ShpWriter.Write(basePath, features, null);

        Assert.Equal(3, result.WrittenFiles.Count);
        Assert.True(File.Exists(basePath + "_point.shp"));
        Assert.True(File.Exists(basePath + "_line.shp"));
        Assert.True(File.Exists(basePath + "_polygon.shp"));
        Assert.Equal(1, result.PointCount);
        Assert.Equal(1, result.LineCount);
        Assert.Equal(1, result.PolygonCount);
    }

    [Fact]
    public void RoundTrip_PreservesGeometryAndKoreanAttributes()
    {
        var features = new List<ShpFeature>
        {
            new(GeometryBuilder.BuildPoint(127.5, 38.0), Attrs("도로중심선", "한글 텍스트 테스트")),
        };

        var basePath = Path.Combine(_dir, "korean");
        ShpWriter.Write(basePath, features, null);

        var readBack = Shapefile.ReadAllFeatures(basePath + "_point.shp");
        Assert.Single(readBack);

        var feature = readBack[0];
        var point = Assert.IsType<Point>(feature.Geometry);
        Assert.Equal(127.5, point.X, 9);
        Assert.Equal(38.0, point.Y, 9);
        Assert.Equal("도로중심선", feature.Attributes["Layer"]);
        Assert.Equal("한글 텍스트 테스트", feature.Attributes["Text"]);
    }

    [Fact]
    public void Write_CreatesCpgFile()
    {
        var features = new List<ShpFeature>
        {
            new(GeometryBuilder.BuildPoint(0, 0), Attrs("test")),
        };

        var basePath = Path.Combine(_dir, "cpg");
        ShpWriter.Write(basePath, features, null);

        Assert.True(File.Exists(basePath + "_point.cpg"));
    }

    [Fact]
    public void Write_WithWkt_CreatesPrjFile()
    {
        const string wkt = "PROJCS[\"Korea_2000_Korea_Central_Belt_2010\",GEOGCS[\"GCS_Korea_2000\",DATUM[\"D_Korea_2000\",SPHEROID[\"GRS_1980\",6378137.0,298.257222101]],PRIMEM[\"Greenwich\",0.0],UNIT[\"Degree\",0.0174532925199433]],PROJECTION[\"Transverse_Mercator\"],PARAMETER[\"False_Easting\",200000.0],PARAMETER[\"False_Northing\",600000.0],PARAMETER[\"Central_Meridian\",127.0],PARAMETER[\"Scale_Factor\",1.0],PARAMETER[\"Latitude_Of_Origin\",38.0],UNIT[\"Meter\",1.0]]";
        var features = new List<ShpFeature>
        {
            new(GeometryBuilder.BuildPoint(200000, 600000), Attrs("test")),
        };

        var basePath = Path.Combine(_dir, "prj");
        ShpWriter.Write(basePath, features, wkt);

        var prjPath = basePath + "_point.prj";
        Assert.True(File.Exists(prjPath));
        Assert.Equal(wkt, File.ReadAllText(prjPath));
    }

    [Fact]
    public void Write_WithoutWkt_DoesNotCreatePrjFile()
    {
        var features = new List<ShpFeature>
        {
            new(GeometryBuilder.BuildPoint(0, 0), Attrs("test")),
        };

        var basePath = Path.Combine(_dir, "noprj");
        ShpWriter.Write(basePath, features, null);

        Assert.False(File.Exists(basePath + "_point.prj"));
    }

    [Fact]
    public void EmptyBuckets_ProduceNoFiles()
    {
        var features = new List<ShpFeature>
        {
            new(GeometryBuilder.BuildPoint(0, 0), Attrs("test")),
        };

        var basePath = Path.Combine(_dir, "onlypoint");
        var result = ShpWriter.Write(basePath, features, null);

        Assert.Single(result.WrittenFiles);
        Assert.False(File.Exists(basePath + "_line.shp"));
        Assert.False(File.Exists(basePath + "_polygon.shp"));
    }

    [Fact]
    public void Rewrite_RemovesFilesForBucketsNoLongerPresent()
    {
        var basePath = Path.Combine(_dir, "rewrite_buckets");
        ShpWriter.Write(basePath,
        [
            new(GeometryBuilder.BuildPoint(0, 0), Attrs("point")),
            new(GeometryBuilder.BuildLineString([new Pt2(0, 0), new Pt2(1, 1)]), Attrs("line")),
        ], null);

        var result = ShpWriter.Write(basePath,
        [
            new(GeometryBuilder.BuildPoint(2, 3), Attrs("point")),
        ], null);

        Assert.Single(result.WrittenFiles);
        Assert.True(File.Exists(basePath + "_point.shp"));
        Assert.False(File.Exists(basePath + "_line.shp"));
        Assert.False(File.Exists(basePath + "_line.dbf"));
        Assert.False(File.Exists(basePath + "_line.shx"));
        Assert.False(File.Exists(basePath + "_line.cpg"));
    }

    [Fact]
    public void Rewrite_WithoutWkt_RemovesStalePrjAndKnownSidecars()
    {
        var basePath = Path.Combine(_dir, "rewrite_prj");
        ShpWriter.Write(basePath,
        [
            new(GeometryBuilder.BuildPoint(0, 0), Attrs("point")),
        ], "TEST_WKT");
        File.WriteAllText(basePath + "_point.qix", "stale");
        File.WriteAllText(basePath + "_point.shp.xml", "stale");

        ShpWriter.Write(basePath,
        [
            new(GeometryBuilder.BuildPoint(1, 1), Attrs("point")),
        ], null);

        Assert.False(File.Exists(basePath + "_point.prj"));
        Assert.False(File.Exists(basePath + "_point.qix"));
        Assert.False(File.Exists(basePath + "_point.shp.xml"));
    }

    [Fact]
    public void FailureBeforeCommit_PreservesExistingOutputSet()
    {
        var basePath = Path.Combine(_dir, "preserve_on_failure");
        ShpWriter.Write(basePath,
        [
            new(GeometryBuilder.BuildPoint(7, 8), Attrs("original")),
        ], "ORIGINAL_WKT");
        var originalShp = File.ReadAllBytes(basePath + "_point.shp");
        var originalDbf = File.ReadAllBytes(basePath + "_point.dbf");

        Assert.Throws<InvalidOperationException>(() =>
            ShpWriter.Write(basePath, FeaturesThatFail(), null));

        Assert.Equal(originalShp, File.ReadAllBytes(basePath + "_point.shp"));
        Assert.Equal(originalDbf, File.ReadAllBytes(basePath + "_point.dbf"));
        Assert.Equal("ORIGINAL_WKT", File.ReadAllText(basePath + "_point.prj"));
    }

    [Fact]
    public void FailureDuringStaging_PreservesExistingOutputSet()
    {
        var basePath = Path.Combine(_dir, "preserve_on_write_failure");
        ShpWriter.Write(basePath,
        [
            new(GeometryBuilder.BuildPoint(7, 8), Attrs("original")),
        ], "ORIGINAL_WKT");
        var originalShp = File.ReadAllBytes(basePath + "_point.shp");
        var invalidAttributes = Attrs("replacement");
        invalidAttributes["Unsupported"] = new object();

        Assert.ThrowsAny<Exception>(() => ShpWriter.Write(basePath,
        [
            new(GeometryBuilder.BuildPoint(0, 0), invalidAttributes),
        ], null));

        Assert.Equal(originalShp, File.ReadAllBytes(basePath + "_point.shp"));
        Assert.Equal("ORIGINAL_WKT", File.ReadAllText(basePath + "_point.prj"));
    }

    private static IEnumerable<ShpFeature> FeaturesThatFail()
    {
        yield return new ShpFeature(GeometryBuilder.BuildPoint(0, 0), Attrs("replacement"));
        throw new InvalidOperationException("Simulated conversion failure.");
    }
}
