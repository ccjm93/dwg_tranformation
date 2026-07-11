using LayerExporter.Core.Crs;
using Xunit;

namespace LayerExporter.Tests;

public class CrsCatalogTests
{
    [Fact]
    public void ResolveByAdskCode_ReturnsCentralBeltWkt()
    {
        var wkt = CrsCatalog.ResolveEsriWkt("KOREA2000-C-2010");
        Assert.NotNull(wkt);
        Assert.Contains("Korea_2000_Korea_Central_Belt_2010", wkt);
        Assert.Contains("127.0", wkt);
    }

    [Fact]
    public void ResolveByEpsgPrefix_Works()
    {
        var wkt = CrsCatalog.ResolveEsriWkt("EPSG:5186");
        Assert.NotNull(wkt);
        Assert.Contains("Central_Belt_2010", wkt);
    }

    [Fact]
    public void ResolveByBareNumber_Works()
    {
        var wkt = CrsCatalog.ResolveEsriWkt("5179");
        Assert.NotNull(wkt);
        Assert.Contains("Unified_Coordinate_System", wkt);
    }

    [Fact]
    public void CodeMatchingIsCaseInsensitive()
    {
        var wkt = CrsCatalog.ResolveEsriWkt("utmk");
        Assert.NotNull(wkt);
    }

    [Fact]
    public void UnknownCode_ReturnsNull()
    {
        Assert.Null(CrsCatalog.ResolveEsriWkt("NOT-A-REAL-CODE"));
        Assert.Null(CrsCatalog.ResolveEsriWkt(""));
    }

    [Fact]
    public void AllEntries_HaveValidWkt()
    {
        Assert.True(CrsCatalog.All.Count >= 8);
        foreach (var entry in CrsCatalog.All)
        {
            Assert.False(string.IsNullOrWhiteSpace(entry.EsriWkt));
            Assert.True(entry.EsriWkt.StartsWith("PROJCS[") || entry.EsriWkt.StartsWith("GEOGCS["));
        }
    }
}
