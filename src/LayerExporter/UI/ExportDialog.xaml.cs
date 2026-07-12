using System.Windows;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using LayerExporter.UI.ViewModels;
using Microsoft.Win32;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace LayerExporter.UI;

public partial class ExportDialog : Window
{
    public ExportDialog(ExportViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;

        // 명령 실행 전 미리 선택해 둔 객체를 다이얼로그가 열릴 때 도면에서 하이라이트로 유지하고,
        // 닫힐 때 하이라이트를 해제한다. 이렇게 하면 "화면에서 객체 선택"으로 추가할 때도
        // 기존 선택이 화면상에서 그대로 보인다.
        Loaded += (_, _) => HighlightSelected(true);
        Closed += (_, _) => HighlightSelected(false);
    }

    private ExportViewModel ViewModel => (ExportViewModel)DataContext;

    /// <summary>현재 객체 선택분을 도면에서 하이라이트하거나 해제한다. 부가 기능이므로 실패해도 무시한다.</summary>
    private void HighlightSelected(bool on)
    {
        var doc = AcApp.DocumentManager.MdiActiveDocument;
        if (doc is null || ViewModel.SelectedObjectIds.Count == 0)
        {
            return;
        }

        try
        {
            using (doc.LockDocument())
            using (var tr = doc.Database.TransactionManager.StartTransaction())
            {
                foreach (var id in ViewModel.SelectedObjectIds)
                {
                    if (id.IsValid && !id.IsErased
                        && tr.GetObject(id, OpenMode.ForRead) is Entity ent)
                    {
                        if (on)
                        {
                            ent.Highlight();
                        }
                        else
                        {
                            ent.Unhighlight();
                        }
                    }
                }

                tr.Commit();
            }
        }
        catch
        {
            // 하이라이트는 부가 기능 — 실패해도 내보내기에는 영향 없음
        }
    }

    private void OnSelectAllChecked(object sender, RoutedEventArgs e)
    {
        ViewModel.SetAllSelected(true);
    }

    private void OnSelectAllUnchecked(object sender, RoutedEventArgs e)
    {
        ViewModel.SetAllSelected(false);
    }

    /// <summary>
    /// 도면 좌표계를 지정한다. 실행 환경을 감지해 Civil 3D/Map이면 MAPCSASSIGN(Map 좌표계 코드 지정),
    /// 일반 AutoCAD면 GEOGRAPHICLOCATION(지오 위치 지정)으로 자동 전환한다.
    /// </summary>
    private void OnAssignCoordinateSystem(object sender, RoutedEventArgs e)
    {
        if (Crs.CoordinateSystemResolver.IsCivil3DAvailable())
        {
            RunAutoCadCommand("좌표계 지정(MAPCSASSIGN)", "_.MAPCSASSIGN");
        }
        else
        {
            RunAutoCadCommand("지오 위치 지정(GEOGRAPHICLOCATION)", "_.GEOGRAPHICLOCATION");
        }
    }

    /// <summary>위성사진(GEOMAP 하이브리드)을 켜서 좌표가 제대로 적용됐는지 확인한다.</summary>
    private void OnVerifyWithGeoMap(object sender, RoutedEventArgs e)
    {
        RunAutoCadCommand("좌표 확인(GEOMAP 위성사진)", "_.GEOMAP", "_Hybrid");
    }

    /// <summary>모달 다이얼로그를 잠시 숨기고 활성 도면에서 AutoCAD 명령을 동기 실행한다.</summary>
    private void RunAutoCadCommand(string label, params object[] command)
    {
        var doc = AcApp.DocumentManager.MdiActiveDocument;
        if (doc is null)
        {
            return;
        }

        var ed = doc.Editor;
        var interaction = ed.StartUserInteraction(this);
        try
        {
            ed.Command(command);
        }
        catch (Autodesk.AutoCAD.Runtime.Exception ex)
        {
            ed.WriteMessage($"\n[LayerExporter] {label} 실패: {ex.Message}\n");
        }
        catch (System.Exception ex)
        {
            ed.WriteMessage($"\n[LayerExporter] {label} 실패: {ex.Message}\n");
        }
        finally
        {
            interaction.End();
        }
    }

    private void OnPickObjectsFromScreen(object sender, RoutedEventArgs e)
    {
        var doc = AcApp.DocumentManager.MdiActiveDocument;
        if (doc is null)
        {
            return;
        }

        var ed = doc.Editor;
        // 다이얼로그를 잠시 숨기고 도면에서 객체를 선택하게 한다
        var interaction = ed.StartUserInteraction(this);
        try
        {
            var options = new PromptSelectionOptions
            {
                MessageForAdding = "\n내보낼 객체 선택 (기존 선택에 추가됨)",
            };
            var result = ed.GetSelection(options);
            if (result.Status != PromptStatus.OK)
            {
                return;
            }

            ViewModel.AddSelectedObjects(result.Value.GetObjectIds());
            // 기존 선택 + 새로 고른 객체 전체를 다시 하이라이트해 화면상 선택 상태를 유지한다.
            HighlightSelected(true);
            ed.WriteMessage($"\n[LayerExporter] 현재 {ViewModel.SelectedObjectCount}개 객체가 선택되었습니다.\n");
        }
        finally
        {
            interaction.End();
        }
    }

    private void OnPickLayersFromScreen(object sender, RoutedEventArgs e)
    {
        var doc = AcApp.DocumentManager.MdiActiveDocument;
        if (doc is null)
        {
            return;
        }

        var ed = doc.Editor;
        // 다이얼로그를 잠시 숨기고 도면에서 객체를 선택하게 한다
        var interaction = ed.StartUserInteraction(this);
        try
        {
            var options = new PromptSelectionOptions
            {
                MessageForAdding = "\n내보낼 레이어의 객체 선택",
            };
            var result = ed.GetSelection(options);
            if (result.Status != PromptStatus.OK)
            {
                return;
            }

            var layerNames = Services.LayerService.GetLayerNames(doc.Database, result.Value.GetObjectIds());
            ViewModel.SelectLayers(layerNames);
            ed.WriteMessage($"\n[LayerExporter] {layerNames.Count}개 레이어가 체크되었습니다: {string.Join(", ", layerNames)}\n");
        }
        finally
        {
            interaction.End();
        }
    }

    private void OnBrowseFolder(object sender, RoutedEventArgs e)
    {
#if NETFRAMEWORK
        // WPF OpenFolderDialog는 .NET 8+ 전용이므로 net48(2018–2024)에서는 WinForms 대화상자를 사용한다
        using var dialog = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = "출력 폴더 선택",
            SelectedPath = ViewModel.OutputFolder,
        };
        if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
        {
            ViewModel.OutputFolder = dialog.SelectedPath;
        }
#else
        var dialog = new OpenFolderDialog { Title = "출력 폴더 선택" };
        if (dialog.ShowDialog(this) == true)
        {
            ViewModel.OutputFolder = dialog.FolderName;
        }
#endif
    }

    private void OnExport(object sender, RoutedEventArgs e)
    {
        var error = ViewModel.Validate();
        if (error is not null)
        {
            MessageBox.Show(this, error, "레이어 내보내기", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        DialogResult = true;
    }
}
