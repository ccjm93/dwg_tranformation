using System.IO;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using LayerExporter.Crs;
using LayerExporter.Geometry;
using LayerExporter.Services;
using LayerExporter.UI;
using LayerExporter.UI.ViewModels;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace LayerExporter.Commands;

public class ExportLayersCommand
{
    [CommandMethod("EXPORTLAYERS", CommandFlags.Modal | CommandFlags.UsePickSet)]
    public void Run()
    {
        var doc = AcApp.DocumentManager.MdiActiveDocument;
        if (doc is null)
        {
            return;
        }

        var ed = doc.Editor;
        try
        {
            var layers = LayerService.GetLayers(doc.Database);
            if (layers.Count == 0)
            {
                ed.WriteMessage("\n[LayerExporter] 도면에 레이어가 없습니다.\n");
                return;
            }

            // 명령 실행 전 미리 선택해 둔 객체 (pickfirst)
            var implied = ed.SelectImplied();
            var pickedIds = implied.Status == PromptStatus.OK
                ? implied.Value.GetObjectIds()
                : Array.Empty<Autodesk.AutoCAD.DatabaseServices.ObjectId>();

            // 기본 흐름: 사전 선택이 없으면 화면에서 객체를 골라 레이어를 지정하게 한다
            HashSet<string>? preCheckedLayers = null;
            if (pickedIds.Length == 0)
            {
                var pso = new PromptSelectionOptions
                {
                    MessageForAdding = "\n내보낼 레이어의 객체 선택 <Enter = 건너뛰고 목록에서 직접 선택>",
                };
                var sel = ed.GetSelection(pso);
                if (sel.Status == PromptStatus.OK)
                {
                    preCheckedLayers = LayerService.GetLayerNames(doc.Database, sel.Value.GetObjectIds());
                    ed.WriteMessage($"\n[LayerExporter] {preCheckedLayers.Count}개 레이어가 체크됩니다: {string.Join(", ", preCheckedLayers)}\n");
                }
                else if (sel.Status == PromptStatus.Cancel)
                {
                    ed.WriteMessage("\n[LayerExporter] 취소되었습니다.\n");
                    return;
                }
                // 빈 Enter 등은 건너뛰고 다이얼로그에서 직접 선택
            }

            var vm = new ExportViewModel(layers, pickedIds.Length)
            {
                OutputFolder = Path.GetDirectoryName(doc.Name) ?? "",
                BaseName = Path.GetFileNameWithoutExtension(doc.Name) is { Length: > 0 } n ? n : "export",
            };
            if (preCheckedLayers is not null)
            {
                vm.SelectLayers(preCheckedLayers);
            }

            var dialog = new ExportDialog(vm);
            if (AcApp.ShowModalWindow(dialog) != true)
            {
                ed.WriteMessage("\n[LayerExporter] 취소되었습니다.\n");
                return;
            }

            using (doc.LockDocument())
            {
                Autodesk.AutoCAD.DatabaseServices.ObjectIdCollection ids;
                if (vm.HasPreselection && vm.UsePreselectionOnly)
                {
                    ids = new Autodesk.AutoCAD.DatabaseServices.ObjectIdCollection(pickedIds);
                    ed.WriteMessage($"\n[LayerExporter] 미리 선택한 {ids.Count}개 객체를 내보냅니다.\n");
                }
                else
                {
                    ids = LayerService.CollectEntityIds(doc.Database, vm.SelectedLayerNames);
                }

                if (ids.Count == 0)
                {
                    ed.WriteMessage("\n[LayerExporter] 내보낼 객체가 없습니다.\n");
                    return;
                }

                if (vm.Format == ExportFormat.Dxf)
                {
                    ExportDxf(doc, ids, vm, ed);
                }
                else
                {
                    ExportShp(doc, ids, vm, ed);
                }
            }
        }
        catch (System.Exception ex)
        {
            ed.WriteMessage($"\n[LayerExporter] 오류: {ex.Message}\n");
        }
    }

    private static void ExportDxf(Document doc, Autodesk.AutoCAD.DatabaseServices.ObjectIdCollection ids,
        ExportViewModel vm, Editor ed)
    {
        var path = Path.Combine(vm.OutputFolder, vm.BaseName + ".dxf");
        DxfExportService.Export(doc.Database, ids, path);
        ed.WriteMessage($"\n[LayerExporter] DXF 내보내기 완료: {ids.Count}개 객체 → {path}\n");
        ed.WriteMessage("[LayerExporter] 참고: Civil 3D 전용 객체(선형·지표면 등)는 다른 CAD에서 프록시로 표시될 수 있습니다.\n");
    }

    private static void ExportShp(Document doc, Autodesk.AutoCAD.DatabaseServices.ObjectIdCollection ids,
        ExportViewModel vm, Editor ed)
    {
        var crs = CoordinateSystemResolver.Resolve();
        var options = new ConversionOptions(vm.Tolerance, vm.ClosedPolylinesAsPolygons, vm.IncludeZ);
        var basePath = Path.Combine(vm.OutputFolder, vm.BaseName);

        using var meter = new Autodesk.AutoCAD.Runtime.ProgressMeter();
        meter.Start("SHP 내보내는 중");
        meter.SetLimit(ids.Count);
        try
        {
            var summary = ShpExportService.Export(
                doc.Database, ids, basePath, options, crs.EsriWkt,
                () => meter.MeterProgress());

            ed.WriteMessage($"\n[LayerExporter] SHP 내보내기 완료: 포인트 {summary.PointCount} / 라인 {summary.LineCount} / 폴리곤 {summary.PolygonCount}\n");
            foreach (var file in summary.WrittenFiles)
            {
                ed.WriteMessage($"  - {file}\n");
            }

            if (summary.PrjWritten)
            {
                ed.WriteMessage($"[LayerExporter] 좌표계 적용됨 ({crs.Code}, 출처: {crs.Source})\n");
            }
            else
            {
                ed.WriteMessage($"[LayerExporter] 경고: .prj 미생성 — {crs.Source}\n");
            }

            if (summary.Skipped.Count > 0)
            {
                ed.WriteMessage($"[LayerExporter] 변환 제외 {summary.Skipped.Count}개 객체:\n");
                foreach (var group in summary.Skipped.GroupBy(s => s.Reason))
                {
                    ed.WriteMessage($"  - {group.Key}: {group.Count()}개\n");
                }
            }
        }
        finally
        {
            meter.Stop();
        }
    }
}
