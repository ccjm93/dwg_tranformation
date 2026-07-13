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
    [CommandMethod("DwgToSHP", CommandFlags.Modal | CommandFlags.UsePickSet)]
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
                TryWriteMessage(doc, "\n[LayerExporter] 도면에 레이어가 없습니다.\n");
                return;
            }

            // 명령 실행 전 미리 선택해 둔 객체(pickfirst)를 객체 모드 초기 선택으로 시드한다.
            // 사전 프롬프트 없이 GUI를 먼저 띄운다.
            var implied = ed.SelectImplied();
            var pickedIds = implied.Status == PromptStatus.OK
                ? implied.Value.GetObjectIds()
                : Array.Empty<Autodesk.AutoCAD.DatabaseServices.ObjectId>();

            var vm = new ExportViewModel(layers, pickedIds)
            {
                OutputFolder = Path.GetDirectoryName(doc.Name) ?? "",
                BaseName = Path.GetFileNameWithoutExtension(doc.Name) is { Length: > 0 } n ? n : "export",
            };

            // 모델리스 창으로 띄운다 — 창을 열어둔 채 도면을 확대/축소/이동하고
            // 위성사진(GEOMAP)을 켜고 끄며 좌표를 확인한 뒤 객체 선택·내보내기를 진행할 수 있다.
            ExportDialog.ShowModeless(doc, vm);
        }
        catch (System.Exception ex)
        {
            TryWriteMessage(doc, $"\n[LayerExporter] 오류: {ex.Message}\n");
        }
    }

    /// <summary>
    /// 다이얼로그의 "내보내기"에서 호출된다. 모델리스 컨텍스트이므로 문서를 잠그고 내보낸다.
    /// 성공적으로 1개 이상 내보냈으면 true(창을 닫는다), 아니면 false(창 유지).
    /// </summary>
    public static async Task<bool> PerformExportAsync(Document doc, ExportViewModel vm)
    {
        var ed = doc.Editor;
        try
        {
            Autodesk.AutoCAD.DatabaseServices.ObjectIdCollection ids;
            using (doc.LockDocument())
            {
                ids = vm.IsObjectMode
                    ? new Autodesk.AutoCAD.DatabaseServices.ObjectIdCollection(vm.SelectedObjectIds
                        .Where(id => id.IsValid && !id.IsErased && id.Database == doc.Database)
                        .ToArray())
                    : LayerService.CollectEntityIds(doc.Database, vm.SelectedLayerNames);
            }

            if (ids.Count == 0)
            {
                TryWriteMessage(doc, "\n[LayerExporter] 내보낼 객체가 없습니다.\n");
                return false;
            }

            TryWriteMessage(doc, $"\n[LayerExporter] {ids.Count}개 객체를 내보냅니다.\n");

            var selectedFormatCount = 0;
            var successfulFormatCount = 0;

            if (vm.ExportDxf)
            {
                selectedFormatCount++;
                try
                {
                    using (doc.LockDocument())
                    {
                        ExportDxf(doc, ids, vm, ed);
                    }
                    successfulFormatCount++;
                }
                catch (System.Exception ex)
                {
                    TryWriteMessage(doc, $"\n[LayerExporter] DXF 내보내기 실패: {ex.Message}\n");
                }
            }

            if (vm.ExportShp)
            {
                selectedFormatCount++;
                try
                {
                    await ExportShpAsync(doc, ids, vm, ed);
                    successfulFormatCount++;
                }
                catch (System.Exception ex)
                {
                    TryWriteMessage(doc, $"\n[LayerExporter] SHP 내보내기 실패: {ex.Message}\n");
                }
            }

            if (selectedFormatCount > 0 && successfulFormatCount == selectedFormatCount)
            {
                return true;
            }

            if (successfulFormatCount > 0)
            {
                TryWriteMessage(doc,
                    "[LayerExporter] 일부 형식만 완료되었습니다. 실패한 형식을 확인하고 다시 시도하세요.\n");
            }
            else
            {
                TryWriteMessage(doc, "[LayerExporter] 선택한 형식을 내보내지 못했습니다. 창을 유지합니다.\n");
            }

            return false;
        }
        catch (System.Exception ex)
        {
            TryWriteMessage(doc, $"\n[LayerExporter] 오류: {ex.Message}\n");
            return false;
        }
    }
    private static void ExportDxf(Document doc, Autodesk.AutoCAD.DatabaseServices.ObjectIdCollection ids,
        ExportViewModel vm, Editor ed)
    {
        var path = Path.Combine(vm.OutputFolder, vm.BaseName + ".dxf");
        DxfExportService.Export(doc.Database, ids, path);
        TryWriteMessage(doc, $"\n[LayerExporter] DXF 내보내기 완료: {ids.Count}개 객체 → {path}\n");
        TryWriteMessage(doc, "[LayerExporter] 참고: Civil 3D 전용 객체(선형·지표면 등)는 다른 CAD에서 프록시로 표시될 수 있습니다.\n");
    }

    private static async Task ExportShpAsync(
        Document doc,
        Autodesk.AutoCAD.DatabaseServices.ObjectIdCollection ids,
        ExportViewModel vm,
        Editor ed)
    {
        CrsResolution crs;
        var options = new ConversionOptions(vm.Tolerance, vm.ClosedPolylinesAsPolygons, vm.IncludeZ);
        var basePath = Path.Combine(vm.OutputFolder, vm.BaseName);
        ShpExtractionResult extraction;

        using (var meter = new Autodesk.AutoCAD.Runtime.ProgressMeter())
        {
            meter.Start("SHP 내보내는 중");
            meter.SetLimit(ids.Count);
            try
            {
                using (doc.LockDocument())
                {
                    crs = CoordinateSystemResolver.Resolve();
                    extraction = ShpExportService.Extract(
                        doc.Database, ids, options, () => meter.MeterProgress());
                }
            }
            finally
            {
                meter.Stop();
            }
        }

        var summary = await Task.Run(
            () => ShpExportService.Write(basePath, extraction, crs.EsriWkt));

        TryWriteMessage(doc, $"\n[LayerExporter] SHP 내보내기 완료: 포인트 {summary.PointCount} / 라인 {summary.LineCount} / 폴리곤 {summary.PolygonCount}\n");
        foreach (var file in summary.WrittenFiles)
        {
            TryWriteMessage(doc, $"  - {file}\n");
        }

        if (summary.PrjWritten)
        {
            TryWriteMessage(doc, $"[LayerExporter] 좌표계 적용됨 ({crs.Code}, 출처: {crs.Source})\n");
        }
        else
        {
            TryWriteMessage(doc, $"[LayerExporter] 경고: .prj 미생성 - {crs.Source}\n");
        }

        if (summary.Skipped.Count > 0)
        {
            TryWriteMessage(doc, $"[LayerExporter] 변환 제외 {summary.Skipped.Count}개 객체:\n");
            foreach (var group in summary.Skipped.GroupBy(s => s.Reason))
            {
                TryWriteMessage(doc, $"  - {group.Key}: {group.Count()}개\n");
            }
        }
    }
    private static void TryWriteMessage(Document doc, string message)
    {
        try
        {
            doc.Editor.WriteMessage(message);
        }
        catch
        {
            // The originating document may close while detached SHP data is being written.
        }
    }
}
