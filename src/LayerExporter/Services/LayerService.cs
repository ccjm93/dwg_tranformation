using Autodesk.AutoCAD.DatabaseServices;

namespace LayerExporter.Services;

public sealed record LayerInfo(string Name, short ColorIndex, bool IsLocked, bool IsFrozen, int EntityCount);

/// <summary>레이어 목록 조회와 선택 레이어의 모델스페이스 엔티티 수집.</summary>
public static class LayerService
{
    public static List<LayerInfo> GetLayers(Database db)
    {
        using var tr = db.TransactionManager.StartTransaction();

        // 레이어별 모델스페이스 엔티티 수 집계
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForRead);
        foreach (ObjectId id in ms)
        {
            if (tr.GetObject(id, OpenMode.ForRead, false) is Entity ent)
            {
                counts[ent.Layer] = counts.TryGetValue(ent.Layer, out var c) ? c + 1 : 1;
            }
        }

        var result = new List<LayerInfo>();
        var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
        foreach (ObjectId lid in lt)
        {
            var ltr = (LayerTableRecord)tr.GetObject(lid, OpenMode.ForRead);
            counts.TryGetValue(ltr.Name, out var count);
            result.Add(new LayerInfo(ltr.Name, ltr.Color.ColorIndex, ltr.IsLocked, ltr.IsFrozen, count));
        }

        tr.Commit();
        return result.OrderBy(l => l.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>선택된 엔티티들이 속한 레이어 이름 집합을 반환한다.</summary>
    public static HashSet<string> GetLayerNames(Database db, IEnumerable<ObjectId> ids)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using var tr = db.TransactionManager.StartTransaction();
        foreach (var id in ids)
        {
            if (tr.GetObject(id, OpenMode.ForRead, false) is Entity ent)
            {
                names.Add(ent.Layer);
            }
        }

        tr.Commit();
        return names;
    }

    /// <summary>
    /// 선택 레이어에 속한 모델스페이스 엔티티 ObjectId를 수집한다.
    /// 잠금/동결 레이어라도 사용자가 명시적으로 선택했으면 포함한다.
    /// </summary>
    public static ObjectIdCollection CollectEntityIds(Database db, IReadOnlyCollection<string> layerNames)
    {
        var selected = new HashSet<string>(layerNames, StringComparer.OrdinalIgnoreCase);
        var ids = new ObjectIdCollection();

        using var tr = db.TransactionManager.StartTransaction();
        var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForRead);
        foreach (ObjectId id in ms)
        {
            if (tr.GetObject(id, OpenMode.ForRead, false) is Entity ent && selected.Contains(ent.Layer))
            {
                ids.Add(id);
            }
        }

        tr.Commit();
        return ids;
    }
}
