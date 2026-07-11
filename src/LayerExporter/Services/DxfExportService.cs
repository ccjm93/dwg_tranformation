using Autodesk.AutoCAD.DatabaseServices;

namespace LayerExporter.Services;

/// <summary>선택 엔티티를 새 Database로 복제한 뒤 DXF로 저장한다.</summary>
public static class DxfExportService
{
    public static void Export(Database source, ObjectIdCollection ids, string path)
    {
        // noDocument=true: 문서에 연결되지 않은 순수 DB
        using var target = new Database(true, true);
        target.Insunits = source.Insunits;

        var mapping = new IdMapping();
        source.WblockCloneObjects(
            ids,
            SymbolUtilityServices.GetBlockModelSpaceId(target),
            mapping,
            DuplicateRecordCloning.Replace,
            false);

        // precision 16 = 최대 소수 자릿수
        target.DxfOut(path, 16, true);
    }
}
